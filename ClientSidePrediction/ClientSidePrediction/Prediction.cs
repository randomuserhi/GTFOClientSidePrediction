using BepInEx.Unity.IL2CPP.Hook;
using Enemies;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using SNetwork;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AI;

namespace ClientSidePrediction {
    /*[HarmonyPatch]
    internal class MovementPrediction : MonoBehaviour {
        [HarmonyPatch(typeof(EnemySync), nameof(EnemySync.OnSpawn))]
        [HarmonyPostfix]
        private static void OnSpawn(EnemySync __instance) {
            if (!__instance.m_agent.gameObject.GetComponent<MovementPrediction>()) {
                MovementPrediction sync = __instance.m_agent.gameObject.AddComponent<MovementPrediction>();
                sync.enemy = __instance.m_agent;
                sync.pathmove = __instance.m_agent.Locomotion.PathMove.TryCast<ES_PathMove>();
            }
        }

        public EnemyAgent? enemy;
        public ES_PathMove? pathmove;
        private Vector3 prevPos = Vector3.zero;
        private Vector3 oldVel = Vector3.zero;
        private static float lerpFactor = 7.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ExpDecay(Vector3 a, Vector3 b, float decay, float dt) {
            return b + (a - b) * Mathf.Exp(-decay * dt);
        }

        private void Update() {
            if (SNet.IsMaster) return;
            if (enemy == null || pathmove == null) return;
            if (pathmove.m_positionBuffer.m_buffer.Count < 1) return;

            Vector3 pos = pathmove.m_positionBuffer.m_buffer[pathmove.m_positionBuffer.m_buffer.Count - 1].Position;

            long now = LatencyTracker.Now;
            Vector3 vel = (pos - prevPos) / Time.deltaTime;
            prevPos = pos;

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2.0f;
            if (ping <= 0) return;

            if ((pos - prevPos).sqrMagnitude > 5) {
                // If moved very far, then must be TP, no prediction...
                return;
            }

            if (Vector3.Dot(oldVel, vel) < 0) {
                // If the player switches direction, reset sent pos to prevent lag in forecast
                enemy.Position = pos;
            }
            oldVel = vel;

            Vector3 target = pos + vel * ping;
            enemy.Position = ExpDecay(enemy.Position, target, lerpFactor * Mathf.Max((enemy.Position - target).sqrMagnitude, 1.0f), Time.deltaTime);
        }
    }*/

    [HarmonyPatch]
    internal class Prediction {
        [HarmonyPatch(typeof(RundownManager), nameof(RundownManager.EndGameSession))]
        [HarmonyPrefix]
        private static void EndGameSession() {
            map.Clear();
        }

        [HarmonyPatch(typeof(SNet_SessionHub), nameof(SNet_SessionHub.LeaveHub))]
        [HarmonyPrefix]
        private static void LeaveHub() {
            map.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ExpDecay(Vector3 a, Vector3 b, float decay, float dt) {
            return b + (a - b) * Mathf.Exp(-decay * dt);
        }

        public class EnemyData {
            public EnemyAgent agent;
            public Vector3 prevPos = Vector3.zero;
            public Vector3 vel = Vector3.zero;
            //public GameObject test;
            public long prevTimestamp;

            public EnemyData(EnemyAgent agent) {
                this.agent = agent;
                /*test = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                test.GetComponent<Collider>().enabled = false;
                test.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                test.GetComponent<MeshRenderer>().material.color = Color.red;*/
            }
        }

        public static Dictionary<IntPtr, EnemyData> map = new Dictionary<IntPtr, EnemyData>();


        [HarmonyPatch(typeof(EnemySync), nameof(EnemySync.OnSpawn))]
        [HarmonyPostfix]
        private static void OnSpawn(EnemySync __instance) {
            if (SNet.IsMaster) return;
            var pathmove = __instance.m_agent.Locomotion.PathMove.TryCast<ES_PathMove>();
            if (pathmove != null) {
                map.Add(__instance.m_agent.Locomotion.PathMove.Cast<ES_PathMove>().m_positionBuffer.Pointer, new EnemyData(__instance.m_agent));
            }
        }

        [HarmonyPatch(typeof(EnemySync), nameof(EnemySync.OnDespawn))]
        [HarmonyPostfix]
        private static void OnDespawn(EnemySync __instance) {
            if (SNet.IsMaster) return;
            var pathmove = __instance.m_agent.Locomotion.PathMove.TryCast<ES_PathMove>();
            if (pathmove != null) {
                map.Remove(pathmove.m_positionBuffer.Pointer);
            }
        }
        [HarmonyPatch(typeof(EnemyBehaviour), nameof(EnemyBehaviour.ChangeState), new Type[] { typeof(EB_States) })]
        [HarmonyPrefix]
        private static void Behaviour_ChangeState(EnemyBehaviour __instance, EB_States state) {
            if (SNet.IsMaster) return;
            if (__instance.m_currentStateName != state && state == EB_States.Dead) {
                var pathmove = __instance.m_ai.m_enemyAgent.Locomotion.PathMove.TryCast<ES_PathMove>();
                if (pathmove != null) {
                    map.Remove(pathmove.m_positionBuffer.Pointer);
                }
            }
        }

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

        private static float lerpFactor = 5f;

        [HarmonyPatch(typeof(ES_PathMove), nameof(ES_PathMove.CommonEnter))]
        [HarmonyPrefix]
        private static void ES_PATHMOVE(ES_PathMove __instance) {
            if (SNet.IsMaster) return;

            IntPtr ptr = __instance.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return;

            EnemyData enemy = map[ptr];

            enemy.prevPos = __instance.m_enemyAgent.Position;
            enemy.prevTimestamp = LatencyTracker.Now;
            enemy.vel = Vector3.zero;
        }

        private unsafe static Vector3* Patch(Vector3* _Vector3Ptr, IntPtr _thisPtr, float t, Il2CppMethodInfo* _) {
            Vector3* position = Original_Interpolate(_Vector3Ptr, _thisPtr, t, _);
            if (SNet.IsMaster) return position;

            if (!map.ContainsKey(_thisPtr)) {
                return position;
            }

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2.0f;
            if (ping <= 0) return position;

            EnemyData enemy = map[_thisPtr];

            //enemy.test.transform.position = *position;

            long now = LatencyTracker.Now;
            float dt = (now - enemy.prevTimestamp) / 1000.0f;
            enemy.prevTimestamp = now;

            Vector3 dir = *position - enemy.prevPos;
            enemy.prevPos = *position;

            if (dt <= 0) return position;

            enemy.vel = ExpDecay(enemy.vel, dir / dt, lerpFactor, dt);

            *position = *position + enemy.vel * ping;

            if (NavMesh.SamplePosition(*position, out var hit, 1f, 1)) {
                *position = hit.position;
            }

            if (NavMesh.Raycast(*position + Vector3.up, *position + Vector3.down, out hit, 1)) {
                *position = hit.position;
            }

            return position;
        }
    }
}
