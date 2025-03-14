﻿using VpnHood.Core.Common.Exceptions;

namespace VpnHood.Core.Client.Device.UiContexts;

public static class AppUiContext
{
    private static IUiContext? _context;

    public static event EventHandler? OnChanged;

    public static IUiContext? Context {
        get => _context;
        set {
            if (_context == value)
                return;

            _context = value;
            OnChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static IUiContext RequiredContext => Context ?? throw new UiContextNotAvailableException();
    public static bool IsPartialIntentRunning => PartialIntentScope.IsRunning;

    // Partial activities may make the main activity animation pause
    public static IDisposable CreatePartialIntentScope() => new PartialIntentScope();
}