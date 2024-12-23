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
        private static long sendTime = 0;
        private static int index = 0;
        private static bool expected = false;
        private static bool running = false;

        private static pNavMarkerState visibleState = default;
        private static float markerHideTimer = 0;
        private static bool show = false;

        private static float timer = 0;

        public static void OnGameplayStarted() {
            timer = 0;
            running = true;
            visibleState.status = eNavMarkerStatus.Hidden;
        }

        [HarmonyPatch(typeof(RundownManager), nameof(RundownManager.EndGameSession))]
        [HarmonyPrefix]
        private static void EndGameSession() {
            running = false;
        }

        [HarmonyPatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.LeaveHub))]
        [HarmonyPrefix]
        private static void LeaveHub() {
            running = false;
        }

        [HarmonyPatch(typeof(GuiManager), nameof(GuiManager.AttemptSetPlayerPingStatus))]
        [HarmonyPrefix]
        private static void GuiManagerHide(PlayerAgent sourceAgent, bool visible, Vector3 worldPos, eNavMarkerStyle style) {
            if (!visible) {
                markerHideTimer = Clock.Time - 1;
                visibleState.status = eNavMarkerStatus.Hidden;
            } else {
                show = false;
            }
        }

        [HarmonyPatch(typeof(LocalPlayerAgent), nameof(LocalPlayerAgent.Update))]
        [HarmonyPrefix]
        private static void Update(LocalPlayerAgent __instance) {
            if (SNet.IsMaster) return;
            if (!running) return;

            index = __instance.Owner.PlayerSlotIndex();
            var pingManager = GuiManager.Current.m_playerPings[index];

            timer += Time.deltaTime;
            if (timer > 1) {
                timer = 0;

                if (Clock.Time > markerHideTimer) {
                    visibleState.status = eNavMarkerStatus.Hidden;

                    if (!expected && visibleState.status == eNavMarkerStatus.Hidden) {
                        pNavMarkerInteraction pingPacket = default;
                        pingPacket.type = eNavMarkerInteractionType.Hide;

                        sendTime = Now;
                        expected = true;
                        pingManager.m_stateReplicator.AttemptInteract(pingPacket);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SyncedNavMarkerWrapper), nameof(SyncedNavMarkerWrapper.OnStateChange))]
        [HarmonyPrefix]
        private static void OnRecievePingStatus(SyncedNavMarkerWrapper __instance, pNavMarkerState oldState, pNavMarkerState newState, bool isDropinState) {
            if (SNet.IsMaster) return;
            if (!running) return;
            if (__instance.m_playerIndex != index) return;

            if (!show && Clock.Time > markerHideTimer && newState.status == eNavMarkerStatus.Visible) {
                visibleState = newState;
                markerHideTimer = Clock.Time + __instance.AutoHideDelay;
                show = true;
                expected = false;
                return;
            } else if (show && Clock.Time <= markerHideTimer && newState.status == eNavMarkerStatus.Hidden && visibleState.status == eNavMarkerStatus.Visible) {
                expected = false;
                pNavMarkerInteraction pingPacket = default;
                pingPacket.type = eNavMarkerInteractionType.Show;
                pingPacket.style = visibleState.style;
                pingPacket.worldPos = visibleState.worldPos;
                pingPacket.terminalItemId = visibleState.terminalItemId;

                __instance.m_stateReplicator.AttemptInteract(pingPacket);
                return;
            }

            if (!expected) return;

            expected = false;

            long receiveTime = Now;

            float target = receiveTime - sendTime;
            const float alpha = 0.5f;
            ping = Mathf.Clamp(alpha * ping + (1.0f - alpha) * target, 0f, 1000.0f);
        }


        /*[HarmonyPatch(typeof(PUI_GameEventLog), nameof(PUI_GameEventLog.AddLogItem))]
        [HarmonyPrefix]
        private static void ReceivedMessage() {
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
        private static void SendMessage() {
            send = Now;
            expecting = true;
        }*/

        [HarmonyPatch(typeof(PUI_LocalPlayerStatus), nameof(PUI_LocalPlayerStatus.UpdateBPM))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void Initialize_Postfix(PUI_LocalPlayerStatus __instance) {
            __instance.m_pulseText.text += $" | {ping} ms";
        }
    }
}
