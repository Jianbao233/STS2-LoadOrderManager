using System;
using Godot;
using HarmonyLib;

namespace LoadOrderManager;

/// <summary>
/// Mod entry point.
///
/// This assembly is the *implementation*; it is loaded by the ModVersionLoader launcher
/// that occupies the root &lt;ModId&gt;.dll slot, which then reflects into
/// <see cref="Initialize"/>. Therefore this class must NOT be decorated with
/// [ModuleInitializer] (that would initialise twice) nor [ModInitializer] (the launcher
/// calls us explicitly at the same point in the load sequence).
/// </summary>
public static class LoadOrderManagerMod
{
    public const string ModId = "LoadOrderManager";

    /// <summary>Set by ModVersionLoader so the implementation can log which bundle won.</summary>
    private const string SelectedVersionEnvVar = "AMS_LOADER_SELECTED_VERSION";

    private static bool _initialized;
    private static bool _harmonyPatched;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        DebugLog.Info("Initialize called.");
        DebugLog.Info($"Version bundle: {ResolveVersionBundle()}.");
        DebugLog.Info($"Log file: {DebugLog.LogPath}");
        ApplyHarmonyPatches();
        DebugLog.Info("Loaded.");

        // Inert unless LOADORDER_SELFTEST is set (see SelfTest.cs).
        SelfTest.Schedule();
    }

    /// <summary>
    /// Which implementation directory the launcher picked (e.g. "g0.107.1"), or
    /// "unknown" when the mod is running outside the version-bundle layout.
    /// </summary>
    private static string ResolveVersionBundle()
    {
        try
        {
            var selected = System.Environment.GetEnvironmentVariable(SelectedVersionEnvVar);
            return string.IsNullOrWhiteSpace(selected) ? "unknown (no launcher)" : selected;
        }
        catch
        {
            return "unknown";
        }
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
