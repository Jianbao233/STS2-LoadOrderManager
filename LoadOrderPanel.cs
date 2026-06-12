using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace LoadOrderManager;

public partial class LoadOrderPanel : Control
{
    private const string ClipboardFormatV1 = "load_order_manager_v1";
    private static readonly Regex TokenSplitRegex = new(
        @"[\r\n,;|\t]+",
        RegexOptions.Compiled);

    private sealed class ClipboardPayload
    {
        public string format { get; set; } = ClipboardFormatV1;
        public string exported_at_utc { get; set; } = string.Empty;
        public List<ClipboardEntry> entries { get; set; } = new();
    }

    private sealed class ClipboardEntry
    {
        public string key { get; set; } = string.Empty;
        public string id { get; set; } = string.Empty;
        public int? source { get; set; }
        public bool? is_enabled { get; set; }
        public bool? enabled { get; set; }

        public bool? ResolveEnabled()
        {
            if (is_enabled.HasValue) return is_enabled.Value;
            if (enabled.HasValue) return enabled.Value;
            return null;
        }
    }

    private readonly List<LoadOrderEntry> _entries = new();
    private bool _uiBuilt;

    private ItemList _list = null!;
    private Label _statusLabel = null!;
    private Label _warningLabel = null!;

    public override void _Ready()
    {
        BuildUiIfNeeded();
        Visible = false;
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is not InputEventKey keyEvent) return;
        if (!keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;

        Visible = false;
        GetViewport().SetInputAsHandled();
    }

    public void OpenPanel()
    {
        BuildUiIfNeeded();
        Visible = true;
        DebugLog.Info("OpenPanel called.");
        RefreshFromRuntime();
    }

    private void BuildUiIfNeeded()
    {
        if (_uiBuilt) return;
        _uiBuilt = true;

        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetLeft = 0f;
        OffsetTop = 0f;
        OffsetRight = 0f;
        OffsetBottom = 0f;
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 100;

        var backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.62f),
            MouseFilter = MouseFilterEnum.Stop
        };
        backdrop.AnchorLeft = 0f;
        backdrop.AnchorTop = 0f;
        backdrop.AnchorRight = 1f;
        backdrop.AnchorBottom = 1f;
        AddChild(backdrop);

        var dialog = new PanelContainer();
        dialog.AnchorLeft = 0.5f;
        dialog.AnchorTop = 0.5f;
        dialog.AnchorRight = 0.5f;
        dialog.AnchorBottom = 0.5f;
        dialog.OffsetLeft = -470f;
        dialog.OffsetTop = -300f;
        dialog.OffsetRight = 470f;
        dialog.OffsetBottom = 300f;
        dialog.MouseFilter = MouseFilterEnum.Stop;
        backdrop.AddChild(dialog);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        dialog.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);

        var title = new Label
        {
            Text = I18n.T("title"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        root.AddChild(title);

        var subtitle = new Label
        {
            Text = I18n.T("subtitle"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        root.AddChild(subtitle);

        _warningLabel = new Label
        {
            Visible = false,
            Text = string.Empty,
            Modulate = new Color(1f, 0.77f, 0.3f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        root.AddChild(_warningLabel);

        var body = new HBoxContainer();
        body.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddThemeConstantOverride("separation", 10);
        root.AddChild(body);

        _list = new ItemList
        {
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true
        };
        _list.CustomMinimumSize = new Vector2(720, 410);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _list.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddChild(_list);

        var side = new VBoxContainer();
        side.CustomMinimumSize = new Vector2(180, 300);
        side.AddThemeConstantOverride("separation", 6);
        body.AddChild(side);

        side.AddChild(MakeButton(I18n.T("btn_move_up"), () => MoveSelected(-1)));
        side.AddChild(MakeButton(I18n.T("btn_move_down"), () => MoveSelected(1)));
        side.AddChild(MakeButton(I18n.T("btn_move_top"), MoveToTop));
        side.AddChild(MakeButton(I18n.T("btn_move_bottom"), MoveToBottom));
        side.AddChild(MakeButton(I18n.T("btn_sort_alpha"), SortByAlphabet));
        side.AddChild(MakeButton(I18n.T("btn_reload"), RefreshFromRuntime));
        side.AddChild(MakeButton(I18n.T("btn_export_clipboard"), ExportToClipboard));
        side.AddChild(MakeButton(I18n.T("btn_import_clipboard"), ImportFromClipboard));

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        root.AddChild(footer);

        footer.AddChild(MakeButton(I18n.T("btn_apply"), ApplyOrder));
        footer.AddChild(MakeButton(I18n.T("btn_close"), () => Visible = false));

        _statusLabel = new Label
        {
            Text = I18n.T("status_ready"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
        footer.AddChild(_statusLabel);
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.All
        };
        button.Pressed += onPressed;
        return button;
    }

    private void RefreshFromRuntime()
    {
        if (!LoadOrderRuntime.TryGetOrderedEntries(out var entries, out var error))
        {
            _entries.Clear();
            RefreshListOnly();
            SetStatus(I18n.Tf("status_load_failed", error));
            DebugLog.Error($"Refresh failed: {error}");
            RefreshOverwriteWarning();
            return;
        }

        _entries.Clear();
        _entries.AddRange(entries);
        RefreshListOnly();

        if (_entries.Count > 0)
        {
            _list.Select(0);
        }

        SetStatus(I18n.Tf("status_loaded", _entries.Count));
        DebugLog.Info($"Loaded {_entries.Count} mods into panel.");
        RefreshOverwriteWarning();
    }

    private void RefreshOverwriteWarning()
    {
        if (LoadOrderRuntime.TryBuildOverwriteWarning(out var warningText) &&
            !string.IsNullOrWhiteSpace(warningText))
        {
            _warningLabel.Text = warningText;
            _warningLabel.Visible = true;
            return;
        }

        _warningLabel.Text = string.Empty;
        _warningLabel.Visible = false;
    }

    private void RefreshListOnly()
    {
        _list.Clear();

        for (var i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            var disabled = e.IsEnabled ? "" : I18n.T("suffix_disabled");
            var text = $"{i + 1:00}. [{e.SourceText}] {e.Name} ({e.Id}){disabled}";
            _list.AddItem(text);
        }
    }

    private int GetSelectedIndex()
    {
        var selected = _list.GetSelectedItems();
        return selected.Length == 0 ? -1 : selected[0];
    }

    private void MoveSelected(int delta)
    {
        var selected = GetSelectedIndex();
        if (selected < 0) return;

        var target = selected + delta;
        if (target < 0 || target >= _entries.Count) return;

        (_entries[selected], _entries[target]) = (_entries[target], _entries[selected]);
        RefreshListOnly();
        _list.Select(target);
        SetStatus(I18n.Tf("status_moved_pos", target + 1));
    }

    private void MoveToTop()
    {
        var selected = GetSelectedIndex();
        if (selected <= 0) return;

        var entry = _entries[selected];
        _entries.RemoveAt(selected);
        _entries.Insert(0, entry);
        RefreshListOnly();
        _list.Select(0);
        SetStatus(I18n.T("status_moved_top"));
    }

    private void MoveToBottom()
    {
        var selected = GetSelectedIndex();
        if (selected < 0 || selected >= _entries.Count - 1) return;

        var entry = _entries[selected];
        _entries.RemoveAt(selected);
        _entries.Add(entry);
        RefreshListOnly();
        _list.Select(_entries.Count - 1);
        SetStatus(I18n.T("status_moved_bottom"));
    }

    private void SortByAlphabet()
    {
        if (_entries.Count < 2)
        {
            SetStatus(I18n.T("status_nothing_to_sort"));
            return;
        }

        var selected = GetSelectedIndex();
        var selectedKey = selected >= 0 && selected < _entries.Count
            ? _entries[selected].Key
            : null;

        _entries.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            if (byName != 0) return byName;

            var byId = string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            if (byId != 0) return byId;

            return a.Source.CompareTo(b.Source);
        });

        RefreshListOnly();
        if (!string.IsNullOrEmpty(selectedKey))
        {
            var newIndex = _entries.FindIndex(e => string.Equals(e.Key, selectedKey, StringComparison.Ordinal));
            if (newIndex >= 0)
            {
                _list.Select(newIndex);
            }
        }

        SetStatus(I18n.T("status_sorted_alpha"));
        DebugLog.Info($"Manual alphabetical sort completed. entries={_entries.Count}");
    }

    private void ExportToClipboard()
    {
        if (_entries.Count == 0)
        {
            SetStatus(I18n.T("status_nothing_to_export"));
            return;
        }

        try
        {
            var payload = new ClipboardPayload
            {
                exported_at_utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                entries = _entries.Select(e => new ClipboardEntry
                {
                    key = e.Key,
                    id = e.Id,
                    source = e.Source,
                    is_enabled = e.IsEnabled
                }).ToList()
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            DisplayServer.ClipboardSet(json);

            SetStatus(I18n.Tf("status_exported_clipboard", payload.entries.Count));
            DebugLog.Info($"Exported load-order config to clipboard. entries={payload.entries.Count}");
        }
        catch (Exception ex)
        {
            SetStatus(I18n.Tf("status_export_failed", ex.Message));
            DebugLog.Error("ExportToClipboard failed.", ex);
        }
    }

    private void ImportFromClipboard()
    {
        try
        {
            var raw = DisplayServer.ClipboardGet();
            if (string.IsNullOrWhiteSpace(raw))
            {
                SetStatus(I18n.T("status_clipboard_empty"));
                return;
            }

            if (!TryParseClipboard(raw, out var importedEntries, out var parseError))
            {
                SetStatus(I18n.Tf("status_import_parse_failed", parseError));
                return;
            }

            if (importedEntries.Count == 0)
            {
                SetStatus(I18n.T("status_import_no_valid"));
                return;
            }

            var matchedOrder = new List<LoadOrderEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var missingCount = 0;
            var duplicateCount = 0;

            foreach (var imported in importedEntries)
            {
                if (!TryResolveImportedEntry(imported, out var target))
                {
                    missingCount++;
                    continue;
                }

                if (!seen.Add(target.Key))
                {
                    duplicateCount++;
                    continue;
                }

                var importedEnabled = imported.ResolveEnabled();
                if (importedEnabled.HasValue)
                {
                    target.IsEnabled = importedEnabled.Value;
                }

                matchedOrder.Add(target);
            }

            if (matchedOrder.Count == 0)
            {
                SetStatus(I18n.Tf("status_import_no_match", missingCount));
                DebugLog.Warn($"Clipboard import found no matching mods. parsed={importedEntries.Count}, missing={missingCount}");
                return;
            }

            var remaining = _entries.Where(e => !seen.Contains(e.Key)).ToList();
            _entries.Clear();
            _entries.AddRange(matchedOrder);
            _entries.AddRange(remaining);

            RefreshListOnly();
            if (_entries.Count > 0)
            {
                _list.Select(0);
            }

            SetStatus(I18n.Tf("status_imported_clipboard", matchedOrder.Count, missingCount, duplicateCount));
            DebugLog.Info(
                $"Imported load-order config from clipboard. parsed={importedEntries.Count}, matched={matchedOrder.Count}, missing={missingCount}, duplicates={duplicateCount}");
        }
        catch (Exception ex)
        {
            SetStatus(I18n.Tf("status_import_failed", ex.Message));
            DebugLog.Error("ImportFromClipboard failed.", ex);
        }
    }

    private bool TryParseClipboard(string raw, out List<ClipboardEntry> entries, out string error)
    {
        entries = new List<ClipboardEntry>();
        error = string.Empty;

        if (TryParseClipboardJson(raw, out entries, out error))
        {
            return true;
        }

        entries = ParsePlainTextClipboard(raw);
        if (entries.Count > 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(error))
        {
            error = I18n.T("status_import_invalid_format");
        }

        return false;
    }

    private bool TryParseClipboardJson(string raw, out List<ClipboardEntry> entries, out string error)
    {
        entries = new List<ClipboardEntry>();
        error = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("entries", out var entriesElement))
                {
                    entries = ParseClipboardJsonArray(entriesElement);
                    return true;
                }

                if (root.TryGetProperty("mod_list", out var modListElement))
                {
                    entries = ParseClipboardJsonArray(modListElement);
                    return true;
                }

                return false;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                entries = ParseClipboardJsonArray(root);
                return true;
            }

            return false;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private List<ClipboardEntry> ParseClipboardJsonArray(JsonElement element)
    {
        var list = new List<ClipboardEntry>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in element.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    {
                        var token = item.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            list.Add(new ClipboardEntry { key = token.Trim(), id = token.Trim() });
                        }
                        break;
                    }
                case JsonValueKind.Object:
                    {
                        var entry = new ClipboardEntry
                        {
                            key = GetJsonString(item, "key"),
                            id = GetJsonString(item, "id"),
                            source = GetJsonInt(item, "source"),
                            is_enabled = GetJsonBool(item, "is_enabled") ?? GetJsonBool(item, "isEnabled"),
                            enabled = GetJsonBool(item, "enabled")
                        };

                        if (!string.IsNullOrWhiteSpace(entry.key) ||
                            !string.IsNullOrWhiteSpace(entry.id) ||
                            entry.source.HasValue)
                        {
                            list.Add(entry);
                        }
                        break;
                    }
            }
        }

        return list;
    }

    private static string GetJsonString(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int? GetJsonInt(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i))
        {
            return i;
        }

        return null;
    }

    private static bool? GetJsonBool(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }

        return null;
    }

    private List<ClipboardEntry> ParsePlainTextClipboard(string raw)
    {
        var list = new List<ClipboardEntry>();
        foreach (var token in TokenSplitRegex.Split(raw))
        {
            var trimmed = token.Trim().Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            list.Add(new ClipboardEntry { key = trimmed, id = trimmed });
        }

        return list;
    }

    private bool TryResolveImportedEntry(ClipboardEntry imported, out LoadOrderEntry entry)
    {
        entry = null!;

        var importedKey = imported.key?.Trim() ?? string.Empty;
        var importedId = imported.id?.Trim() ?? string.Empty;
        var importedSource = imported.source;

        if (!string.IsNullOrWhiteSpace(importedKey) &&
            TryFindByKey(importedKey, out entry))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(importedKey) &&
            TryParseEntryKey(importedKey, out var keyId, out var keySource))
        {
            importedId = keyId;
            importedSource ??= keySource;
        }

        if (string.IsNullOrWhiteSpace(importedId) && !string.IsNullOrWhiteSpace(importedKey))
        {
            importedId = importedKey;
        }

        if (string.IsNullOrWhiteSpace(importedId))
        {
            return false;
        }

        if (importedSource.HasValue)
        {
            var exactKey = LoadOrderEntry.BuildKey(importedId, importedSource.Value);
            if (TryFindByKey(exactKey, out entry))
            {
                return true;
            }
        }

        var sameId = _entries
            .Where(e => string.Equals(e.Id, importedId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameId.Count == 0)
        {
            return false;
        }

        if (sameId.Count == 1)
        {
            entry = sameId[0];
            return true;
        }

        if (importedSource.HasValue)
        {
            var sameSource = sameId.FirstOrDefault(e => e.Source == importedSource.Value);
            if (sameSource != null)
            {
                entry = sameSource;
                return true;
            }
        }

        entry = sameId.FirstOrDefault(e => e.Source == 1) ?? sameId[0];
        return true;
    }

    private bool TryFindByKey(string key, out LoadOrderEntry entry)
    {
        entry = _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))!;
        return entry != null;
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

        var idPart = key[..pos].Trim();
        var sourcePart = key[(pos + 2)..].Trim();
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

    private void ApplyOrder()
    {
        if (_entries.Count == 0)
        {
            SetStatus(I18n.T("status_nothing_to_save"));
            return;
        }

        if (!LoadOrderRuntime.TrySaveOrderedEntries(_entries, out var error))
        {
            SetStatus(I18n.Tf("status_save_failed", error));
            DebugLog.Error($"Apply failed: {error}");
            RefreshOverwriteWarning();
            return;
        }

        SetStatus(I18n.T("status_saved"));
        DebugLog.Info("Apply succeeded. Restart required for effect.");
        RefreshOverwriteWarning();
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }
}
