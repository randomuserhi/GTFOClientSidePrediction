using BepInEx.Unity.IL2CPP.Hook;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ClientSidePrediction {
    /*[HarmonyPatch]
    internal class TonguePrediction : MonoBehaviour {
        // AgentAbility.Melee, base.m_ai.m_enemyAgent.EnemyBehaviorData.MeleeAttackDistance.Max

        [HarmonyPatch(typeof(EnemySync), nameof(EnemySync.UpdateStateData))]
        [HarmonyPostfix]
        private static void OnUpdate(EnemySync __instance) {
            EnemyAgent agent = __instance.m_agent;
            EB_InCombat? combat = agent.AI.m_behaviour.TryCast<EB_InCombat>();
            if (combat == null) return;

            if (combat.CheckAbilityReady(AgentAbility.Melee, agent.EnemyBehaviorData.MeleeAttackDistance.Max)) {
                APILogger.Warn("bruh");
                agent.Locomotion.StrikerAttack.ActivateState(agent.AI.Target.m_agent, AgentAbility.Melee, agent.Locomotion.StrikerAttack.m_lastAttackIndex);
            }
        }
    }*/

    [HarmonyPatch]
    internal class Prediction {
        private unsafe static Interpolate Patch_Interpolate = Patch;
#pragma warning disable CS8618
        private static Interpolate Original_Interpolate;
#pragma warning restore CS8618
        private unsafe delegate Vector3* Interpolate(Vector3* _Vector3Ptr, IntPtr _thisPtr, float t, Il2CppMethodInfo* _);

        public unsafe Prediction() {
            INativeClassStruct val = UnityVersionHandler.Wrap((Il2CppClass*)(void*)Il2CppClassPointerStore<PositionSnapshotBuffer<pES_PathMoveData>>.NativeClassPtr);
            for (int i = 0; i < val.MethodCount; i++) {
                INativeMethodInfoStruct val2 = UnityVersionHandler.Wrap(val.Methods[i]);
                if (Marshal.PtrToStringAnsi(val2.Name) == nameof(PositionSnapshotBuffer<pES_PathMoveData>.Interpolate)) {
                    INativeDetour.CreateAndApply(val2.MethodPointer, Patch_Interpolate, out Original_Interpolate);
                    break;
                }
            }
        }

        private unsafe static Vector3* Patch(Vector3* _Vector3Ptr, IntPtr _thisPtr, float t, Il2CppMethodInfo* _) {
            Vector3* position = Original_Interpolate(_Vector3Ptr, _thisPtr, t, _);
            PositionSnapshotBuffer<pES_PathMoveData> snapshotBuffer = new PositionSnapshotBuffer<pES_PathMoveData>(_thisPtr);
            Il2CppSystem.Collections.Generic.List<pES_PathMoveData> buffer = snapshotBuffer.m_buffer;
            if (buffer.Count <= 1) return position;

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2.0f;
            if (ping < 0) return position;

            pES_PathMoveData a = buffer[buffer.Count - 1];
            pES_PathMoveData b = buffer[buffer.Count - 2];
            Vector3 dir = b.Position - a.Position;

            const float dt = 0.1f;

            *position = *position + dir / dt * ping;
            return position;
        }
    }
}
