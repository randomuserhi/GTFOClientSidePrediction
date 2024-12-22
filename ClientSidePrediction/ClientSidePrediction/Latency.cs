using HarmonyLib;
using Player;
using SNetwork;
using UnityEngine;

namespace ClientSidePrediction {
    // TODO(randomuserhi): Fix
    [HarmonyPatch]
    internal class LatencyTracker : MonoBehaviour {
        private static float ping = 0;
        public static float Ping => (SNet.Master == null || SNet.IsMaster) ? 0 : ping / 1000.0f;

        public static long Now => ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
        private static long send = 0;
        private static bool expecting = false;

        private float timer = 0;
        public void Update() {
            if (SNet.Master == null || SNet.IsMaster) {
                return;
            }

            timer += Time.deltaTime;
            if (timer > 1) {
                send = Now;
                expecting = true;
                PlayerChatManager.WantToSentTextMessage(PlayerManager.GetLocalPlayerAgent(), "Ping Check...");
                timer = 0;
            }
        }

        [HarmonyPatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.AddLogItem))]
        [HarmonyPrefix]
        public static void ReceivedMessage() {
            if (expecting == false) return;

            long receive = Now;

            const float alpha = 0.75f;
            ping = alpha * ping + (1.0f - alpha) * (receive - send);

            if (ping < 0) {
                ping = 0;
            }
            expecting = false;
        }

        [HarmonyPatch(typeof(PlayerChatManager), nameof(PlayerChatManager.PostMessage))]
        [HarmonyPrefix]
        public static void SendMessage() {
            send = Now;
            expecting = true;
        }

        [HarmonyPatch(typeof(PUI_LocalPlayerStatus), nameof(PUI_LocalPlayerStatus.UpdateBPM))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void Initialize_Postfix(PUI_LocalPlayerStatus __instance) {
            __instance.m_pulseText.text += $" | {ping} ms";
        }
    }
}
