using HarmonyLib;
using Player;
using SNetwork;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ClientSidePrediction {
    // TODO(randomuserhi): Fix
    [HarmonyPatch]
    internal class Forecast {
        private static float lerpFactor = 7.5f;
        private static Vector3 sentPos = Vector3.zero;
        private static Vector3 prevPos = Vector3.zero;
        private static Vector3 oldVel = Vector3.zero;
        private static CharacterController? characterController = null;
        private static long prevTimestamp = 0;
        //private static GameObject? marker;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ExpDecay(Vector3 a, Vector3 b, float decay, float dt) {
            return b + (a - b) * Mathf.Exp(-decay * dt);
        }

        [HarmonyPatch(typeof(PlayerSync), nameof(PlayerSync.SendLocomotion))]
        [HarmonyPrefix]
        private static void Prefix_SendLocomotion(PlayerSync __instance, PlayerLocomotion.PLOC_State state, ref Vector3 pos, Vector3 lookDir, float velFwd, float velRight) {
            if (SNet.IsMaster) return;

            switch (state) {
            case PlayerLocomotion.PLOC_State.OnTerminal:
            case PlayerLocomotion.PLOC_State.Downed:
            case PlayerLocomotion.PLOC_State.InElevator:
                return;
            }

            long now = LatencyTracker.Now;
            float dt = Mathf.Min(now - prevTimestamp, 1000.0f) / 1000.0f;
            Vector3 vel = (pos - prevPos) / dt;
            prevPos = pos;
            prevTimestamp = now;

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2.0f;
            if (ping <= 0) return;

            if (__instance.m_agent.Locomotion.m_input.sqrMagnitude <= 0.01) {
                // If no movement keys are currently pressed, don't forecast (prevents broken stealth)

                sentPos = pos; // snap to actual position

                // pos = sentPos; // maintain last sent position

                return;
            }

            if ((pos - prevPos).sqrMagnitude > 9) {
                // If moved very far, then must be TP, no forecasting...
                sentPos = pos;
                return;
            }

            if (Vector3.Dot(oldVel, vel) < 0) {
                // If the player switches direction, reset sent pos to prevent lag in forecast
                sentPos = pos;
            }
            oldVel = vel;

            Vector3 target = pos + vel * ping;
            sentPos = ExpDecay(sentPos, target, lerpFactor * Mathf.Max((sentPos - target).magnitude, 1.0f), dt);

            // adjust sent pos based on collision
            if (characterController == null) {
                characterController = __instance.m_agent.Cast<LocalPlayerAgent>().m_playerCharacterController.m_characterController;
            }
            if (characterController == null) return;
            Vector3 prev = characterController.transform.position;

            if (!characterController.isGrounded) {
                // Fix y cause predicting gravity is funky
                sentPos.y = pos.y;
            }

            characterController.Move(sentPos - pos);
            sentPos = characterController.transform.position;
            characterController.transform.position = prev;

            /*
            const float height = 1.85f;
            const float skinDepth = 0.01f;
            const float radius = 0.5f;
            RaycastHit hit;
            if (Physics.CapsuleCast(sentPos + Vector3.up * radius, pos + Vector3.up * (height - radius), radius - skinDepth, slide, out hit, slide.magnitude)) {
                sentPos = slide.normalized * hit.distance;
            }
            */

            /*if (marker == null) {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.GetComponent<Collider>().enabled = false;
                marker.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                marker.GetComponent<MeshRenderer>().material.color = Color.yellow;
            }
            marker.transform.position = sentPos;*/

            pos = sentPos;
        }
    }
}