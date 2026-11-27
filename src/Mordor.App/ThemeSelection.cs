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

/// <summary>
/// The port's Light and Dark themes (the port's own; the original has only its Windows 3.1 grey).
/// The choice is settings.ini's [Appearance] Theme: Dark, Light or System (the operating system's,
/// or the browser's, light or dark setting). With nothing saved it is Dark, the port's look before
/// there was a choice, so an update changes nobody's game. It is made in the Options window on
/// every system, and on Windows in each game window's system menu too.
///
/// The brushes below are shared: every window takes the same instances, and a change of theme
/// recolours them in place. They start in the default theme's colours, so a host that never calls
/// <see cref="Initialize"/> (the browser page) still draws a whole theme.
/// </summary>
internal static class ThemeSelection
{
    internal enum Choice
    {
        System,
        Light,
        Dark,
    }

    /// <summary>The theme with nothing saved: Dark, the port's look before the choice existed.</summary>
    private const Choice Default = Choice.Dark;

    private const string Section = "Appearance";
    private const string Key = "Theme";
    private const uint WmSysCommand = 0x0112;
    private const uint WmInitMenuPopup = 0x0117;
    private const uint MfSeparator = 0x0800;
    private const uint MfPopup = 0x0010;
    // the system-menu commands: below 0xF000, as a window's own SC_ items must be, and multiples of
    // 16, as WM_SYSCOMMAND keeps the low four bits for itself (the handler masks them off)
    private const uint ScSystemTheme = 0x1F00;
    private const uint ScLightTheme = 0x1F10;
    private const uint ScDarkTheme = 0x1F20;

    private static Choice _choice = Default;
    private static IClassicDesktopStyleApplicationLifetime? _desktop;
    internal static event Action? ThemeChanged;

    /// <summary>The theme chosen: what the Options window shows.</summary>
    internal static Choice Current => _choice;

    internal static bool IsLight => _choice == Choice.Light
        || (_choice == Choice.System && Application.Current?.ActualThemeVariant != ThemeVariant.Dark);

    // the brushes take the default theme's colours before anything draws with them
    static ThemeSelection()
    {
        Paint(Default == Choice.Light);
        // a text box given a ground of its own keeps it under the pointer and with the keyboard: the
        // theme swaps in its white for both (TextControlBackgroundPointerOver and ...Focused), and
        // the Library's yellow page turned white when moused over. Boxes on the theme's own ground
        // are left as the theme draws them.
        TextBox.BackgroundProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.Priority != Avalonia.Data.BindingPriority.LocalValue) return;
            box.Resources["TextControlBackgroundPointerOver"] = e.NewValue;
            box.Resources["TextControlBackgroundFocused"] = e.NewValue;
        });
    }

    public static void Initialize(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        if (desktop.Windows is INotifyCollectionChanged windows)
            windows.CollectionChanged += (_, _) => UpdateWindowBackgrounds();
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += (_, _) => UpdateWindowBackgrounds();

        string saved = Ini.Read(GameFolder.LaunchIniPath).Text(Section, Key);
        Choice? read = saved.ToUpperInvariant() switch
        {
            "LIGHT" => Choice.Light,
            "DARK" => Choice.Dark,
            // "Windows" is what the first builds of the choice wrote for System
            "SYSTEM" or "WINDOWS" => Choice.System,
            _ => null,
        };
        if (read is null && saved != "")
            CrashLog.Note("unknown theme setting " + saved + "; using " + Default);
        _choice = read ?? Default;
        Apply();
    }

    /// <summary>The window's ground, and the Light theme's relief inside it (ClassicLook).</summary>
    public static void ApplyWindowSurface(Window window)
    {
        window.Background = Palette.Window;
        Classic(window);
        _surfaces.RemoveAll(w => !w.TryGetTarget(out _));
        _surfaces.Add(new WeakReference<StyledElement>(window));
    }

    /// <summary>The windows given the surface, for the relief to follow the theme in each.</summary>
    private static readonly System.Collections.Generic.List<WeakReference<StyledElement>> _surfaces = new();
    private static bool _classicStyled;

    /// <summary>ClassicLook's class on a window in the Light theme, its styles in the application once.</summary>
    private static void Classic(StyledElement window)
    {
        if (!_classicStyled && Application.Current is { } app)
        {
            app.Styles.Add(ClassicLook.Styles);
            // the colour roles by name, for the mods (ThemeKeys): the same brushes, recoloured in place
            Palette.Publish(app.Resources);
            _classicStyled = true;
        }
        window.Classes.Set(ClassicLook.ClassName, IsLight);
    }

    internal static void ApplyPartyHeader(Grid header)
    {
        foreach (Control child in header.Children)
            if (child is TextBlock label)
                label.Foreground = Palette.Caption;
    }

    internal static void ApplyCommandButtonTheme(Canvas canvas, params Button[] buttons)
    {
        canvas.Resources["ButtonBackground"] = Palette.Button;
        canvas.Resources["ButtonBackgroundPointerOver"] = Palette.Window;
        canvas.Resources["ButtonBackgroundPressed"] = Palette.Shadow;
        canvas.Resources["ButtonBackgroundDisabled"] = Palette.Button;
        foreach (Button button in buttons)
            button.Foreground = Palette.Text;
    }

    internal static void ApplyItemCommandTheme(Canvas canvas, params Button[] buttons) =>
        ApplyCommandButtonTheme(canvas, buttons);

    public static void Attach(Window window)
    {
        ApplyWindowSurface(window);
        if (!OperatingSystem.IsWindows()) return;

        IntPtr themeMenu = IntPtr.Zero;

        Win32Properties.CustomWndProcHookCallback hook =
            (IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == WmInitMenuPopup && wParam == themeMenu)
                {
                    UpdateCheck(themeMenu);
                }
                else if (message == WmSysCommand)
                {
                    uint command = unchecked((uint)wParam.ToInt64()) & 0xFFF0;

                    Choice? selected = command switch
                    {
                        ScSystemTheme => Choice.System,
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
            if (hwnd == IntPtr.Zero) return;

            // Reset system menu to remove any previous theme submenu.
            GetSystemMenu(hwnd, true);

            IntPtr systemMenu = GetSystemMenu(hwnd, false);
            themeMenu = CreatePopupMenu();

            if (systemMenu == IntPtr.Zero || themeMenu == IntPtr.Zero
                || !AppendMenuW(themeMenu, 0,
                    new IntPtr(unchecked((int)ScLightTheme)), "Light")
                || !AppendMenuW(themeMenu, 0,
                    new IntPtr(unchecked((int)ScDarkTheme)), "Dark")
                || !AppendMenuW(themeMenu, 0,
                    new IntPtr(unchecked((int)ScSystemTheme)), "Same as Windows")
                || !AppendMenuW(systemMenu, MfSeparator, IntPtr.Zero, null)
                || !AppendMenuW(systemMenu, MfPopup, themeMenu, "Theme Selection"))
            {
                CrashLog.Note("Could not add theme selection to Windows system menu ("
                    + Marshal.GetLastWin32Error() + ")");
                return;
            }

            UpdateCheck(themeMenu);
        };

        window.Closed += (_, _) =>
            Win32Properties.RemoveWndProcHookCallback(window, hook);
    }

    /// <summary>Takes a theme at once and saves it to settings.ini.</summary>
    internal static void Select(Choice choice)
    {
        if (choice == _choice) return;
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
        Paint(IsLight);
        ClassicLook.Set(IsLight);
        if (_desktop is not null)
            foreach (Window window in _desktop.Windows)
            {
                window.Background = Palette.Window;
                Classic(window);
            }
        _surfaces.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in _surfaces)
            if (w.TryGetTarget(out var surface)) Classic(surface);
        ThemeChanged?.Invoke();
    }

    /// <summary>Every role's colour in the Light or the Dark theme (Palette).</summary>
    private static void Paint(bool light) => Palette.Paint(light);


    /// <summary>The system menu's item ticked for a theme: each its own, Same as Windows for System (it had ticked Light).</summary>
    internal static uint MenuCommandFor(Choice choice) => choice switch
    {
        Choice.Light => ScLightTheme,
        Choice.Dark => ScDarkTheme,
        _ => ScSystemTheme,
    };

    private static void UpdateCheck(IntPtr menu) => CheckMenuRadioItem(menu, ScSystemTheme, ScDarkTheme, MenuCommandFor(_choice), 0);

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
