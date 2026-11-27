using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Mordor.App;

/// <summary>
/// The Light theme's Windows 3.1 relief: the original's 3-D buttons and its sunken and raised boxes,
/// one-pixel lines light along one pair of edges and dark along the other (game_files/dmainw1.png and
/// cityw1.png, the help's own pictures of the game: the buttons raised in a black line, the view, the
/// position, the monster box and the lists sunken). The Dark theme is the port's own and keeps its
/// flat lines. A GUI matter, not a rule (docs/FIDELITY.md): nothing here changes what the game does.
///
/// The lines are inset box shadows on a border with no outline of its own: a one-pixel outline under
/// them smears them across two pixels, where on their own they fall on whole pixels.
///
/// Buttons are restyled by <see cref="Styles"/>, under any window carrying the
/// <see cref="ClassName"/> class (ThemeSelection puts it on while the theme is Light). A box is
/// bevelled by naming it (<see cref="Sunken"/>, <see cref="Raised"/>): the change of theme then
/// takes its outline off and draws the relief, and puts the outline back for Dark.
/// </summary>
internal static class ClassicLook
{
    /// <summary>The class on a window whose contents take the relief.</summary>
    public const string ClassName = "classic";

    // the relief's colours, the palette's Light ones (the relief is the Light theme's alone)
    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static readonly string Light = Hex(Palette.LightOf(Palette.Highlight)), Shadow = Hex(Palette.LightOf(Palette.Shadow)),
                                   Dark = Hex(Palette.LightOf(Palette.Outline));

    /// <summary>A 3-D panel set in: shadow along the top and left, light along the bottom and right.</summary>
    private static readonly BoxShadows PanelIn = BoxShadows.Parse($"inset 1 1 0 0 {Shadow}, inset -1 -1 0 0 {Light}");
    /// <summary>A field set in two pixels deep, a list's or a text box's: shadow then black above and left.</summary>
    private static readonly BoxShadows FieldIn = BoxShadows.Parse($"inset 1 1 0 0 {Shadow}, inset 2 2 0 0 {Dark}, inset -1 -1 0 0 {Light}");
    /// <summary>A panel standing out: light along the top and left, shadow along the bottom and right.</summary>
    private static readonly BoxShadows PanelOut = BoxShadows.Parse($"inset -1 -1 0 0 {Shadow}, inset 1 1 0 0 {Light}");
    /// <summary>A button: a black line round it, then light above and left and two pixels of shadow below and right.</summary>
    private static readonly BoxShadows ButtonUp = BoxShadows.Parse(
        $"inset 1 1 0 0 {Dark}, inset -1 -1 0 0 {Dark}, inset 2 2 0 0 {Light}, inset -3 -3 0 0 {Shadow}");
    /// <summary>A button held down: the black line, and one pixel of shadow above and left.</summary>
    private static readonly BoxShadows ButtonDown = BoxShadows.Parse(
        $"inset 1 1 0 0 {Dark}, inset -1 -1 0 0 {Dark}, inset 2 2 0 0 {Shadow}");

    /// <summary>A tab not chosen: light above and left, shadow on the right and along its foot.</summary>
    private static readonly BoxShadows TabUp = BoxShadows.Parse($"inset 1 1 0 0 {Light}, inset -1 -1 0 0 {Shadow}");
    /// <summary>The chosen tab: light above and left, shadow on the right, open at its foot into the page.</summary>
    private static readonly BoxShadows TabOpen = BoxShadows.Parse($"inset 1 1 0 0 {Light}, inset -1 0 0 0 {Shadow}");

    private enum Relief { PanelIn, FieldIn, PanelOut }

    private sealed class Entry
    {
        public Relief Relief;
        public Thickness Outline;
    }

    private static readonly ConditionalWeakTable<Control, Entry> _boxes = new();
    private static readonly List<WeakReference<Control>> _order = new();
    private static bool _on;

    /// <summary>A panel set in (the view, the position, the monster box, the party's rows).</summary>
    public static T Sunken<T>(T box) where T : Control => Add(box, Relief.PanelIn);
    /// <summary>A field set in two pixels deep: a list or a box of text.</summary>
    public static T Field<T>(T box) where T : Control => Add(box, Relief.FieldIn);
    /// <summary>A panel standing out.</summary>
    public static T Raised<T>(T box) where T : Control => Add(box, Relief.PanelOut);

    private static T Add<T>(T box, Relief relief) where T : Control
    {
        if (_boxes.TryGetValue(box, out _)) return box;
        _boxes.Add(box, new Entry { Relief = relief, Outline = OutlineOf(box) });
        _order.RemoveAll(w => !w.TryGetTarget(out _));
        _order.Add(new WeakReference<Control>(box));
        // a list's border is in its template, there once the template is
        if (box is TemplatedControl t) t.TemplateApplied += (_, _) => Paint(box);
        Paint(box);
        return box;
    }

    /// <summary>The relief on (Light) or off (Dark), on every box named so far.</summary>
    public static void Set(bool on)
    {
        _on = on;
        _order.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in _order)
            if (w.TryGetTarget(out var box)) Paint(box);
    }

    private static Thickness OutlineOf(Control box) => box switch
    {
        Border b => b.BorderThickness,
        TemplatedControl t => t.BorderThickness,
        _ => default,
    };

    private static void Paint(Control box)
    {
        if (!_boxes.TryGetValue(box, out var e)) return;
        var shadows = e.Relief switch { Relief.FieldIn => FieldIn, Relief.PanelOut => PanelOut, _ => PanelIn };
        // the relief is two pixels at most: what is drawn inside keeps clear of it
        var room = new Thickness(e.Relief == Relief.FieldIn ? 2 : 1);
        switch (box)
        {
            case Border b:
                b.BorderThickness = _on ? default : e.Outline;
                b.BoxShadow = _on ? shadows : default;
                break;
            case TemplatedControl t:
                t.BorderThickness = _on ? default : e.Outline;
                // the template's own border: a ListBox's or an ItemsControl's is its root
                if (t.GetVisualChildren().FirstOrDefault() is Border inner)
                {
                    inner.BoxShadow = _on ? shadows : default;
                    if (_on) inner.Padding = room; else inner.ClearValue(Decorator.PaddingProperty);
                }
                break;
        }
    }

    /// <summary>
    /// The buttons' restyle, for the application's styles: every Button but the town's pictures
    /// (Button.pic, whose picture is its face) and the toolbar's icons (Button.tool, drawn with their
    /// own relief), under a window carrying <see cref="ClassName"/>. The caption keeps its name,
    /// PART_ContentPresenter, so the windows' own styles for it still find it.
    /// </summary>
    public static Styles Styles { get; } = Build();

    // ".classic Button:not(.pic):not(.tool)", and that with a pseudo-class
    private static Selector Buttons(Selector? x, string? pseudo = null)
    {
        var s = x.Class(ClassName).Descendant().OfType<Button>().Not(y => y.Class("pic")).Not(y => y.Class("tool"));
        return pseudo == null ? s : s.Class(pseudo);
    }

    // ".classic TabItem /template/ Border#PART_LayoutRoot", and that with a pseudo-class
    private static Selector Tab(Selector? x, string? pseudo)
    {
        var s = x.Class(ClassName).Descendant().OfType<TabItem>();
        return (pseudo == null ? s : s.Class(pseudo)).Template().OfType<Border>().Name("PART_LayoutRoot");
    }

    private static Selector Caption(Selector? x, string? pseudo) =>
        Buttons(x, pseudo).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");

    // The theme's own button template is kept, and only its caption's colours and lines are set:
    // putting a template of the port's own on and taking it off again with the theme fails, as a
    // button whose content is a control finds that control still in the old template's presenter
    // ("already has a visual parent"). The black line is drawn as inset lines too, not as the
    // caption's border, which would smear the lines inside it (see the class's summary).
    private static Styles Build()
    {
        var face = Palette.Button;
        Setter[] Caption3D(BoxShadows lines) =>
        [
            new Setter(ContentPresenter.BackgroundProperty, face),
            new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(0)),
            new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(0)),
            new Setter(ContentPresenter.BoxShadowProperty, lines),
        ];
        Style On(Func<Selector?, Selector> selector, params Setter[] setters)
        {
            var style = new Style(selector);
            foreach (var s in setters) style.Setters.Add(s);
            return style;
        }
        return new Styles
        {
            // selected text as Windows 3.1 draws it: white on navy. The theme's own highlight is the
            // system's accent, which left black text on a dark blue (the Load Character box)
            On(x => x.Class(ClassName).Descendant().OfType<TextBox>(),
               new Setter(TextBox.SelectionBrushProperty, Palette.Selection)),
            // a list's chosen row as Windows 3.1 marks it: white on navy, hovered or not (the theme's
            // was a pale blue under black; the original's Messages and Party Options show the navy)
            On(x => x.Class(ClassName).Descendant().OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
               new Setter(ContentPresenter.BackgroundProperty, Palette.Selection),
               new Setter(ContentPresenter.ForegroundProperty, Palette.SelectionText)),
            // and its text white over anything but a colour the row set as its own (a Messages line's
            // colour is set at a style's priority, MessageView, so it gives way here)
            On(x => x.Class(ClassName).Descendant().OfType<ListBoxItem>().Class(":selected").Descendant().OfType<TextBlock>(),
               new Setter(TextBlock.ForegroundProperty, Palette.SelectionText)),
            // and white in either theme: with none of its own, selected text kept the box's black ink
            // on the theme's blue highlight (2.9:1 in Dark, worse on Light's darker accent)
            On(x => x.OfType<TextBox>(), new Setter(TextBox.SelectionForegroundBrushProperty, Palette.SelectionText)),
            // a caption's access letter underlined all the time, as Windows 3.1 draws "&Save Character",
            // not only while Alt is held (the theme's way)
            On(x => x.Class(ClassName).Descendant().OfType<AccessText>(), new Setter(AccessText.ShowAccessKeyProperty, true)),
            // an index tab (IndexTab): raised, light along its top and left and shadow down its right;
            // the tabs not chosen are shut off from the page by a shadow line along their foot, and the
            // chosen one opens into it (the character window in the game, as the player sees it)
            On(x => Tab(x, null), new Setter(Border.BorderThicknessProperty, new Thickness(0)),
               new Setter(Border.BackgroundProperty, face), new Setter(Border.BoxShadowProperty, TabUp)),
            On(x => Tab(x, ":pointerover"), new Setter(Border.BackgroundProperty, face)),
            On(x => Tab(x, ":selected"), new Setter(Border.BackgroundProperty, face), new Setter(Border.BoxShadowProperty, TabOpen)),
            On(x => Buttons(x),
               new Setter(TemplatedControl.BackgroundProperty, face),
               new Setter(TemplatedControl.ForegroundProperty, Palette.Text),
               new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0))),
            On(x => Caption(x, null), Caption3D(ButtonUp)),
            // no hover colour: a Windows 3.1 button does not light up under the pointer
            On(x => Caption(x, ":pointerover"), [.. Caption3D(ButtonUp), new Setter(ContentPresenter.ForegroundProperty, Palette.Text)]),
            On(x => Caption(x, ":pressed"), [.. Caption3D(ButtonDown), new Setter(ContentPresenter.ForegroundProperty, Palette.Text)]),
            On(x => Caption(x, ":disabled"), [.. Caption3D(ButtonUp), new Setter(ContentPresenter.ForegroundProperty, Palette.Shadow)]),
        };
    }
}
