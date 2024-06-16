using HarmonyLib;
using SNetwork;
using UnityEngine;

namespace ClientSidePrediction {
    [HarmonyPatch]
    internal class LatencyTracker : MonoBehaviour {
        private float timer = float.MaxValue;
        private static int ping = 500;
        public static float Ping => (SNet.Master == null || SNet.IsMaster) ? -1 : ping / 1000.0f;

        public void Update() {
            if (SNet.Master == null || SNet.IsMaster) {
                return;
            }

            if (timer > Time.time) {
                ping = 500;
            }
        }

        [HarmonyPatch(typeof(PUI_LocalPlayerStatus), nameof(PUI_LocalPlayerStatus.UpdateBPM))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void Initialize_Postfix(PUI_LocalPlayerStatus __instance) {
            __instance.m_pulseText.text += $" | {Ping} ms";
        }
    }
}
