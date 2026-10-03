using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace LoadOrderManager;

/// <summary>
/// A named preset.
///
/// <see cref="DisabledMods"/> + <see cref="KnownMods"/> together describe the preset:
/// a mod the preset has seen (<see cref="KnownMods"/>) is enabled unless it is listed
/// in <see cref="DisabledMods"/>; a mod the preset has never seen (subscribed after the
/// preset was created/last committed) follows <see cref="AutoEnableNewMods"/>.
/// </summary>
internal sealed class ModPreset
{
    public string Name { get; set; } = "Default";
    public HashSet<string> DisabledMods { get; set; } = new();
    public HashSet<string> KnownMods { get; set; } = new();
    public bool AutoEnableNewMods { get; set; } = true;
}

/// <summary>
/// Persisted preset state, stored in a standalone JSON file
/// (not inside settings.save).
/// </summary>
internal sealed class PresetSaveData
{
    public int SchemaVersion { get; set; } = ModPresetStore.CurrentSchemaVersion;
    public List<ModPreset> Profiles { get; set; } = new();
    public int CurrentProfileIndex { get; set; } = 0;
}

/// <summary>
/// Manages preset CRUD and persistence to
/// %APPDATA%/SlayTheSpire2/LoadOrderManager/presets.json
/// </summary>
internal static class ModPresetStore
{
    /// <summary>
    /// 1 = only DisabledMods (v0.3.0 and older).
    /// 2 = adds KnownMods + AutoEnableNewMods (newly subscribed mods only auto-enable
    ///     in the preset that opts in, "Default" by default).
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static PresetSaveData? _cache;

    /// <summary>
    /// True while the store has never been written to disk (first run, or the file was
    /// unreadable). Treated like a legacy file: everything installed right now counts as
    /// already seen, so a fresh install does not label every mod as NEW.
    /// </summary>
    private static bool _freshStore;

    private static string StorePath
    {
        get
        {
            var userData = OS.GetUserDataDir();
            var dir = Path.Combine(userData, "LoadOrderManager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "presets.json");
        }
    }

    // ── Load / Save ──────────────────────────────────────

    public static PresetSaveData Load()
    {
        if (_cache != null) return _cache;

        try
        {
            var path = StorePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _cache = JsonSerializer.Deserialize<PresetSaveData>(json) ?? CreateDefault();
            }
            else
            {
                _cache = CreateDefault();
                _freshStore = true;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Error("ModPresetStore.Load failed.", ex);
            _cache = CreateDefault();
            _freshStore = true;
        }

        NormalizeIndex(_cache);
        return _cache;
    }

    public static bool Save()
    {
        if (_cache == null) return false;

        try
        {
            var path = StorePath;
            var json = JsonSerializer.Serialize(_cache, JsonOpts);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Error("ModPresetStore.Save failed.", ex);
            return false;
        }
    }

    // ── Profile accessors ─────────────────────────────────

    public static ModPreset CurrentProfile
    {
        get
        {
            var data = Load();
            NormalizeIndex(data);
            return data.Profiles[data.CurrentProfileIndex];
        }
    }

    // ── Migration ─────────────────────────────────────────

    /// <summary>
    /// Upgrades schema 1 (DisabledMods only) to schema 2.
    ///
    /// Every currently installed mod is recorded as "already seen", so nothing the user
    /// has today changes state; only mods subscribed *after* this migration count as new.
    /// Only the first preset auto-enables new mods, matching the "Default group only"
    /// behaviour requested by users.
    /// </summary>
    public static void MigrateIfNeeded(IReadOnlyList<LoadOrderEntry> entries)
    {
        var data = Load();
        var isLegacy = data.SchemaVersion < CurrentSchemaVersion;
        if (!isLegacy && !_freshStore) return;

        var installed = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .Select(e => e.Id)
            .ToList();

        for (var i = 0; i < data.Profiles.Count; i++)
        {
            var profile = data.Profiles[i];
            foreach (var id in installed)
            {
                profile.KnownMods.Add(id);
            }

            // Legacy files only recorded disabled mods; give the first preset (Default)
            // the "auto-enable new mods" behaviour users asked for and leave the rest manual.
            if (isLegacy)
            {
                profile.AutoEnableNewMods = i == 0;
            }
        }

        data.SchemaVersion = CurrentSchemaVersion;
        _freshStore = false;
        Save();
        DebugLog.Info(
            $"Presets initialised (legacy={isLegacy}, schema={CurrentSchemaVersion}). profiles={data.Profiles.Count}, installed={installed.Count}");
    }

    // ── Snapshot & Apply ──────────────────────────────────

    /// <summary>
    /// Snapshot the current state into the current profile: every entry becomes "known"
    /// and the disabled ones are recorded explicitly.
    /// </summary>
    public static void CommitCurrentProfile(IReadOnlyList<LoadOrderEntry> entries)
    {
        var profile = CurrentProfile;
        profile.DisabledMods.Clear();
        profile.KnownMods.Clear();

        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Id)) continue;

            profile.KnownMods.Add(e.Id);
            if (!e.IsEnabled)
            {
                profile.DisabledMods.Add(e.Id);
            }
        }

        Save();
    }

    /// <summary>
    /// Applies the preset to the entries in-place, including the new-mod policy.
    /// Returns how many entries were governed by the new-mod policy (i.e. unknown to
    /// this preset).
    /// </summary>
    public static int ApplyPreset(ModPreset preset, List<LoadOrderEntry> entries)
    {
        var newMods = 0;

        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || !preset.KnownMods.Contains(e.Id))
            {
                e.IsEnabled = preset.AutoEnableNewMods;
                e.IsNew = true;
                newMods++;
                continue;
            }

            e.IsEnabled = !preset.DisabledMods.Contains(e.Id);
            e.IsNew = false;
        }

        return newMods;
    }

    /// <summary>
    /// Applies the new-mod policy only, leaving mods this preset already knows untouched
    /// (so manual toggles made in the official mod screen stay visible). Used when the
    /// panel is opened or the policy checkbox is flipped, and never writes to disk.
    /// </summary>
    public static int ApplyNewModPolicy(ModPreset preset, List<LoadOrderEntry> entries)
    {
        var newMods = 0;

        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || preset.KnownMods.Contains(e.Id))
            {
                e.IsNew = false;
                continue;
            }

            e.IsEnabled = preset.AutoEnableNewMods;
            e.IsNew = true;
            newMods++;
        }

        return newMods;
    }

    /// <summary>Pure decision helper (kept free of Godot types so it stays testable).</summary>
    public static bool DecideEnabled(bool isKnown, bool isDisabled, bool autoEnableNewMods)
    {
        return isKnown ? !isDisabled : autoEnableNewMods;
    }

    // ── CRUD ─────────────────────────────────────────────

    public static ModPreset CreatePreset(string name, IReadOnlyList<LoadOrderEntry> entries)
    {
        var data = Load();
        var preset = new ModPreset
        {
            Name = name,
            KnownMods = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Id))
                .Select(e => e.Id)
                .ToHashSet(),
            DisabledMods = entries
                .Where(e => !e.IsEnabled && !string.IsNullOrWhiteSpace(e.Id))
                .Select(e => e.Id)
                .ToHashSet(),
            // A preset created now knows every installed mod, so the policy only applies
            // to mods subscribed later. Default to "manual", which is what users asked for.
            AutoEnableNewMods = false
        };
        data.Profiles.Add(preset);
        data.CurrentProfileIndex = data.Profiles.Count - 1;
        Save();
        return preset;
    }

    public static bool SetAutoEnableNewMods(bool value)
    {
        var data = Load();
        data.Profiles[data.CurrentProfileIndex].AutoEnableNewMods = value;
        return Save();
    }

    public static bool DeleteCurrentPreset()
    {
        var data = Load();
        if (data.Profiles.Count <= 1) return false;

        data.Profiles.RemoveAt(data.CurrentProfileIndex);
        if (data.CurrentProfileIndex >= data.Profiles.Count)
        {
            data.CurrentProfileIndex = data.Profiles.Count - 1;
        }
        Save();
        return true;
    }

    public static bool RenameCurrentPreset(string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        var data = Load();
        data.Profiles[data.CurrentProfileIndex].Name = trimmed;
        return Save();
    }

    /// <summary>
    /// Snapshot current state, switch to profile[index], apply it.
    /// Caller is responsible for calling ApplyPreset afterwards.
    /// </summary>
    public static void SelectProfile(int index, IReadOnlyList<LoadOrderEntry> entries)
    {
        var data = Load();
        CommitCurrentProfile(entries);
        data.CurrentProfileIndex = index;
        NormalizeIndex(data);
        Save();
    }

    // ── Internal helpers ─────────────────────────────────

    private static PresetSaveData CreateDefault()
    {
        return new PresetSaveData
        {
            SchemaVersion = CurrentSchemaVersion,
            Profiles = new List<ModPreset> { new() { Name = "Default", AutoEnableNewMods = true } },
            CurrentProfileIndex = 0
        };
    }

    private static void NormalizeIndex(PresetSaveData? data)
    {
        if (data == null) return;
        if (data.Profiles.Count == 0)
        {
            data.Profiles.Add(new ModPreset { Name = "Default", AutoEnableNewMods = true });
        }
        if (data.CurrentProfileIndex < 0) data.CurrentProfileIndex = 0;
        if (data.CurrentProfileIndex >= data.Profiles.Count)
        {
            data.CurrentProfileIndex = data.Profiles.Count - 1;
        }
    }
}
