using System;
using System.Runtime.CompilerServices;
using Godot;

namespace LoadOrderManager;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        try
        {
            LoadOrderManagerMod.Initialize();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadOrderManager] ModuleInitializer failed: {ex}");
        }
    }
}
