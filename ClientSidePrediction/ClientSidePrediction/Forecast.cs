// #define ENABLE_ON_MASTER
// #define ENABLE_DEBUG_MARKER

using HarmonyLib;
using Player;
#if !ENABLE_ON_MASTER
using SNetwork;
#endif
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

        [HarmonyPatch(typeof(PlayerSync), nameof(PlayerSync.SendLocomotion))]
        [HarmonyPrefix]
        private static void Prefix_SendLocomotion(PlayerSync __instance, PlayerLocomotion.PLOC_State state, ref Vector3 pos, Vector3 lookDir, float velFwd, float velRight) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

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

                // NOTE(randomuserhi): GTFO has interpolation on the received position which counts towards enemy detection,
                //                     thus snapping the position does not fully fix the issue, and option (2) may be better.

                sentPos = pos; // option 1) snap to actual position

                // pos = sentPos; // option 2) maintain last sent position

                return;
            }

            if ((pos - prevPos).sqrMagnitude > 9) {
                // If moved very far, then must be TP, no forecasting...
                sentPos = pos;
                return;
            }
            
            oldVel = vel;

            Vector3 target = pos + vel * ping;
            sentPos = pos + vel * ping;

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

            pos = sentPos;
        }

#if ENABLE_DEBUG_MARKER
        private static GameObject? marker;
        [HarmonyPatch(typeof(PlayerSync), nameof(PlayerSync.SendLocomotion))]
        [HarmonyPostfix]
        private static void Postfix_SendLocomotion(PlayerSync __instance, PlayerLocomotion.PLOC_State state, Vector3 pos, Vector3 lookDir, float velFwd, float velRight) {
            if (marker == null) {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.GetComponent<Collider>().enabled = false;
                marker.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                marker.GetComponent<MeshRenderer>().material.color = Color.yellow;
            }
            marker.transform.position = pos;
        }
#endif
    }
}
