using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Godot;
using HarmonyLib;

namespace LoadOrderManager;

internal static class I18n
{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.Ordinal);
    private static readonly object LockObj = new();
    private static bool _loaded;

    public static string T(string key)
    {
        EnsureLoaded();

        var language = ResolveLanguageTag(GetClientLanguageCode() ?? "en");
        if (Tables.TryGetValue(language, out var table) && table.TryGetValue(key, out var localized))
        {
            return localized;
        }

        if (Tables.TryGetValue("en", out var fallbackEn) && fallbackEn.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public static string Tf(string key, params object[] args)
    {
        return string.Format(CultureInfo.InvariantCulture, T(key), args);
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;

        lock (LockObj)
        {
            if (_loaded) return;

            var i18nDir = GetI18nDirectory();
            if (!Directory.Exists(i18nDir))
            {
                DebugLog.Error($"i18n directory not found: {i18nDir}");
                _loaded = true;
                return;
            }

            foreach (var file in Directory.GetFiles(i18nDir, "*.lang", SearchOption.TopDirectoryOnly))
            {
                var langTag = NormalizeTag(Path.GetFileNameWithoutExtension(file));
                if (string.IsNullOrWhiteSpace(langTag)) continue;

                var table = ParseLangFile(file);
                if (table.Count == 0) continue;
                Tables[langTag] = table;
            }

            if (!Tables.ContainsKey("en"))
            {
                DebugLog.Warn("Missing en.lang; fallback texts may be unavailable.");
            }

            DebugLog.Info($"i18n loaded from: {i18nDir}, languages: {string.Join(", ", Tables.Keys)}");
            _loaded = true;
        }
    }

    private static string GetI18nDirectory()
    {
        var candidates = new List<string>();

        try
        {
            var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(asmDir))
            {
                candidates.Add(Path.Combine(asmDir, "i18n"));
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            var exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                candidates.Add(Path.Combine(exeDir, "mods", "LoadOrderManager", "i18n"));
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            var userData = OS.GetUserDataDir();
            if (!string.IsNullOrWhiteSpace(userData))
            {
                candidates.Add(Path.Combine(userData, "mods", "LoadOrderManager", "i18n"));
            }
        }
        catch
        {
            // ignored
        }

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates.Count > 0 ? candidates[0] : "i18n";
    }

    private static Dictionary<string, string> ParseLangFile(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith(";", StringComparison.Ordinal)) continue;

            var idx = line.IndexOf('=');
            if (idx <= 0) continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Length == 0) continue;

            dict[key] = value.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        return dict;
    }

    private static string ResolveLanguageTag(string code)
    {
        var normalized = NormalizeTag(code);

        if (normalized.StartsWith("zhs", StringComparison.Ordinal) ||
            normalized.Contains("hans", StringComparison.Ordinal) ||
            normalized == "zh-cn" ||
            normalized == "zh-sg" ||
            normalized == "zh")
        {
            return "zhs";
        }

        if (normalized.StartsWith("zht", StringComparison.Ordinal) ||
            normalized.Contains("hant", StringComparison.Ordinal) ||
            normalized == "zh-tw" ||
            normalized == "zh-hk" ||
            normalized == "zh-mo")
        {
            return "zht";
        }

        if (normalized.StartsWith("en", StringComparison.Ordinal)) return "en";
        if (normalized.StartsWith("ko", StringComparison.Ordinal)) return "ko";
        if (normalized.StartsWith("de", StringComparison.Ordinal)) return "de";
        if (normalized.StartsWith("ja", StringComparison.Ordinal)) return "ja";
        if (normalized.StartsWith("fr", StringComparison.Ordinal)) return "fr";
        if (normalized.StartsWith("ru", StringComparison.Ordinal)) return "ru";
        if (normalized.StartsWith("pl", StringComparison.Ordinal)) return "pl";
        if (normalized.StartsWith("tr", StringComparison.Ordinal)) return "tr";
        if (normalized.StartsWith("it", StringComparison.Ordinal)) return "it";

        if (normalized.StartsWith("pt", StringComparison.Ordinal)) return "pt-br";

        if (normalized == "es-419" || normalized.Contains("latam", StringComparison.Ordinal))
        {
            return "es-419";
        }

        if (normalized.StartsWith("es-", StringComparison.Ordinal))
        {
            var region = normalized[3..];
            if (IsLatamRegion(region))
            {
                return "es-419";
            }

            return "es-es";
        }

        if (normalized == "es")
        {
            return "es-es";
        }

        return "en";
    }

    private static bool IsLatamRegion(string region)
    {
        return region switch
        {
            "ar" or "bo" or "cl" or "co" or "cr" or "cu" or "do" or "ec" or "sv" or "gt" or "hn" or
            "mx" or "ni" or "pa" or "py" or "pe" or "pr" or "uy" or "ve" => true,
            _ => false
        };
    }

    private static string NormalizeTag(string tag)
    {
        return tag.Trim().Replace('_', '-').ToLowerInvariant();
    }

    private static string? GetClientLanguageCode()
    {
        try
        {
            var saveManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Saves.SaveManager");
            var saveManager = AccessTools.Property(saveManagerType, "Instance")?.GetValue(null);
            var settingsSave = GetMemberValue(saveManager, "SettingsSave");
            var langObj = GetMemberValue(settingsSave, "Language");
            if (langObj is string lang && !string.IsNullOrWhiteSpace(lang))
            {
                return lang;
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            return TranslationServer.GetLocale();
        }
        catch
        {
            return null;
        }
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
}
