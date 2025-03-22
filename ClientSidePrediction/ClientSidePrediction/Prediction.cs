// #define ENABLE_ON_MASTER
// #define ENABLE_DEBUG_MARKER

using Agents;
using API;
using BepInEx.Unity.IL2CPP.Hook;
using ClientSidePrediction.BepInEx;
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

// TODO(randomuserhi): Cleanup code to use GetComponent on the monobehaviour -> might be faster than pointer lookup?

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

        public class EnemyPredict : MonoBehaviour {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
            public EnemyAgent agent;
            public NavMeshAgent navMeshAgent;
            public EnemyAI ai;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor.

            public Vector3 prevPos = Vector3.zero;
            public Vector3 vel = Vector3.zero;
            public long prevTimestamp;
            public int lastAnimIndex = 0;
            public float lastReceivedAttack = 0;
            public float triggeredTongue = 0;
            public float lastSound = 0;
            public bool predictTongue = false;
            public AgentAbility type = AgentAbility.Melee;
            public Vector3 targetPos;
            public uint lastReceivedTick = uint.MaxValue;

            public long desyncTimestamp = 0;

#if ENABLE_DEBUG_MARKER
            public GameObject marker;
#endif

            public void Setup(EnemyAgent agent) {
                this.agent = agent;
                ai = agent.AI;
                navMeshAgent = agent.AI.m_navMeshAgent.Cast<NavMeshAgentExtention.NavMeshAgentProxy>().m_agent;

                predictTongue = CheckAbilityTypeHasTongue(AgentAbility.Melee);
                if (!predictTongue) {
                    predictTongue = CheckAbilityTypeHasTongue(AgentAbility.Ranged);
                    type = AgentAbility.Ranged;
                }

                if (predictTongue && ConfigManager.DisableTonguePredictOnEnemiesWithMelee) {
                    if (CheckAbilityTypeHasMelee(AgentAbility.Melee) || CheckAbilityTypeHasMelee(AgentAbility.Ranged)) {
                        predictTongue = false;
                        APILogger.Debug("Disabled tongue for enemy as it has a melee ability.");
                    }
                }

                targetPos = agent.transform.position;

#if ENABLE_DEBUG_MARKER
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.GetComponent<Collider>().enabled = false;
                marker.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                marker.GetComponent<MeshRenderer>().material.color = Color.blue;
#endif
            }

            private void FixedUpdate() {
                // TODO(randomuserhi): Edit slide animation to start slow then speed up (players normally expect enemies to be still during windup animation after all)
                //                     this way they still get a period of windup anim where the enemy is slow.

                float windupDuration = agent.Locomotion.AnimHandle.TentacleAttackWindUpLen / agent.Locomotion.AnimSpeedOrg;
                if (Clock.Time < triggeredTongue + windupDuration) {
                    // Slide enemy to correct location during tongue prediction (in case prediction is incorrect)
                    agent.transform.position = ExpDecay(agent.transform.position, targetPos, 0.5f, Time.fixedDeltaTime);
                }
            }

            private bool CheckAbilityTypeHasTongue(AgentAbility type) {
                EnemyAbility? ability = agent.Abilities.GetAbility(type);
                if (ability == null) return false;

                EAB_MovingEnemeyTentacle? tentacle = ability.TryCast<EAB_MovingEnemeyTentacle>();
                if (tentacle == null) return false;

                return tentacle.m_type == eTentacleEnemyType.Striker;
            }

            private bool CheckAbilityTypeHasMelee(AgentAbility type) {
                EnemyAbility? ability = agent.Abilities.GetAbility(type);
                if (ability == null) return false;

                //EAB_MeleeStrike? melee = ability.TryCast<EAB_MeleeStrike>();
                EAB_StrikerMelee? melee = ability.TryCast<EAB_StrikerMelee>();
                if (melee == null) return false;

                return true;
            }
        }

        public static Dictionary<IntPtr, EnemyPredict> map = new Dictionary<IntPtr, EnemyPredict>();

        [HarmonyPatch(typeof(ES_StrikerAttack), nameof(ES_StrikerAttack.RecieveAttackStart))]
        [HarmonyPrefix]
        private static void ES_StrikerAttack_RecieveAttackStart(ES_StrikerAttack __instance, pES_EnemyAttackData attackData) {
#if !ENABLE_ON_MASTER
            if (SNet.IsMaster) return;
#endif

            var pathmove = __instance.m_enemyAgent.Locomotion.PathMove.TryCast<ES_PathMove>();
            if (pathmove == null) return;
            IntPtr ptr = pathmove.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return;

            EnemyPredict enemy = map[ptr];

            if (enemy.lastReceivedAttack < 0) return; // NOTE(randomuserhi): Ignore calls due to prediction

            enemy.lastReceivedAttack = Clock.Time;
        }

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

            EnemyPredict enemy = map[ptr];
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
                EnemyPredict data = __instance.m_agent.gameObject.AddComponent<EnemyPredict>();
                data.Setup(__instance.m_agent);
                map.Add(__instance.m_agent.Locomotion.PathMove.Cast<ES_PathMove>().m_positionBuffer.Pointer, data);
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

        private const float lerpFactor = 5f;

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

            EnemyPredict enemy = map[ptr];
            enemy.targetPos = incomingData.Position;

            float windupDuration = enemy.agent.Locomotion.AnimHandle.TentacleAttackWindUpLen / enemy.agent.Locomotion.AnimSpeedOrg;
            if (Clock.Time < enemy.triggeredTongue + windupDuration) {
                __instance.m_positionBuffer.Push(incomingData);
                enemy.prevTimestamp = LatencyTracker.Now;
                enemy.prevPos = incomingData.Position;

                if (enemy.lastReceivedAttack < 0 && Clock.Time - enemy.triggeredTongue > LatencyTracker.Ping * 1.5f) {
                    // Check if we did not recieve an actual attack packet after predicted tongue (within expected delay) then
                    // cancel out of tongue animation.
                    APILogger.Debug("Mispredicted tongue animation!");
                    return true;
                }

                // Don't interrupt predicted animation with recieved data
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

            EnemyPredict enemy = map[ptr];

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

            if ((int)LatencyTracker.ping < ConfigManager.TonguePredictThreshold) {
                return;
            }

            IntPtr ptr = __instance.m_positionBuffer.Pointer;

            if (!map.ContainsKey(ptr)) return;

            EnemyPredict enemy = map[ptr];

            if (!enemy.predictTongue) return; // If no tongue ability, early return

            PlayerAgent player = PlayerManager.GetLocalPlayerAgent();

            // NOTE(randomuserhi): Add a small amount of leeway to prediction to make it consistent
            float dist = (enemy.type == AgentAbility.Melee
                ? enemy.agent.EnemyBehaviorData.MeleeAttackDistance.Max
                : enemy.agent.EnemyBehaviorData.RangedAttackDistance.Max) * 1.05f;
            float sqrDist = dist * dist;

            Vector3 dir = player.AimTarget.position - enemy.agent.EyePosition;

            if (dir.sqrMagnitude > sqrDist && enemy.triggeredTongue != 0) {
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
                    if (dir.sqrMagnitude < dist * dist) {
                        // Line of Sight check
                        Vector3 eyePos = enemy.agent.EyePosition;
                        dir.y = 0.0001f;
                        dir.Normalize();
                        float viewDot = Vector3.Dot(dir.normalized, enemy.agent.transform.forward);

                        bool occluded = Physics.Linecast(eyePos, player.EyePosition, LayerManager.MASK_WORLD);

                        if ((dir.sqrMagnitude < 4 || viewDot > 0.45f) && !occluded) {
                            // Tongue is ready...
                            pES_EnemyAttackData fakedata = default;
                            fakedata.Position = enemy.agent.Position;
                            fakedata.TargetPosition = player.AimTarget.position;
                            fakedata.TargetAgent.Set(player);
                            fakedata.AnimIndex = (byte)enemy.agent.Locomotion.GetUniqueAnimIndex(EnemyLocomotion.s_hashAbilityFires, ref enemy.lastAnimIndex);
                            fakedata.AbilityType = AgentAbility.Melee;

                            enemy.lastReceivedAttack = -1;
                            enemy.agent.Locomotion.StrikerAttack.RecieveAttackStart(fakedata);

                            enemy.triggeredTongue = Clock.Time;
                        }
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

            EnemyPredict enemy = map[ptr];
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

            EnemyPredict enemy = map[_thisPtr];

            PositionSnapshotBuffer<pES_PathMoveData> snapshotBuffer = new PositionSnapshotBuffer<pES_PathMoveData>(_thisPtr);
            Il2CppSystem.Collections.Generic.List<pES_PathMoveData> buffer = snapshotBuffer.m_buffer;

            if (enemy.lastReceivedTick == snapshotBuffer.m_lastReceivedTick) return position;
            enemy.lastReceivedTick = snapshotBuffer.m_lastReceivedTick;

            long now = LatencyTracker.Now;
            float dt = Mathf.Clamp01((now - enemy.prevTimestamp) / 1000.0f);
            enemy.prevTimestamp = now;

            Vector3 dir = *position - enemy.prevPos;
            enemy.prevPos = *position;

            if (dt <= 0) return position;

            enemy.vel = dir / dt;

            const float maxPredictDist = 5.0f;

            Vector3 target = *position + Vector3.ClampMagnitude(enemy.vel * ping, maxPredictDist);

            enemy.navMeshAgent.destination = target;

#if ENABLE_DEBUG_MARKER
            enemy.marker.transform.position = enemy.navMeshAgent.pathEndPosition;
#else
            *position = enemy.navMeshAgent.pathEndPosition;

            // Fail safe if enemy is too far from real position
            if ((*position - enemy.prevPos).sqrMagnitude > maxPredictDist * maxPredictDist) {
                *position = enemy.prevPos;
            }
#endif

            return position;
        }

#if ENABLE_DEBUG_MARKER
        private static EnemyAgent? selectedEnemy = null;
        [HarmonyPatch(typeof(Dam_EnemyDamageBase), nameof(Dam_EnemyDamageBase.MeleeDamage))]
        [HarmonyPrefix]
        public static void Prefix_EnemyReceiveMeleeDamage(Dam_EnemyDamageBase __instance) {
            if (SNet.IsMaster) return;

            selectedEnemy = __instance.Owner;
        }

        [HarmonyPatch(typeof(PUI_LocalPlayerStatus), nameof(PUI_LocalPlayerStatus.UpdateBPM))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void Initialize_Postfix(PUI_LocalPlayerStatus __instance) {
            if (SNet.IsMaster) return;

            if (selectedEnemy == null) return;

            IntPtr ptr = selectedEnemy.Locomotion.PathMove.Cast<ES_PathMove>().m_positionBuffer.Pointer;
            if (!map.ContainsKey(ptr)) return;
            EnemyData enemy = map[ptr];

            __instance.m_pulseText.text += $" | {enemy.vel.x} {enemy.vel.y} {enemy.vel.z} | {enemy.marker.transform.position.x} {enemy.marker.transform.position.y} {enemy.marker.transform.position.z}";
        }
#endif
    }

}
