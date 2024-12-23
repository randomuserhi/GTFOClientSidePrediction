using API;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

// TODO(randomuserhi): predict animations
// TODO(randomuserhi): automatic ping detection cause rn using chat is stupid

namespace ClientSidePrediction.BepInEx;

[BepInPlugin(Module.GUID, Module.Name, Module.Version)]
public class Plugin : BasePlugin {
    public override void Load() {
        APILogger.Log("Plugin is loaded!");
        harmony = new Harmony(Module.GUID);
        harmony.PatchAll();

        APILogger.Log("Debug is " + (ConfigManager.Debug ? "Enabled" : "Disabled"));

        ClassInjector.RegisterTypeInIl2Cpp<LatencyTracker>();
        //ClassInjector.RegisterTypeInIl2Cpp<MovementPrediction>();

        RundownManager.OnExpeditionGameplayStarted += (Action)LatencyTracker.OnGameplayStarted;

        new GameObject().AddComponent<LatencyTracker>();
        new Prediction();
    }

    private static Harmony? harmony;
}