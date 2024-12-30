using API;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

// TODO(randomuserhi): predict animations

namespace ClientSidePrediction.BepInEx;

[BepInPlugin(Module.GUID, Module.Name, Module.Version)]
public class Plugin : BasePlugin {
    public override void Load() {
        APILogger.Log("Plugin is loaded!");
        harmony = new Harmony(Module.GUID);
        harmony.PatchAll();

        APILogger.Log("Debug is " + (ConfigManager.Debug ? "Enabled" : "Disabled"));

        ClassInjector.RegisterTypeInIl2Cpp<LatencyTracker>();

        RundownManager.OnExpeditionGameplayStarted += (Action)LatencyTracker.OnGameplayStarted;

        new GameObject().AddComponent<LatencyTracker>();
        new Prediction();
    }

    private static Harmony? harmony;
}

// Shove players for fun (Should not be put in final build lmfao)
/*[HarmonyPatch]
internal static class Patches {
    private static bool shoved = false;

    private static System.Collections.IEnumerator RepeatShove(PlayerAgent player, pFullDamageData push, int count) {
        for (int i = 0; i < count; ++i) {
            player.Damage.m_meleeDamagePacket.Send(push, SNetwork.SNet_ChannelType.GameNonCritical, player.Owner);
            yield return new WaitForSeconds(1.5f);
        }
    }

    [HarmonyPatch(typeof(MWS_Push), nameof(MWS_Push.Enter))]
    [HarmonyPostfix]
    private static void Shove_Enter(MWS_Push __instance) {
        shoved = false;
    }

    [HarmonyPatch(typeof(MWS_Push), nameof(MWS_Push.Update))]
    [HarmonyPostfix]
    private static void Shove_Update(MWS_Push __instance) {
        if (shoved) return;

        if (__instance.m_elapsed > __instance.m_data.m_damageStartTime && __instance.m_elapsed <= __instance.m_data.m_damageEndTime && !shoved) {
            PlayerAgent player = __instance.m_weapon.Owner;

            float sqrDist = __instance.m_weapon.MeleeArchetypeData.PushDamageSphereRadius * __instance.m_weapon.MeleeArchetypeData.PushDamageSphereRadius;

            pFullDamageData push = default(pFullDamageData);
            push.damage.Set(0, player.Damage.DamageMax);
            push.source.Set(player);
            push.localPosition.Set(Vector3.zero, 10f);
            push.direction.Value = player.TargetLookDir;

            foreach (PlayerAgent other in PlayerManager.PlayerAgentsInLevel) {
                if (player == other) continue;
                if ((player.Position - other.Position).sqrMagnitude > sqrDist) continue;

                other.Damage.m_meleeDamagePacket.Send(push, SNetwork.SNet_ChannelType.GameNonCritical, other.Owner);
                other.StartCoroutine(RepeatShove(other, push, 2).WrapToIl2Cpp());

                shoved = true;
            }
        }
    }
}*/