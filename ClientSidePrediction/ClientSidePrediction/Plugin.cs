using API;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

// TODO(randomuserhi): predict animations

namespace ClientSidePrediction.BepInEx;

[BepInPlugin(Module.GUID, Module.Name, Module.Version)]
[BepInDependency(LocaliaCoreGUID, BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BasePlugin {
    const string LocaliaCoreGUID = "Localia.LocaliaCore";

    public override void Load() {
        APILogger.Log("Plugin is loaded!");
        harmony = new Harmony(Module.GUID);
        harmony.PatchAll();

        APILogger.Log("Debug is " + (ConfigManager.Debug ? "Enabled" : "Disabled"));

        ClassInjector.RegisterTypeInIl2Cpp<LatencyTracker>();
        ClassInjector.RegisterTypeInIl2Cpp<Prediction.EnemyPredict>();

        RundownManager.OnExpeditionGameplayStarted += (Action)LatencyTracker.OnGameplayStarted;

        new GameObject().AddComponent<LatencyTracker>();
        new Prediction();

        hasLocaliaCore = IL2CPPChainloader.Instance.Plugins.TryGetValue(LocaliaCoreGUID, out _);
    }

    public static bool hasLocaliaCore = false;

    private static Harmony? harmony;
}