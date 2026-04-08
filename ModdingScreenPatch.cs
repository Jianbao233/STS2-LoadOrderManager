using System;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace LoadOrderManager;

[HarmonyPatch]
internal static class ModdingScreenReadyPatch
{
    private const string ScreenTypeName = "MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen.NModdingScreen";

    static MethodBase? TargetMethod()
    {
        var type = AccessTools.TypeByName(ScreenTypeName) ?? AccessTools.TypeByName("NModdingScreen");
        return type?.GetMethod("_Ready", BindingFlags.Public | BindingFlags.Instance);
    }

    static void Postfix(object __instance)
    {
        try
        {
            LoadOrderUiInjector.TryInject(__instance);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadOrderManager] UI inject failed: {ex.Message}");
        }
    }
}

internal static class LoadOrderUiInjector
{
    private const string OpenButtonName = "LoadOrderManager_OpenButton";
    private const string PanelName = "LoadOrderManager_Panel";

    public static void TryInject(object screenObject)
    {
        if (screenObject is not Control screen) return;
        if (!GodotObject.IsInstanceValid(screen)) return;
        if (screen.FindChild(OpenButtonName, true, false) != null) return;

        var button = new Button
        {
            Name = OpenButtonName,
            Text = I18n.T("open_button"),
            TooltipText = I18n.T("open_button_tooltip"),
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        button.AnchorLeft = 1f;
        button.AnchorRight = 1f;
        button.AnchorTop = 0f;
        button.AnchorBottom = 0f;
        button.OffsetLeft = -220f;
        button.OffsetRight = -20f;
        button.OffsetTop = 60f;
        button.OffsetBottom = 98f;
        button.ZIndex = 40;

        button.Pressed += () =>
        {
            var panel = EnsurePanel(screen);
            panel.OpenPanel();
        };

        screen.AddChild(button);
        GD.Print("[LoadOrderManager] Injected load-order button.");
    }

    private static LoadOrderPanel EnsurePanel(Control screen)
    {
        if (screen.FindChild(PanelName, true, false) is LoadOrderPanel existing)
        {
            return existing;
        }

        var panel = new LoadOrderPanel { Name = PanelName };
        screen.AddChild(panel);
        return panel;
    }
}
