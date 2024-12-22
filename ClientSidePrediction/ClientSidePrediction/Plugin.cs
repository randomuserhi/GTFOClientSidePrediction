using API;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BetterChat;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ClientSidePrediction.BepInEx;

[BepInPlugin(Module.GUID, Module.Name, Module.Version)]
[BepInDependency(BetterChat.Module.GUID, BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BasePlugin {
    public override void Load() {
        APILogger.Log("Plugin is loaded!");
        harmony = new Harmony(Module.GUID);
        harmony.PatchAll();

        APILogger.Log("Debug is " + (ConfigManager.Debug ? "Enabled" : "Disabled"));

        ClassInjector.RegisterTypeInIl2Cpp<LatencyTracker>();
        //ClassInjector.RegisterTypeInIl2Cpp<TonguePrediction>();

        new GameObject().AddComponent<LatencyTracker>();
        new Prediction();

        if (IL2CPPChainloader.Instance.Plugins.TryGetValue(BetterChat.Module.GUID, out _)) {
            APILogger.Debug("BetterChat is installed, adding commands.");
            ChatLogger.root.AddCommand("Forecast/lerp", new ChatLogger.Command() {
                action = (ChatLogger.CmdNode n, ChatLogger.Command cmd, string[] args) => {
                    if (args.Length == 0) {
                        n.Debug(cmd.help);
                        return;
                    }
                    float value;
                    if (float.TryParse(args[0], out value)) {
                        Forecast.lerpFactor = value;
                        n.Debug($"{value}");
                    } else {
                        n.Debug(cmd.help);
                        return;
                    }
                },
                description = "",
                syntax = "<value>"
            });
        }
    }

    private static Harmony? harmony;
}