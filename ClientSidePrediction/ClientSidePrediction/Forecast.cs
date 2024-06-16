/*using API;
using HarmonyLib;
using Player;
using SNetwork;
using UnityEngine;*/

namespace ClientSidePrediction {
    /*[HarmonyPatch]
    internal class Forecast {
        private static long Now => ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();

        private static long prevTime = 0;
        private static Vector3 prevPos = Vector3.zero;

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

            long now = Now;
            float dt = (now - prevTime) / 1000.0f;
            if (dt > 1) {
                prevPos = pos;
                prevTime = now;
                return;
            }
            Vector3 vel = (pos - prevPos) / dt;

            prevPos = pos;
            prevTime = now;

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2;
            if (ping < 0) return;
            APILogger.Debug($"BEFORE: {pos.x} {pos.y} {pos.z}");
            pos += vel * ping;
            APILogger.Debug($"AFTER: {pos.x} {pos.y} {pos.z}");
        }
    }*/
}
