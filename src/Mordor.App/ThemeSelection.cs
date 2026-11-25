using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Mordor.Core;

namespace Mordor.App;

internal static class ThemeSelection
{
    private enum Choice
    {
        Windows,
        Light,
        Dark,
    }

    private const string Section = "Appearance";
    private const string Key = "Theme";
    private const uint WmSysCommand = 0x0112;
    private const uint WmInitMenuPopup = 0x0117;
    private const uint MfSeparator = 0x0800;
    private const uint MfPopup = 0x0010;
    private const uint ScWindowsTheme = 0x1F00;
    private const uint ScLightTheme = 0x1F10;
    private const uint ScDarkTheme = 0x1F20;

    private static Choice _choice = Choice.Windows;
    private static IClassicDesktopStyleApplicationLifetime? _desktop;
    internal static SolidColorBrush WindowSurface { get; } = new(Color.Parse("#101018"));
    internal static SolidColorBrush ContentSurface { get; } = new(Color.Parse("#0a0a10"));
    internal static SolidColorBrush RaisedSurface { get; } = new(Color.Parse("#16161f"));
    internal static SolidColorBrush DialogText { get; } = new(Color.Parse("#d8d8df"));
    internal static SolidColorBrush FormFace { get; } = new(Color.Parse("#141420"));
    internal static SolidColorBrush FormInk { get; } = new(Color.Parse("#d8d8e0"));
    internal static SolidColorBrush FormBevel { get; } = new(Color.Parse("#555566"));
    internal static SolidColorBrush FormDisplayField { get; } = new(Color.Parse("#33333a"));
    internal static SolidColorBrush FormListSurface { get; } = new(Color.Parse("#0a0a10"));
    internal static SolidColorBrush FormBlue { get; } = new(Color.Parse("#6495ed"));
    internal static SolidColorBrush FormMaroon { get; } = new(Color.Parse("#cd5c5c"));
    internal static SolidColorBrush FormNavy { get; } = new(Color.Parse("#8aa8e8"));
    internal static SolidColorBrush StoreMutedInk { get; } = new(Color.Parse("#808080"));
    internal static SolidColorBrush PartyPlainInk { get; } = new(Color.Parse("#f5f5f5"));
    internal static SolidColorBrush PartySelectedInk { get; } = new(Color.Parse("#90ee90"));
    internal static SolidColorBrush PartyFixedInk { get; } = new(Color.Parse("#6495ed"));
    internal static SolidColorBrush PartyWellInk { get; } = new(Color.Parse("#6495ed"));
    internal static SolidColorBrush PartyAilingInk { get; } = new(Color.Parse("#cd5c5c"));
    internal static SolidColorBrush PartyHeaderInk { get; } = new(Color.Parse("#888888"));

    public static void Initialize(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        if (desktop.Windows is INotifyCollectionChanged windows)
            windows.CollectionChanged += (_, _) => UpdateWindowBackgrounds();
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => UpdateWindowBackgrounds();

        string saved = Ini.Read(GameFolder.LaunchIniPath).Text(Section, Key);
        _choice = saved.ToUpperInvariant() switch
        {
            "LIGHT" => Choice.Light,
            "DARK" => Choice.Dark,
            "WINDOWS" or "SYSTEM" or "" => Choice.Windows,
            _ => Choice.Windows,
        };
        if (saved != "" && saved.ToUpperInvariant() is not ("LIGHT" or "DARK" or "WINDOWS" or "SYSTEM"))
            CrashLog.Note("unknown theme setting " + saved + "; using the Windows theme");
        Apply();
    }

    public static void ApplyWindowSurface(Window window) => window.Background = WindowSurface;

    internal static void ApplyPartyHeader(Grid header)
    {
        foreach (Control child in header.Children)
            if (child is TextBlock label)
                label.Foreground = PartyHeaderInk;
    }

    internal static void ApplyItemCommandTheme(Canvas canvas, params Button[] buttons)
    {
        canvas.Resources["ButtonBackground"] = FormFace;
        canvas.Resources["ButtonBackgroundPointerOver"] = RaisedSurface;
        canvas.Resources["ButtonBackgroundPressed"] = FormBevel;
        canvas.Resources["ButtonBackgroundDisabled"] = FormFace;
        foreach (Button button in buttons)
            button.Foreground = FormInk;
    }

    public static void Attach(Window window)
    {
        ApplyWindowSurface(window);
        if (!OperatingSystem.IsWindows()) return;

        IntPtr themeMenu = IntPtr.Zero;
        Win32Properties.CustomWndProcHookCallback hook = (IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == WmInitMenuPopup && wParam == themeMenu)
                UpdateCheck(themeMenu);
            else if (message == WmSysCommand)
            {
                uint command = unchecked((uint)wParam.ToInt64()) & 0xFFF0;
                Choice? selected = command switch
                {
                    ScWindowsTheme => Choice.Windows,
                    ScLightTheme => Choice.Light,
                    ScDarkTheme => Choice.Dark,
                    _ => null,
                };
                if (selected is Choice choice)
                {
                    Select(choice);
                    UpdateCheck(themeMenu);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        };

        Win32Properties.AddWndProcHookCallback(window, hook);
        window.Opened += (_, _) =>
        {
            IntPtr hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            IntPtr systemMenu = hwnd == IntPtr.Zero ? IntPtr.Zero : GetSystemMenu(hwnd, false);
            themeMenu = CreatePopupMenu();
            if (systemMenu == IntPtr.Zero || themeMenu == IntPtr.Zero
                || !AppendMenuW(themeMenu, 0, new IntPtr(unchecked((int)ScWindowsTheme)), "Windows (system)")
                || !AppendMenuW(themeMenu, 0, new IntPtr(unchecked((int)ScLightTheme)), "Light")
                || !AppendMenuW(themeMenu, 0, new IntPtr(unchecked((int)ScDarkTheme)), "Dark")
                || !AppendMenuW(systemMenu, MfSeparator, IntPtr.Zero, null)
                || !AppendMenuW(systemMenu, MfPopup, themeMenu, "Theme Selection"))
            {
                CrashLog.Note("could not add theme selection to the Windows system menu (" + Marshal.GetLastWin32Error() + ")");
                return;
            }
            UpdateCheck(themeMenu);
        };
        window.Closed += (_, _) => Win32Properties.RemoveWndProcHookCallback(window, hook);
    }

    private static void Select(Choice choice)
    {
        _choice = choice;
        Apply();

        string path = GameFolder.LaunchIniPath;
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } parent) Directory.CreateDirectory(parent);
        }
        catch (Exception ex)
        {
            CrashLog.Write("creating settings folder for theme", ex);
            return;
        }

        var ini = Ini.Read(path);
        ini.Set(Section, Key, choice.ToString());
        if (!ini.Write(path)) CrashLog.Note("could not save theme selection to " + path);
    }

    private static void Apply()
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = _choice switch
        {
            Choice.Light => ThemeVariant.Light,
            Choice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        UpdateWindowBackgrounds();
    }

    private static void UpdateWindowBackgrounds()
    {
        if (_desktop is null || Application.Current is not { } app) return;

        bool light = _choice == Choice.Light
            || (_choice == Choice.Windows && app.ActualThemeVariant != ThemeVariant.Dark);
        WindowSurface.Color = Color.Parse(light ? "#F0F0F0" : "#101018");
        ContentSurface.Color = Color.Parse(light ? "#E6E6E6" : "#0a0a10");
        RaisedSurface.Color = Color.Parse(light ? "#FFFFFF" : "#16161f");
        DialogText.Color = Color.Parse(light ? "#202020" : "#d8d8df");
        FormFace.Color = Color.Parse(light ? "#C0C0C0" : "#141420");
        FormInk.Color = Color.Parse(light ? "#000000" : "#d8d8e0");
        FormBevel.Color = Color.Parse(light ? "#808080" : "#555566");
        FormDisplayField.Color = Color.Parse(light ? "#FFFFFF" : "#33333a");
        FormListSurface.Color = Color.Parse(light ? "#FFFFFF" : "#0a0a10");
        FormBlue.Color = Color.Parse(light ? "#0000FF" : "#6495ed");
        FormMaroon.Color = Color.Parse(light ? "#800000" : "#cd5c5c");
        FormNavy.Color = Color.Parse(light ? "#000080" : "#8aa8e8");
        StoreMutedInk.Color = Color.Parse(light ? "#666666" : "#808080");
        PartyPlainInk.Color = Color.Parse(light ? "#202020" : "#f5f5f5");
        PartySelectedInk.Color = Color.Parse(light ? "#006400" : "#90ee90");
        PartyFixedInk.Color = Color.Parse(light ? "#000080" : "#6495ed");
        PartyWellInk.Color = Color.Parse(light ? "#000080" : "#6495ed");
        PartyAilingInk.Color = Color.Parse(light ? "#800000" : "#cd5c5c");
        PartyHeaderInk.Color = Color.Parse(light ? "#666666" : "#888888");
        foreach (Window window in _desktop.Windows)
            window.Background = WindowSurface;
    }

    private static void UpdateCheck(IntPtr menu)
    {
        uint command = _choice switch
        {
            Choice.Light => ScLightTheme,
            Choice.Dark => ScDarkTheme,
            _ => ScWindowsTheme,
        };
        CheckMenuRadioItem(menu, ScWindowsTheme, ScDarkTheme, command, 0);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetSystemMenu(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool revert);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr item, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckMenuRadioItem(IntPtr menu, uint first, uint last, uint check, uint flags);
}
