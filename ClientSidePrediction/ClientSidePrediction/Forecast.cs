using HarmonyLib;
using Player;
using SNetwork;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ClientSidePrediction {
    // TODO(randomuserhi): Fix
    [HarmonyPatch]
    internal class Forecast {
        public static float lerpFactor = 7.5f;
        private static Vector3 sentPos = Vector3.zero;
        private static Vector3 prevPos = Vector3.zero;
        private static Vector3 oldVel = Vector3.zero;
        private static long prevTimestamp = 0;

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
            if (ping < 0) return;

            if ((pos - prevPos).sqrMagnitude > 5) {
                // If moved very far, then must be TP, no forecasting...
                return;
            }

            if (Vector3.Dot(oldVel, vel) < 0) {
                // If the player switches direction, reset sent pos to prevent lag in forecast
                sentPos = pos;
            }
            oldVel = vel;

            Vector3 target = pos + vel * ping;
            sentPos = ExpDecay(sentPos, target, lerpFactor * Mathf.Max((sentPos - target).sqrMagnitude / 0.5f, 1.0f), dt);

            // Fix y cause gravity is funky
            sentPos.y = pos.y;

            pos = sentPos;
        }
    }
}