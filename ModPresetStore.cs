using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace LoadOrderManager;

/// <summary>
/// A named preset that stores which mods are disabled.
/// </summary>
internal sealed class ModPreset
{
    public string Name { get; set; } = "Default";
    public HashSet<string> DisabledMods { get; set; } = new();
}

/// <summary>
/// Persisted preset state, stored in a standalone JSON file
/// (not inside settings.save).
/// </summary>
internal sealed class PresetSaveData
{
    public List<ModPreset> Profiles { get; set; } = new();
    public int CurrentProfileIndex { get; set; } = 0;
}

/// <summary>
/// Manages preset CRUD and persistence to
/// %APPDATA%/SlayTheSpire2/LoadOrderManager/presets.json
/// </summary>
internal static class ModPresetStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static PresetSaveData? _cache;

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
            }
        }
        catch (Exception ex)
        {
            DebugLog.Error("ModPresetStore.Load failed.", ex);
            _cache = CreateDefault();
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

    // ── Snapshot & Apply ──────────────────────────────────

    /// <summary>
    /// Snapshot current is_enabled states into the current profile.
    /// Disabled mods = those with IsEnabled == false (keyed by mod Id).
    /// </summary>
    public static void SnapshotCurrent(IReadOnlyList<LoadOrderEntry> entries)
    {
        var profile = CurrentProfile;
        profile.DisabledMods.Clear();
        foreach (var e in entries)
        {
            if (!e.IsEnabled && !string.IsNullOrWhiteSpace(e.Id))
            {
                profile.DisabledMods.Add(e.Id);
            }
        }
        Save();
    }

    /// <summary>
    /// Apply a preset's disabled set to the entries in-place.
    /// </summary>
    public static void ApplyPreset(ModPreset preset, List<LoadOrderEntry> entries)
    {
        foreach (var e in entries)
        {
            e.IsEnabled = !preset.DisabledMods.Contains(e.Id);
        }
    }

    // ── CRUD ─────────────────────────────────────────────

    public static ModPreset CreatePreset(string name, IReadOnlyList<LoadOrderEntry> entries)
    {
        var data = Load();
        var preset = new ModPreset
        {
            Name = name,
            DisabledMods = entries
                .Where(e => !e.IsEnabled && !string.IsNullOrWhiteSpace(e.Id))
                .Select(e => e.Id)
                .ToHashSet()
        };
        data.Profiles.Add(preset);
        data.CurrentProfileIndex = data.Profiles.Count - 1;
        Save();
        return preset;
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
        SnapshotCurrent(entries);
        data.CurrentProfileIndex = index;
        NormalizeIndex(data);
        Save();
    }

    // ── Internal helpers ─────────────────────────────────

    private static PresetSaveData CreateDefault()
    {
        return new PresetSaveData
        {
            Profiles = new List<ModPreset> { new() { Name = "Default" } },
            CurrentProfileIndex = 0
        };
    }

    private static void NormalizeIndex(PresetSaveData? data)
    {
        if (data == null) return;
        if (data.Profiles.Count == 0)
        {
            data.Profiles.Add(new ModPreset { Name = "Default" });
        }
        if (data.CurrentProfileIndex < 0) data.CurrentProfileIndex = 0;
        if (data.CurrentProfileIndex >= data.Profiles.Count)
        {
            data.CurrentProfileIndex = data.Profiles.Count - 1;
        }
    }
}