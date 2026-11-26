using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mordor.Mod.Cartographer;

/// <summary>
/// The route settings, opened from the Automap's right-click menu ("Route settings..."): whether routes
/// take waypoints, what a cast of Ethereal Portal counts (or Don't use, the default), and for each hazard the extra actions stepping onto it counts - a whole number typed
/// in its box - or Avoid. Every change is written at once (<see cref="CartographerMod.SetUseWaypoints"/>,
/// <see cref="CartographerMod.SetExtra"/>); the host then asks the Automap's marks again, so a route on
/// the map follows as the numbers are typed. One at a time: asked again, the open one comes to the front.
/// Laid out as the Mods page's settings windows are. The game does not guard the handlers of a mod's own
/// controls, so each catches its own failure and reports it to the mod's log.
/// </summary>
internal static class RouteSettingsWindow
{
    private static readonly IBrush Heading = Brush.Parse("#8fb0ff");
    private static readonly IBrush Faint = Brush.Parse("#9a9aae");

    private static Window? _open;

    /// <summary>The hazards' names as the window shows them.</summary>
    private static string Name(Hazard h) => h switch
    {
        Hazard.AntiMagic => "Anti-magic",
        _ => h.ToString(),
    };

    public static void Close()
    {
        var w = _open;
        _open = null;
        w?.Close();
    }

    public static void Open(CartographerMod mod, Action<string, Exception> report)
    {
        void Safe(string what, Action act)
        {
            try { act(); }
            catch (Exception ex) { try { report("route settings: " + what, ex); } catch { } }
        }

        if (_open is { IsVisible: true } already) { already.Activate(); return; }

        // a number box: whole numbers only - anything else typed or pasted is taken straight out again -
        // written as it changes; left empty, it counts 0 and says so once the player moves on
        void Digits(TextBox box, Action write)
        {
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property != TextBox.TextProperty) return;
                Safe("typing", () =>
                {
                    string t = box.Text ?? "";
                    string digits = new(t.Where(char.IsAsciiDigit).ToArray());
                    if (digits != t) { box.Text = digits; return; }   // sets Text again: written then
                    if (box.IsEnabled) write();
                });
            };
            box.LostFocus += (_, _) => Safe("leaving", () => { if (string.IsNullOrEmpty(box.Text)) box.Text = "0"; });
        }

        var w = new Window
        {
            Title = "Route settings",
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brush.Parse("#101018"),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            CanResize = false,
            ShowInTaskbar = false,
        };

        var waypoints = new CheckBox { Content = "Take waypoints", IsChecked = mod.Rules().UseWaypoints, FontSize = 12 };
        ToolTip.SetTip(waypoints, "A route may step onto a waypoint's source to reach its destination (on stairs, with t).");
        waypoints.IsCheckedChanged += (_, _) => Safe("waypoints", () => mod.SetUseWaypoints(waypoints.IsChecked == true));

        // Ethereal Portal: what a cast counts, or Don't use (the default)
        int? portal = mod.Rules().PortalCost;
        var portalBox = new TextBox
        {
            Text = (portal ?? 1).ToString(),
            Width = 56, MaxLength = 3, FontSize = 12,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            IsEnabled = portal != null,
            Margin = new Thickness(0, 2, 12, 2),
        };
        var portalOff = new CheckBox { Content = "Don't use", IsChecked = portal == null, FontSize = 12 };
        ToolTip.SetTip(portalBox, "Actions one cast counts: the cast, and whatever else you reckon it costs (0 to 999). "
                                + "It goes one square the way you face through any wall, onto ground you have stood on; never cast where the map shows anti-magic or an extinguisher.");
        ToolTip.SetTip(portalOff, "Routes never cast Ethereal Portal.");
        void WritePortal() => mod.SetPortalCost(portalOff.IsChecked == true ? null : int.TryParse(portalBox.Text, out int v) ? v : 0);
        Digits(portalBox, WritePortal);
        portalOff.IsCheckedChanged += (_, _) => Safe("portal", () => { portalBox.IsEnabled = portalOff.IsChecked != true; WritePortal(); });
        var portalRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = "Ethereal Portal", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) },
                portalBox, portalOff,
            },
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions(string.Join(",", Enumerable.Repeat("Auto", Enum.GetValues<Hazard>().Length))),
            Margin = new Thickness(18, 0, 0, 0),
        };
        int row = 0;
        var penalties = mod.Rules().Penalties;
        foreach (var h in Enum.GetValues<Hazard>())
        {
            var hazard = h;
            bool avoid = penalties.TryGetValue(hazard, out int? p) && p == null;
            int n = p ?? 0;
            var label = new TextBlock { Text = Name(hazard), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var box = new TextBox
            {
                Text = n.ToString(),
                Width = 56,
                MaxLength = 3,
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                IsEnabled = !avoid,
                Margin = new Thickness(0, 2, 12, 2),
            };
            var off = new CheckBox { Content = "Avoid", IsChecked = avoid, FontSize = 12 };
            ToolTip.SetTip(off, "Never step onto it, unless it is where the route goes.");
            ToolTip.SetTip(box, "Extra actions counted for stepping onto it, on top of the step (0 to 999).");

            void Write() => mod.SetExtra(hazard, off.IsChecked == true ? null : int.TryParse(box.Text, out int v) ? v : 0);
            Digits(box, Write);
            off.IsCheckedChanged += (_, _) => Safe("avoid", () => { box.IsEnabled = off.IsChecked != true; Write(); });

            Grid.SetRow(label, row); Grid.SetColumn(label, 0);
            Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            Grid.SetRow(off, row); Grid.SetColumn(off, 2);
            grid.Children.Add(label); grid.Children.Add(box); grid.Children.Add(off);
            row++;
        }

        Border Group(string caption, string note, Control body) => new()
        {
            BorderBrush = Brush.Parse("#3a3a4e"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = caption, Foreground = Heading, FontWeight = FontWeight.Bold, FontSize = 12 },
                    new TextBlock { Text = note, Foreground = Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left },
                    body,
                },
            },
        };

        var close = new Button { Content = "_Close", IsDefault = true, IsCancel = true, Width = 120, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => w.Close();

        w.Content = new Border
        {
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Group("Routes", "A step, a turn and t on stairs are one action each.",
                          new StackPanel { Margin = new Thickness(18, 0, 0, 0), Spacing = 4, Children = { waypoints, portalRow } }),
                    Group("Hazards", "Extra actions for stepping onto each hazard the map shows, or Avoid.", grid),
                    close,
                },
            },
        };
        w.Closed += (_, _) => { if (ReferenceEquals(_open, w)) _open = null; };
        _open = w;

        // over the window that asked (the Automap's, or the game's frame it sits in)
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(x => x.IsActive);
        if (owner != null) { w.WindowStartupLocation = WindowStartupLocation.CenterOwner; w.Show(owner); }
        else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; w.Show(); }
    }
}
