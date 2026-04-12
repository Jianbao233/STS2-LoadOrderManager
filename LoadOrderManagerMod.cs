using System;
using Godot;
using HarmonyLib;

namespace LoadOrderManager;

public static class LoadOrderManagerMod
{
    public const string ModId = "LoadOrderManager";
    private static bool _initialized;
    private static bool _harmonyPatched;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        DebugLog.Info("Initialize called.");
        DebugLog.Info($"Log file: {DebugLog.LogPath}");
        LoadOrderRuntime.LogDiagnosticsOnStartup();
        ApplyHarmonyPatches();
        DebugLog.Info("Loaded.");
    }

    internal static void ApplyHarmonyPatches()
    {
        if (_harmonyPatched) return;
        _harmonyPatched = true;

        try
        {
            var harmony = new Harmony(ModId);
            harmony.PatchAll();
            DebugLog.Info("Harmony patches applied.");
        }
        catch (Exception ex)
        {
            DebugLog.Error("Harmony patch failed.", ex);
        }
    }
}
