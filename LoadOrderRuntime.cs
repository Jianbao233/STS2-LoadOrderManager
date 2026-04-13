using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace LoadOrderManager;

internal sealed class LoadOrderEntry
{
    public LoadOrderEntry(string id, string name, int source, bool isEnabled)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? id : name;
        Source = source;
        IsEnabled = isEnabled;
    }

    public string Id { get; }
    public string Name { get; }
    public int Source { get; }
    public bool IsEnabled { get; set; }

    public string SourceText => Source switch
    {
        1 => I18n.T("source_mods"),
        2 => I18n.T("source_workshop"),
        _ => I18n.T("source_unknown")
    };

    public string Key => BuildKey(Id, Source);

    public static string BuildKey(string id, int source) => $"{id}::{source}";
}

internal static class LoadOrderRuntime
{
    private const string SettingsSaveFileName = "settings.save";

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly Regex UserIdPathRegex = new(
        "[\\/](?:steam|gog|epic|xbox|none)[\\/](?<id>\\d{1,20})(?:[\\/]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InvalidSnapshotKeyCharsRegex = new(
        "[^0-9A-Za-z_-]",
        RegexOptions.Compiled);

    public static void LogDiagnosticsOnStartup()
    {
        try
        {
            if (!TryResolveSaveContext(out var context, out var contextError))
            {
                DebugLog.Warn($"Startup diagnostics skipped: {contextError}");
                return;
            }

            ReadSettings(out var currentKeys, out var currentEnabledByKey);
            DebugLog.Info(
                $"Startup diagnostics context: runtimeUserId={FormatUserId(context.RuntimeUserId)}, pathUserId={FormatUserId(context.PathUserId)}, settingsPath={context.SettingsPath}, currentTop={FormatTop(currentKeys)}");

            if (!TryLoadApplySnapshot(context, out var snapshot, out var snapshotError))
            {
                DebugLog.Warn($"Startup diagnostics failed to load snapshot: {snapshotError}");
                return;
            }

            if (snapshot == null || snapshot.Entries.Count == 0)
            {
                DebugLog.Info("Startup diagnostics: no previous apply snapshot for current user.");
                return;
            }

            var snapshotKeys = snapshot.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => e.Key)
                .ToList();

            DebugLog.Info(
                $"Startup diagnostics snapshot: savedAtUtc={snapshot.SavedAtUtc}, entries={snapshot.Entries.Count}, top={FormatTop(snapshotKeys)}");

            if (IsSnapshotConsistent(snapshot, currentKeys, currentEnabledByKey, out var reason))
            {
                DebugLog.Info("Startup diagnostics: current settings match the last apply snapshot.");
            }
            else
            {
                DebugLog.Warn(
                    $"Startup diagnostics: detected drift from last apply snapshot. reason={reason}, snapshotTop={FormatTop(snapshotKeys)}, currentTop={FormatTop(currentKeys)}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Error("LogDiagnosticsOnStartup failed.", ex);
        }
    }

    public static bool TryBuildOverwriteWarning(out string warningText)
    {
        warningText = string.Empty;

        try
        {
            if (!TryResolveSaveContext(out var context, out var contextError))
            {
                DebugLog.Warn($"Overwrite check skipped: {contextError}");
                return false;
            }

            if (!TryLoadApplySnapshot(context, out var snapshot, out var snapshotError))
            {
                DebugLog.Warn($"Overwrite check skipped: failed to load snapshot. {snapshotError}");
                return false;
            }

            if (snapshot == null || snapshot.Entries.Count == 0)
            {
                return false;
            }

            ReadSettings(out var currentKeys, out var currentEnabledByKey);
            if (IsSnapshotConsistent(snapshot, currentKeys, currentEnabledByKey, out var reason))
            {
                return false;
            }

            var savedAt = FormatSnapshotTime(snapshot.SavedAtUtc);
            warningText = I18n.Tf("warning_overwritten", savedAt);

            var snapshotKeys = snapshot.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => e.Key)
                .ToList();

            DebugLog.Warn(
                $"Overwrite check detected drift. reason={reason}, snapshotTop={FormatTop(snapshotKeys)}, currentTop={FormatTop(currentKeys)}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Error("TryBuildOverwriteWarning failed.", ex);
            return false;
        }
    }

    public static bool TryGetOrderedEntries(out List<LoadOrderEntry> entries, out string error)
    {
        entries = new List<LoadOrderEntry>();
        error = string.Empty;

        try
        {
            var loadedModsByKey = ReadLoadedMods();
            if (loadedModsByKey.Count == 0)
            {
                error = "No mods found in ModManager.AllMods.";
                DebugLog.Warn(error);
                return false;
            }

            ReadSettings(out var settingsOrder, out var enabledByKey);

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in settingsOrder)
            {
                if (!loadedModsByKey.TryGetValue(key, out var mod)) continue;
                mod.IsEnabled = enabledByKey.GetValueOrDefault(key, true);
                entries.Add(mod);
                used.Add(key);
            }

            foreach (var kv in loadedModsByKey)
            {
                if (used.Contains(kv.Key)) continue;
                kv.Value.IsEnabled = enabledByKey.GetValueOrDefault(kv.Key, true);
                entries.Add(kv.Value);
            }

            DebugLog.Info($"Read order success. allMods={loadedModsByKey.Count}, settingsOrder={settingsOrder.Count}, merged={entries.Count}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            DebugLog.Error("TryGetOrderedEntries failed.", ex);
            return false;
        }
    }

    public static bool TrySaveOrderedEntries(IReadOnlyList<LoadOrderEntry> orderedEntries, out string error)
    {
        error = string.Empty;

        try
        {
            if (!TryResolveSaveContext(out var context, out var contextError))
            {
                error = contextError;
                DebugLog.Error(error);
                return false;
            }

            if (!ValidateCurrentUserScope(context, out var scopeError))
            {
                error = scopeError;
                DebugLog.Error(error);
                return false;
            }

            var saveManager = context.SaveManager;
            var settingsSave = GetMemberValue(saveManager, "SettingsSave");
            if (settingsSave == null)
            {
                error = "SaveManager.SettingsSave is null.";
                DebugLog.Error(error);
                return false;
            }

            var modSettingsType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModSettings");
            var settingsSaveModType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.SettingsSaveMod");
            var modSourceType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModSource");
            if (modSettingsType == null || settingsSaveModType == null || modSourceType == null)
            {
                error = "Mod settings runtime types not found.";
                DebugLog.Error(error);
                return false;
            }

            var modSettings = GetMemberValue(settingsSave, "ModSettings");
            if (modSettings == null)
            {
                modSettings = Activator.CreateInstance(modSettingsType);
                SetMemberValue(settingsSave, "ModSettings", modSettings);
            }

            if (modSettings == null)
            {
                error = "Failed to create ModSettings object.";
                DebugLog.Error(error);
                return false;
            }

            var listType = typeof(List<>).MakeGenericType(settingsSaveModType);
            var modList = (IList?)Activator.CreateInstance(listType);
            if (modList == null)
            {
                error = "Failed to create mod list.";
                DebugLog.Error(error);
                return false;
            }

            DebugLog.Info(
                $"Saving manual order with {orderedEntries.Count} entries. runtimeUserId={FormatUserId(context.RuntimeUserId)}, pathUserId={FormatUserId(context.PathUserId)}, settingsPath={context.SettingsPath}, top={FormatTop(orderedEntries)}");

            foreach (var entry in orderedEntries)
            {
                var item = Activator.CreateInstance(settingsSaveModType);
                if (item == null) continue;

                SetMemberValue(item, "Id", entry.Id);
                SetMemberValue(item, "Source", Enum.ToObject(modSourceType, entry.Source));
                SetMemberValue(item, "IsEnabled", entry.IsEnabled);
                modList.Add(item);
            }

            SetMemberValue(modSettings, "ModList", modList);

            var saveManagerType = ResolveCoreType("MegaCrit.Sts2.Core.Saves.SaveManager");
            var saveSettings = AccessTools.Method(saveManagerType, "SaveSettings", Type.EmptyTypes);
            if (saveSettings == null)
            {
                error = "SaveManager.SaveSettings() not found.";
                DebugLog.Error(error);
                return false;
            }

            saveSettings.Invoke(saveManager, null);

            var expectedKeys = orderedEntries.Select(e => e.Key).ToList();
            ReadSettings(out var persistedKeys, out _);
            if (!IsOrderMonotonic(expectedKeys, persistedKeys))
            {
                error = "Order verification failed after save. Check log for details.";
                DebugLog.Warn($"Order verification failed. expectedTop={FormatTop(expectedKeys)}, persistedTop={FormatTop(persistedKeys)}");
                return false;
            }

            if (!TryWriteApplySnapshot(context, orderedEntries, out var snapshotError))
            {
                DebugLog.Warn($"SaveSettings succeeded but snapshot write failed: {snapshotError}");
            }

            DebugLog.Info($"SaveSettings succeeded. Persisted top={FormatTop(persistedKeys)}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            DebugLog.Error("TrySaveOrderedEntries failed.", ex);
            return false;
        }
    }

    private static Dictionary<string, LoadOrderEntry> ReadLoadedMods()
    {
        var dict = new Dictionary<string, LoadOrderEntry>(StringComparer.Ordinal);
        var modManagerType = ResolveCoreType("MegaCrit.Sts2.Core.Modding.ModManager");
        if (modManagerType == null)
        {
            DebugLog.Warn("ReadLoadedMods: ModManager type not found.");
            return dict;
        }

        if (TryCollectModsFromMember(modManagerType, "AllMods", dict))
        {
            DebugLog.Info($"ReadLoadedMods: collected {dict.Count} entries from ModManager.AllMods.");
            return dict;
        }

        if (TryCollectModsFromMember(modManagerType, "_mods", dict))
        {
            DebugLog.Warn($"ReadLoadedMods: AllMods empty, fallback to ModManager._mods ({dict.Count}).");
            return dict;
        }

        if (TryCollectModsFromMember(modManagerType, "LoadedMods", dict))
        {
            DebugLog.Warn($"ReadLoadedMods: AllMods/_mods empty, fallback to ModManager.LoadedMods ({dict.Count}).");
            return dict;
        }

        if (TryCollectModsFromSettings(dict))
        {
            DebugLog.Warn($"ReadLoadedMods: ModManager lists empty, fallback to settings.mod_list ({dict.Count}).");
        }

        return dict;
    }

    private static void ReadSettings(out List<string> orderedKeys, out Dictionary<string, bool> enabledByKey)
    {
        orderedKeys = new List<string>();
        enabledByKey = new Dictionary<string, bool>(StringComparer.Ordinal);

        var saveManagerType = ResolveCoreType("MegaCrit.Sts2.Core.Saves.SaveManager");
        if (saveManagerType == null) return;
        var saveManager = AccessTools.Property(saveManagerType, "Instance")?.GetValue(null);
        var settingsSave = GetMemberValue(saveManager, "SettingsSave");
        var modSettings = GetMemberValue(settingsSave, "ModSettings");
        var modListObj = GetMemberValue(modSettings, "ModList");
        if (modListObj is not IEnumerable modList) return;

        foreach (var item in modList)
        {
            var id = AsString(GetMemberValue(item, "Id"));
            if (string.IsNullOrWhiteSpace(id)) continue;

            var source = ToInt(GetMemberValue(item, "Source"));
            var enabled = ToBool(GetMemberValue(item, "IsEnabled"), true);
            var key = LoadOrderEntry.BuildKey(id, source);

            orderedKeys.Add(key);
            enabledByKey[key] = enabled;
        }
    }

    private static bool TryResolveSaveContext(out SaveContext context, out string error)
    {
        context = null!;
        error = string.Empty;

        var saveManagerType = ResolveCoreType("MegaCrit.Sts2.Core.Saves.SaveManager");
        if (saveManagerType == null)
        {
            error = "SaveManager type not found.";
            return false;
        }

        var saveManager = AccessTools.Property(saveManagerType, "Instance")?.GetValue(null);
        if (saveManager == null)
        {
            error = "SaveManager.Instance is null.";
            return false;
        }

        var saveStore = GetMemberValue(saveManager, "_saveStore");
        if (saveStore == null)
        {
            error = "SaveManager._saveStore is null.";
            return false;
        }

        var getFullPath = AccessTools.Method(saveStore.GetType(), "GetFullPath", new[] { typeof(string) });
        if (getFullPath == null)
        {
            error = $"GetFullPath(string) not found on save store {saveStore.GetType().FullName}.";
            return false;
        }

        var settingsPath = AsString(getFullPath.Invoke(saveStore, new object[] { SettingsSaveFileName }));
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            error = "Could not resolve full settings.save path from save store.";
            return false;
        }

        var runtimeUserId = TryResolveRuntimeUserId();
        var pathUserId = TryExtractPathUserId(settingsPath);

        context = new SaveContext(saveManager, settingsPath, runtimeUserId, pathUserId);
        return true;
    }

    private static bool ValidateCurrentUserScope(SaveContext context, out string error)
    {
        error = string.Empty;

        if (context.RuntimeUserId == null)
        {
            error = $"Current runtime user id is unavailable. Refusing save to avoid cross-account overwrite. settingsPath={context.SettingsPath}";
            return false;
        }

        if (context.PathUserId == null)
        {
            if (ContainsUserIdSegment(context.SettingsPath, context.RuntimeUserId.Value))
            {
                return true;
            }

            error =
                $"Could not verify settings path belongs to runtime user {context.RuntimeUserId}. path={context.SettingsPath}";
            return false;
        }

        if (context.RuntimeUserId.Value != context.PathUserId.Value)
        {
            error =
                $"Refusing save: runtime user id {context.RuntimeUserId} does not match settings path user id {context.PathUserId}. path={context.SettingsPath}";
            return false;
        }

        return true;
    }

    private static ulong? TryResolveRuntimeUserId()
    {
        try
        {
            var platformUtilType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Platform.PlatformUtil");
            if (platformUtilType == null)
            {
                return null;
            }

            var primaryPlatform = AccessTools.Property(platformUtilType, "PrimaryPlatform")?.GetValue(null);
            if (primaryPlatform == null)
            {
                return null;
            }

            var getLocalPlayerId = AccessTools.Method(platformUtilType, "GetLocalPlayerId");
            if (getLocalPlayerId == null)
            {
                return null;
            }

            var idObj = getLocalPlayerId.Invoke(null, new[] { primaryPlatform });
            return ToUlong(idObj);
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolveCoreType(string fullTypeName)
    {
        var coreAssembly = ResolveCoreAssembly();
        var coreType = coreAssembly?.GetType(fullTypeName, false, false);
        if (coreType != null)
        {
            return coreType;
        }

        var fallback = AccessTools.TypeByName(fullTypeName);
        if (fallback != null)
        {
            DebugLog.Warn($"ResolveCoreType fallback used for {fullTypeName}: {fallback.Assembly.FullName}");
        }

        return fallback;
    }

    private static Assembly? ResolveCoreAssembly()
    {
        var saveManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Saves.SaveManager");
        return saveManagerType?.Assembly;
    }

    private static bool TryCollectModsFromMember(Type modManagerType, string memberName, Dictionary<string, LoadOrderEntry> dict)
    {
        object? value = null;
        var prop = AccessTools.Property(modManagerType, memberName);
        if (prop != null)
        {
            value = prop.GetValue(null);
        }
        else
        {
            var field = AccessTools.Field(modManagerType, memberName);
            if (field != null)
            {
                value = field.GetValue(null);
            }
        }

        if (value is not IEnumerable items)
        {
            return false;
        }

        return TryCollectMods(items, dict);
    }

    private static bool TryCollectMods(IEnumerable modList, Dictionary<string, LoadOrderEntry> dict)
    {
        var added = 0;

        foreach (var modObj in modList)
        {
            var manifest = GetMemberValue(modObj, "manifest");
            var id = AsString(GetMemberValue(manifest, "id"));
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var name = AsString(GetMemberValue(manifest, "name"));
            var source = ToInt(GetMemberValue(modObj, "modSource"));
            var key = LoadOrderEntry.BuildKey(id, source);
            var wasNew = !dict.ContainsKey(key);

            dict[key] = new LoadOrderEntry(id, name ?? id, source, true);
            if (wasNew)
            {
                added++;
            }
        }

        return added > 0;
    }

    private static bool TryCollectModsFromSettings(Dictionary<string, LoadOrderEntry> dict)
    {
        ReadSettings(out var orderedKeys, out var enabledByKey);
        if (orderedKeys.Count == 0)
        {
            return false;
        }

        var added = 0;
        foreach (var key in orderedKeys)
        {
            if (!TryParseEntryKey(key, out var id, out var source))
            {
                continue;
            }

            if (dict.ContainsKey(key))
            {
                continue;
            }

            dict[key] = new LoadOrderEntry(id, id, source, enabledByKey.GetValueOrDefault(key, true));
            added++;
        }

        return added > 0;
    }

    private static bool TryParseEntryKey(string key, out string id, out int source)
    {
        id = string.Empty;
        source = 0;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var pos = key.LastIndexOf("::", StringComparison.Ordinal);
        if (pos <= 0 || pos >= key.Length - 2)
        {
            return false;
        }

        var idPart = key[..pos];
        var sourcePart = key[(pos + 2)..];
        if (string.IsNullOrWhiteSpace(idPart))
        {
            return false;
        }

        if (!int.TryParse(sourcePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSource))
        {
            return false;
        }

        id = idPart;
        source = parsedSource;
        return true;
    }

    private static ulong? TryExtractPathUserId(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var match = UserIdPathRegex.Match(path);
        if (!match.Success)
        {
            return null;
        }

        return ulong.TryParse(match.Groups["id"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static bool ContainsUserIdSegment(string path, ulong userId)
    {
        var marker1 = $"/{userId}/";
        var marker2 = $"\\{userId}\\";
        return path.Contains(marker1, StringComparison.OrdinalIgnoreCase) ||
               path.Contains(marker2, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSnapshotPath(SaveContext context)
    {
        var logDir = Path.GetDirectoryName(DebugLog.LogPath);
        var stateDir = Path.Combine(string.IsNullOrWhiteSpace(logDir) ? "." : logDir, "state");

        var rawKey = context.RuntimeUserId?.ToString(CultureInfo.InvariantCulture) ??
                     context.PathUserId?.ToString(CultureInfo.InvariantCulture) ??
                     "unknown";

        var safeKey = InvalidSnapshotKeyCharsRegex.Replace(rawKey, "_");
        return Path.Combine(stateDir, $"last_apply_{safeKey}.json");
    }

    private static bool TryWriteApplySnapshot(SaveContext context, IReadOnlyList<LoadOrderEntry> orderedEntries, out string error)
    {
        error = string.Empty;

        try
        {
            var snapshot = new ApplySnapshot
            {
                SavedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                RuntimeUserId = FormatUserId(context.RuntimeUserId),
                PathUserId = FormatUserId(context.PathUserId),
                SettingsPath = context.SettingsPath,
                Entries = orderedEntries
                    .Select(e => new SnapshotEntry { Key = e.Key, IsEnabled = e.IsEnabled })
                    .ToList()
            };

            var snapshotPath = BuildSnapshotPath(context);
            var dir = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
            File.WriteAllText(snapshotPath, json);

            var keys = snapshot.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => e.Key)
                .ToList();

            DebugLog.Info(
                $"Apply snapshot saved. path={snapshotPath}, entries={snapshot.Entries.Count}, top={FormatTop(keys)}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            DebugLog.Error("TryWriteApplySnapshot failed.", ex);
            return false;
        }
    }

    private static bool TryLoadApplySnapshot(SaveContext context, out ApplySnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = string.Empty;

        try
        {
            var snapshotPath = BuildSnapshotPath(context);
            if (!File.Exists(snapshotPath))
            {
                return true;
            }

            var json = File.ReadAllText(snapshotPath);
            var parsed = JsonSerializer.Deserialize<ApplySnapshot>(json);
            if (parsed == null)
            {
                error = $"Snapshot json parse returned null. path={snapshotPath}";
                return false;
            }

            parsed.Entries ??= new List<SnapshotEntry>();
            snapshot = parsed;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            DebugLog.Error("TryLoadApplySnapshot failed.", ex);
            return false;
        }
    }

    private static bool IsSnapshotConsistent(
        ApplySnapshot snapshot,
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, bool> currentEnabledByKey,
        out string reason)
    {
        var snapshotEntries = snapshot.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .ToList();

        var snapshotOrder = snapshotEntries
            .Select(e => e.Key)
            .ToList();

        if (snapshotOrder.Count == 0)
        {
            reason = "snapshot_empty";
            return true;
        }

        if (!IsOrderMonotonic(snapshotOrder, currentOrder))
        {
            reason = "order_changed";
            return false;
        }

        foreach (var entry in snapshotEntries)
        {
            if (!currentEnabledByKey.TryGetValue(entry.Key, out var enabledNow))
            {
                reason = $"missing:{entry.Key}";
                return false;
            }

            if (enabledNow != entry.IsEnabled)
            {
                reason = $"enabled_changed:{entry.Key}";
                return false;
            }
        }

        reason = "matched";
        return true;
    }

    private static string FormatSnapshotTime(string savedAtUtc)
    {
        if (DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return savedAtUtc;
    }

    private static string FormatUserId(ulong? userId)
    {
        return userId?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static object? GetMemberValue(object? obj, string name)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        var prop = AccessTools.Property(type, name);
        if (prop != null) return prop.GetValue(obj);
        var field = AccessTools.Field(type, name);
        return field?.GetValue(obj);
    }

    private static void SetMemberValue(object? obj, string name, object? value)
    {
        if (obj == null) return;
        var type = obj.GetType();
        var prop = AccessTools.Property(type, name);
        if (prop != null)
        {
            prop.SetValue(obj, value);
            return;
        }

        var field = AccessTools.Field(type, name);
        field?.SetValue(obj, value);
    }

    private static string? AsString(object? obj)
    {
        return obj switch
        {
            null => null,
            string s => s,
            _ => obj.ToString()
        };
    }

    private static ulong? ToUlong(object? obj)
    {
        return obj switch
        {
            null => null,
            ulong u => u,
            long l when l >= 0 => (ulong)l,
            int i when i >= 0 => (ulong)i,
            Enum e => (ulong)Convert.ToInt64(e, CultureInfo.InvariantCulture),
            _ when ulong.TryParse(obj.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static int ToInt(object? obj)
    {
        return obj switch
        {
            null => 0,
            int i => i,
            long l => (int)l,
            Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture),
            _ when int.TryParse(obj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static bool ToBool(object? obj, bool fallback)
    {
        return obj switch
        {
            null => fallback,
            bool b => b,
            _ when bool.TryParse(obj.ToString(), out var parsed) => parsed,
            _ => fallback
        };
    }

    private static bool IsOrderMonotonic(IReadOnlyList<string> expectedOrder, IReadOnlyList<string> persistedOrder)
    {
        var indexMap = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < persistedOrder.Count; i++)
        {
            indexMap[persistedOrder[i]] = i;
        }

        var prev = -1;
        foreach (var key in expectedOrder)
        {
            if (!indexMap.TryGetValue(key, out var idx))
            {
                continue;
            }

            if (idx < prev)
            {
                return false;
            }

            prev = idx;
        }

        return true;
    }

    private static string FormatTop(IReadOnlyList<LoadOrderEntry> entries)
    {
        var take = Math.Min(entries.Count, 8);
        if (take == 0) return "<empty>";
        var ids = entries.Take(take).Select(e => $"{e.Id}({e.Source})");
        return string.Join(" > ", ids);
    }

    private static string FormatTop(IReadOnlyList<string> keys)
    {
        var take = Math.Min(keys.Count, 8);
        if (take == 0) return "<empty>";
        return string.Join(" > ", keys.Take(take));
    }

    private sealed class SaveContext
    {
        public SaveContext(object saveManager, string settingsPath, ulong? runtimeUserId, ulong? pathUserId)
        {
            SaveManager = saveManager;
            SettingsPath = settingsPath;
            RuntimeUserId = runtimeUserId;
            PathUserId = pathUserId;
        }

        public object SaveManager { get; }
        public string SettingsPath { get; }
        public ulong? RuntimeUserId { get; }
        public ulong? PathUserId { get; }
    }

    private sealed class ApplySnapshot
    {
        public string SavedAtUtc { get; set; } = string.Empty;
        public string RuntimeUserId { get; set; } = string.Empty;
        public string PathUserId { get; set; } = string.Empty;
        public string SettingsPath { get; set; } = string.Empty;
        public List<SnapshotEntry> Entries { get; set; } = new();
    }

    private sealed class SnapshotEntry
    {
        public string Key { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }
}
