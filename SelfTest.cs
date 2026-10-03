using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;

namespace LoadOrderManager;

/// <summary>
/// Automated, environment-guarded self-test.
///
/// It is completely inert unless <c>LOADORDER_SELFTEST</c> is set, which makes it safe to
/// ship: the released mod does nothing here on a normal launch. It exists so the mod can be
/// verified end-to-end inside a *headless* game process (no window, no audio, no user input):
///
///   LOADORDER_SELFTEST=panel   build the panel, log the layout verdict, quit
///   LOADORDER_SELFTEST=full    panel + presets migration + per-preset new-mod policy,
///                              optionally applying a preset
///   LOADORDER_SELFTEST_WRITE=1                allow writing settings.save (Apply)
///   LOADORDER_SELFTEST_APPLY=&lt;preset name&gt;  preset to Apply in
///   LOADORDER_SELFTEST_PROBE=&lt;mod id&gt;       mod treated as "the newly subscribed one"
///   LOADORDER_UI_COMPACT / LOADORDER_UI_SIMULATE_H   see LoadOrderPanel
///
/// Invoke with: SlayTheSpire2.exe --headless --quit-after 60000
///
/// Driving note: the heartbeat is a chain of SceneTreeTimers rather than a node _Process or
/// the SceneTree.ProcessFrame signal. Both of those were observed to stop firing partway
/// through the game's startup (the tree gets paused and node processing is suspended), which
/// silently stalled the test. Timers are serviced by the SceneTree itself.
/// </summary>
internal static partial class SelfTest
{
    private const string ModeEnv = "LOADORDER_SELFTEST";
    private const string WriteEnv = "LOADORDER_SELFTEST_WRITE";
    private const string ApplyEnv = "LOADORDER_SELFTEST_APPLY";
    private const string ProbeEnv = "LOADORDER_SELFTEST_PROBE";
    private const string AutoPresetName = "SelfTest";
    private const double TickSeconds = 0.25;

    private static string _mode = string.Empty;
    private static bool _write;
    private static string _applyPreset = string.Empty;
    private static string _probeId = "LomProbe";

    private static bool _scheduled;
    private static bool _ticking;
    private static bool _finished;
    private static LoadOrderPanel? _panel;

    private static int _step;
    private static int _waitTicks;
    private static int _readyTicks;
    private static int _ticks;

    public static void Schedule()
    {
        if (_scheduled) return;

        var mode = ReadEnv(ModeEnv);
        if (string.IsNullOrWhiteSpace(mode)) return; // inert by default

        _mode = mode.Trim().ToLowerInvariant();
        _write = ReadEnv(WriteEnv) == "1";
        _applyPreset = ReadEnv(ApplyEnv)?.Trim() ?? string.Empty;
        var probe = ReadEnv(ProbeEnv);
        if (!string.IsNullOrWhiteSpace(probe)) _probeId = probe.Trim();

        _scheduled = true;

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Log("no SceneTree; aborted");
            return;
        }

        Log($"scheduled: mode={_mode}, write={_write}, apply='{_applyPreset}', probe={_probeId}");
        StartTicking(tree);
    }

    private static void StartTicking(SceneTree tree)
    {
        if (_ticking || _finished) return;
        _ticking = true;
        ChainNextTick(tree);
    }

    private static void ChainNextTick(SceneTree tree)
    {
        if (_finished) return;

        var timer = tree.CreateTimer(TickSeconds, processAlways: true);
        timer.Timeout += () =>
        {
            _ticking = false;
            Tick(tree);
        };
    }

    private static void Tick(SceneTree tree)
    {
        try
        {
            _ticks++;

            if (!SaveManagerReady())
            {
                if (++_readyTicks % 40 == 0)
                {
                    Log($"waiting for SaveManager.SettingsSave.ModSettings... ({_readyTicks} ticks)");
                }

                if (_readyTicks > 400)
                {
                    Log("timed out waiting for the save system; aborted");
                    Finish(tree);
                    return;
                }

                StartTicking(tree);
                return;
            }

            if (_waitTicks > 0)
            {
                _waitTicks--;
                StartTicking(tree);
                return;
            }

            Step(tree);
        }
        catch (Exception ex)
        {
            Log($"FAILED: {ex}");
            Finish(tree);
        }
    }

    private static void Finish(SceneTree tree)
    {
        if (_finished) return;
        _finished = true;
        Log("=== self-test end ===");
        Log("quitting");
        tree.Quit();
    }

    private static void Step(SceneTree tree)
    {
        switch (_step)
        {
            case 0:
                Log($"=== self-test begin === (sceneTreePaused={tree.Paused})");
                _panel = new LoadOrderPanel { Name = "LoadOrderManager_SelfTestPanel" };
                tree.Root.AddChild(_panel);
                _panel.OpenPanel();
                Log("panel opened");
                _waitTicks = 2;
                _step = 1;
                StartTicking(tree);
                return;

            case 1:
                _panel!.DebugLogLayoutCheck();
                ReportEntries("initial");
                if (_mode == "panel")
                {
                    Finish(tree);
                    return;
                }
                EnsureAutoPreset();
                ReportProfiles("after-ensure");
                SelectPresetByName("Default");
                ReportEntries("Default");
                SelectPresetByName(AutoPresetName);
                ReportEntries(AutoPresetName);
                _waitTicks = 2;
                _step = 2;
                StartTicking(tree);
                return;

            case 2:
                if (_write && !string.IsNullOrEmpty(_applyPreset))
                {
                    SelectPresetByName(_applyPreset);
                    ReportEntries($"before-apply({_applyPreset})");
                    _panel!.DebugApplyOrder();
                    Log($"applied preset '{_applyPreset}'");
                    _waitTicks = 4;
                    _step = 3;
                    StartTicking(tree);
                    return;
                }

                Log("no Apply requested (set LOADORDER_SELFTEST_WRITE=1 and LOADORDER_SELFTEST_APPLY=<preset>)");
                Finish(tree);
                return;

            case 3:
                _panel!.DebugLogLayoutCheck();
                ReportSavedState();
                ReportEntries($"after-apply({_applyPreset})");
                Finish(tree);
                return;

            default:
                Finish(tree);
                return;
        }
    }

    // ── Reporting ────────────────────────────────────────

    private static void ReportEntries(string stage)
    {
        var entries = _panel?.DebugEntries;
        if (entries == null)
        {
            Log($"{stage}: no entries");
            return;
        }

        var profile = ModPresetStore.CurrentProfile;
        Log($"{stage}: preset='{profile.Name}' autoEnableNewMods={profile.AutoEnableNewMods} " +
            $"known={profile.KnownMods.Count} disabled={profile.DisabledMods.Count} entries={entries.Count}");

        var probe = entries.FirstOrDefault(e =>
            string.Equals(e.Id, _probeId, StringComparison.OrdinalIgnoreCase));
        if (probe == null)
        {
            Log($"{stage}: {_probeId} NOT in list");
            return;
        }

        Log($"{stage}: {_probeId} known={profile.KnownMods.Contains(probe.Id)} " +
            $"enabled={probe.IsEnabled} isNew={probe.IsNew}");
    }

    private static void ReportProfiles(string stage)
    {
        var data = ModPresetStore.Load();
        for (var i = 0; i < data.Profiles.Count; i++)
        {
            var p = data.Profiles[i];
            Log($"{stage}: profile[{i}] '{p.Name}' autoEnableNewMods={p.AutoEnableNewMods} " +
                $"known={p.KnownMods.Count} disabled={p.DisabledMods.Count}" +
                (i == data.CurrentProfileIndex ? "  <- current" : string.Empty));
        }
    }

    /// <summary>What settings.save actually holds for us and for the probe.</summary>
    private static void ReportSavedState()
    {
        try
        {
            var (entries, error) = ReadSavedModList();
            if (error != null)
            {
                Log($"saved: unable to read ({error})");
                return;
            }

            foreach (var item in entries.Where(e =>
                         e.Id == LoadOrderManagerMod.ModId ||
                         string.Equals(e.Id, _probeId, StringComparison.OrdinalIgnoreCase)))
            {
                Log($"saved: {item.Id} source={item.Source} is_enabled={item.IsEnabled}");
            }
        }
        catch (Exception ex)
        {
            Log($"saved: read failed: {ex.Message}");
        }
    }

    private static (List<SavedEntry> Entries, string? Error) ReadSavedModList()
    {
        var result = new List<SavedEntry>();

        var saveManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Saves.SaveManager");
        var saveManager = AccessTools.Property(saveManagerType, "Instance")?.GetValue(null);
        var settingsSave = GetMember(saveManager, "SettingsSave");
        var modSettings = GetMember(settingsSave, "ModSettings");
        var modList = GetMember(modSettings, "ModList");
        if (modList is not System.Collections.IEnumerable items) return (result, "ModList unavailable");

        foreach (var item in items)
        {
            var id = GetMember(item, "Id")?.ToString();
            if (string.IsNullOrWhiteSpace(id)) continue;
            result.Add(new SavedEntry(
                id!,
                GetMember(item, "Source")?.ToString() ?? "?",
                GetMember(item, "IsEnabled") is bool enabled && enabled));
        }

        return (result, null);
    }

    private sealed record SavedEntry(string Id, string Source, bool IsEnabled);

    // ── Helpers ──────────────────────────────────────────

    private static void EnsureAutoPreset()
    {
        var data = ModPresetStore.Load();
        if (data.Profiles.Any(p => string.Equals(p.Name, AutoPresetName, StringComparison.Ordinal)))
        {
            Log($"preset '{AutoPresetName}' already exists");
            return;
        }

        _panel!.DebugCreatePreset(AutoPresetName);
        Log($"created preset '{AutoPresetName}' (autoEnableNewMods=false expected)");
    }

    private static void SelectPresetByName(string name)
    {
        var data = ModPresetStore.Load();
        var index = data.Profiles.FindIndex(p => string.Equals(p.Name, name, StringComparison.Ordinal));
        if (index < 0)
        {
            Log($"preset '{name}' not found; skipping switch");
            return;
        }

        _panel!.DebugSelectPreset(index);
    }

    private static bool SaveManagerReady()
    {
        var saveManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Saves.SaveManager");
        var saveManager = AccessTools.Property(saveManagerType, "Instance")?.GetValue(null);
        if (saveManager == null) return false;

        var settingsSave = GetMember(saveManager, "SettingsSave");
        if (settingsSave == null) return false;

        return GetMember(settingsSave, "ModSettings") != null;
    }

    private static object? GetMember(object? obj, string name)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        var prop = AccessTools.Property(type, name);
        if (prop != null) return prop.GetValue(obj);
        return AccessTools.Field(type, name)?.GetValue(obj);
    }

    private static string? ReadEnv(string name)
    {
        try
        {
            return System.Environment.GetEnvironmentVariable(name);
        }
        catch
        {
            return null;
        }
    }

    private static void Log(string message) => DebugLog.Info($"[SelfTest] {message}");
}
