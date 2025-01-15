using Agents;
using BepInEx.Unity.IL2CPP.Hook;
using Enemies;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.MethodInfo;
using Player;
using ShaderValueAnimation;
using SNetwork;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AI;

namespace ClientSidePrediction {

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

        // Fix glow issue:
        [HarmonyPatch(typeof(Vector4Transition), nameof(Vector4Transition.FeedMaterial))]
        [HarmonyPrefix]
        private static void FeedMaterial(Vector4Transition __instance, Il2CppSystem.Collections.Generic.List<ISVA_MaterialHolder> mats, Vector4 value, ref float t) {
            if (float.IsNaN(t)) {
                t = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3 ExpDecay(Vector3 a, Vector3 b, float decay, float dt) {
            return b + (a - b) * Mathf.Exp(-decay * dt);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ExpDecay(float a, float b, float decay, float dt) {
            return b + (a - b) * Mathf.Exp(-decay * dt);
        }

        public class EnemyData {
            public EnemyAgent agent;
            public NavMeshAgent navMeshAgent;
            public EnemyAI ai;
            public Vector3 prevPos = Vector3.zero;
            public Vector3 vel = Vector3.zero;
            public long prevTimestamp;
            public int lastAnimIndex = 0;
            public float triggeredTongue = 0;
            public float lastSound = 0;
            public bool hasTongue = false;

            // public GameObject marker;

            public EnemyData(EnemyAgent agent) {
                this.agent = agent;
                ai = agent.AI;
                navMeshAgent = agent.AI.m_navMeshAgent.Cast<NavMeshAgentExtention.NavMeshAgentProxy>().m_agent;

                hasTongue = CheckAbilityTypeHasTongue(AgentAbility.Melee);
                if (!hasTongue) {
                    hasTongue = CheckAbilityTypeHasTongue(AgentAbility.Ranged);
                }

                /*marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.GetComponent<Collider>().enabled = false;
                marker.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                marker.GetComponent<MeshRenderer>().material.color = Color.blue;*/
            }

            private bool CheckAbilityTypeHasTongue(AgentAbility type) {
                EnemyAbility? ability = agent.Abilities.GetAbility(type);
                if (ability == null) return false;

                EAB_MovingEnemeyTentacle? tentacle = ability.TryCast<EAB_MovingEnemeyTentacle>();
                if (tentacle == null) return false;

                return tentacle.m_type == eTentacleEnemyType.Striker;
            }
        }

        public static Dictionary<IntPtr, EnemyData> map = new Dictionary<IntPtr, EnemyData>();

        [HarmonyPatch(typeof(ES_StrikerAttack), nameof(ES_StrikerAttack.OnAttackWindUp))]
        [HarmonyPrefix]
        private static bool ES_StrikerAttack_OnAttackWindUp(ES_StrikerAttack __instance, int attackIndex, AgentAbility abilityType, int abilityIndex) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return true;
#endif

            var pathmove = __instance.m_enemyAgent.Locomotion.PathMove.TryCast<ES_PathMove>();
            if (pathmove == null) return true;

            IntPtr ptr = pathmove.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return true;

            EnemyData enemy = map[ptr];
            if (Clock.Time < enemy.lastSound + 0.5f) {
                __instance.m_tentacleAbility = __instance.m_ai.m_enemyAgent.Abilities.GetAbility(abilityType).Cast<EAB_MovingEnemeyTentacle>();
                __instance.m_locomotion.m_animator.CrossFadeInFixedTime(EnemyLocomotion.s_hashAbilityFires[attackIndex], __instance.m_enemyAgent.EnemyMovementData.BlendIntoAttackAnim);
                __instance.m_enemyAgent.Appearance.InterpolateGlow(__instance.m_attackGlowColor, __instance.m_attackGlowLocationEnd, __instance.m_attackWindupDuration * 1.2f);
                return false;
            }

            enemy.lastSound = Clock.Time;
            return true;
        }

        [HarmonyPatch(typeof(EnemySync), nameof(EnemySync.OnSpawn))]
        [HarmonyPostfix]
        private static void OnSpawn(EnemySync __instance) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

            var pathmove = __instance.m_agent.Locomotion.PathMove.TryCast<ES_PathMove>();
            if (pathmove != null) {
                map.Add(__instance.m_agent.Locomotion.PathMove.Cast<ES_PathMove>().m_positionBuffer.Pointer, new EnemyData(__instance.m_agent));
            }
        }

        [HarmonyPatch(typeof(EnemySync), nameof(EnemySync.OnDespawn))]
        [HarmonyPostfix]
        private static void OnDespawn(EnemySync __instance) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

            var pathmove = __instance.m_agent.Locomotion.PathMove.TryCast<ES_PathMove>();
            if (pathmove != null) {
                map.Remove(pathmove.m_positionBuffer.Pointer);
            }
        }
        [HarmonyPatch(typeof(EnemyBehaviour), nameof(EnemyBehaviour.ChangeState), new Type[] { typeof(EB_States) })]
        [HarmonyPrefix]
        private static void Behaviour_ChangeState(EnemyBehaviour __instance, EB_States state) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

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

#if true

        [HarmonyPatch(typeof(ES_PathMove), nameof(ES_PathMove.RecieveStateData))]
        [HarmonyPrefix]
        private static bool ES_PathMove_RecieveStateData(ES_PathMove __instance, pES_PathMoveData incomingData) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return true;
#endif

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2.0f;
            if (ping <= 0) return true;

            IntPtr ptr = __instance.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return true;

            EnemyData enemy = map[ptr];
            if (Clock.Time < enemy.triggeredTongue + 3.0f) {
                long now = LatencyTracker.Now;
                float dt = (now - enemy.prevTimestamp) / 1000.0f;
                enemy.prevTimestamp = now;

                lerpFactor = 1f;
                __instance.m_positionBuffer.Push(incomingData);
                return false;
            }

            return true;
        }

        [HarmonyPatch(typeof(ES_PathMove), nameof(ES_PathMove.SyncEnter))]
        [HarmonyPostfix]
        private static void ES_PathMove_SyncEnter(ES_PathMove __instance) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

            IntPtr ptr = __instance.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return;

            EnemyData enemy = map[ptr];

            enemy.prevPos = __instance.m_enemyAgent.Position;
            enemy.prevTimestamp = LatencyTracker.Now;
            enemy.vel = Vector3.zero;

            enemy.ai.m_navMeshAgent.enabled = true;
        }

        [HarmonyPatch(typeof(ES_PathMove), nameof(ES_PathMove.SyncUpdate))]
        [HarmonyPostfix]
        private static void ES_PathMove_SyncUpdate(ES_PathMove __instance) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

            IntPtr ptr = __instance.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return;

            EnemyData enemy = map[ptr];

            if (!enemy.hasTongue) return; // If no tongue ability, early return

            PlayerAgent player = PlayerManager.GetLocalPlayerAgent();

            float dist = enemy.agent.EnemyBehaviorData.MeleeAttackDistance.Max * 1.05f;
            float sqrDist = dist * dist;
            if ((enemy.agent.Position - player.transform.position).sqrMagnitude > sqrDist && enemy.triggeredTongue != 0) {
                // if out of range, enable tongue prediction
                enemy.triggeredTongue = 0;
            }

            if (enemy.triggeredTongue != 0) {
                // Early return if prediction is disabled
                return;
            }

            // Predict state switch to tongue attack:
            if (!enemy.ai.m_navMeshAgent.isOnOffMeshLink) {
                // Bail if we are on a off mesh link, otherwise continue with prediction

                if (enemy.ai.m_target != null && enemy.ai.m_target.m_agent == player) {
                    // Enemy has local player as target
                    if ((enemy.agent.transform.position - player.transform.position).sqrMagnitude < dist * dist) {
                        // Tongue is ready...

                        pES_EnemyAttackData fakedata = default;
                        fakedata.Position = enemy.agent.Position;
                        fakedata.TargetPosition = player.AimTarget.position;
                        fakedata.TargetAgent.Set(player);
                        fakedata.AnimIndex = (byte)enemy.agent.Locomotion.GetUniqueAnimIndex(EnemyLocomotion.s_hashAbilityFires, ref enemy.lastAnimIndex);
                        fakedata.AbilityType = AgentAbility.Melee;

                        enemy.agent.Locomotion.StrikerAttack.RecieveAttackStart(fakedata);

                        enemy.triggeredTongue = Clock.Time;
                    }
                }
            }
        }

        // NOTE(randomuserhi): Patching SyncExit causes crash with EnemyAnimationFix
        [HarmonyPatch(typeof(ES_PathMove), nameof(ES_PathMove.Exit))]
        [HarmonyPostfix]
        private static void ES_PathMove_Exit(ES_PathMove __instance) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

            IntPtr ptr = __instance.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return;

            EnemyData enemy = map[ptr];
            enemy.ai.m_navMeshAgent.enabled = false;
        }

#endif

        private unsafe static Vector3* Patch(Vector3* _Vector3Ptr, IntPtr _thisPtr, float t, Il2CppMethodInfo* _) {
            Vector3* position = Original_Interpolate(_Vector3Ptr, _thisPtr, t, _);
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return position;
#endif

            if (!map.ContainsKey(_thisPtr)) {
                return position;
            }

            float ping = Mathf.Min(LatencyTracker.Ping, 1f) / 2.0f;
            if (ping <= 0) return position;

            EnemyData enemy = map[_thisPtr];

            // enemy.marker.transform.position = *position;

            long now = LatencyTracker.Now;
            float dt = (now - enemy.prevTimestamp) / 1000.0f;
            enemy.prevTimestamp = now;

            lerpFactor = ExpDecay(lerpFactor, 5.0f, 0.5f, dt);

            Vector3 dir = *position - enemy.prevPos;
            enemy.prevPos = *position;

            if (dt <= 0) return position;

            enemy.vel = ExpDecay(enemy.vel, dir / dt, lerpFactor, dt);

            const float maxPredictDist = 5.0f;

            Vector3 target = *position + Vector3.ClampMagnitude(enemy.vel * ping, maxPredictDist);

            enemy.navMeshAgent.destination = target;

            *position = enemy.navMeshAgent.pathEndPosition;

            // Fail safe if enemy is too far from real position
            if ((*position - enemy.prevPos).sqrMagnitude > maxPredictDist * maxPredictDist + 0.1f) {
                *position = enemy.prevPos;
            }

            return position;
        }
    }

}
