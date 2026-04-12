using System;
using System.Collections.Generic;
using Godot;

namespace LoadOrderManager;

public partial class LoadOrderPanel : Control
{
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
        side.AddChild(MakeButton(I18n.T("btn_reload"), RefreshFromRuntime));

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
