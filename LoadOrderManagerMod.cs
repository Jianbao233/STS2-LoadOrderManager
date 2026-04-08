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

        ApplyHarmonyPatches();
        GD.Print("[LoadOrderManager] Loaded.");
    }

    internal static void ApplyHarmonyPatches()
    {
        if (_harmonyPatched) return;
        _harmonyPatched = true;

        try
        {
            var harmony = new Harmony(ModId);
            harmony.PatchAll();
            GD.Print("[LoadOrderManager] Harmony patches applied.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadOrderManager] Harmony patch failed: {ex}");
        }
    }
}
