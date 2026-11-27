using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mordor.App;

/// <summary>
/// The symbols the port captions a few small buttons with: the dungeon's arrow pad (← ↑ → ↓), an MDI
/// child's title-bar buttons (✕, □, ❐, ▴) and the Mods window's remove button (✕).
///
/// On Windows each is the character, drawn by the button's own face as it always was. Away from
/// Windows the faces cannot be relied on for them: the bundled Liberation Sans has the arrows, but at
/// the arrow pad's size Linux drew ← and → as a short dash with no head to speak of (the CI pictures),
/// and it has no ✕, ❐ or ▴ at all, which leaves those to whatever the system happens to have. So
/// there each is drawn as a small vector shape instead, in the button's own text colour, and looks
/// the same on every system. <see cref="Override"/> lets a check draw them on Windows too.
/// </summary>
public static class Glyphs
{
    /// <summary>For a check: true draws the shapes on Windows too, false draws the characters everywhere; null is the default.</summary>
    public static bool? Override { get; set; }

    /// <summary>Whether the shapes stand in for the characters here.</summary>
    public static bool Drawn => Override ?? !OperatingSystem.IsWindows();

    /// <summary>A shape: its box, its path data, and whether it is filled rather than stroked.</summary>
    private sealed record Figure(double Width, double Height, string Data, bool Filled = false);

    // Strokes are one pixel wide on the half pixel, so they land on whole pixels at the form's own size.
    private static readonly Dictionary<string, Figure> Shapes = new(StringComparer.Ordinal)
    {
        ["←"] = new(12, 7, "M0.5,3.5 H11.5 M3.5,0.5 L0.5,3.5 L3.5,6.5"),
        ["→"] = new(12, 7, "M0.5,3.5 H11.5 M8.5,0.5 L11.5,3.5 L8.5,6.5"),
        ["↑"] = new(7, 12, "M3.5,0.5 V11.5 M0.5,3.5 L3.5,0.5 L6.5,3.5"),
        ["↓"] = new(7, 12, "M3.5,0.5 V11.5 M0.5,8.5 L3.5,11.5 L6.5,8.5"),
        ["✕"] = new(8, 8, "M0.5,0.5 L7.5,7.5 M7.5,0.5 L0.5,7.5"),
        ["□"] = new(8, 8, "M0.5,0.5 H7.5 V7.5 H0.5 Z"),
        ["❐"] = new(9, 9, "M2.5,2.5 V0.5 H8.5 V6.5 H6.5 M0.5,2.5 H6.5 V8.5 H0.5 Z"),
        ["▴"] = new(7, 4, "M0,4 L3.5,0 L7,4 Z", Filled: true),
    };

    /// <summary>Whether <paramref name="symbol"/> has a shape here.</summary>
    public static bool Has(string symbol) => Shapes.ContainsKey(symbol);

    /// <summary>
    /// A button's content for <paramref name="symbol"/>: the character itself on Windows (or for
    /// anything without a shape), else its shape.
    /// </summary>
    public static object Content(string symbol) =>
        Drawn && Shapes.TryGetValue(symbol, out var s) ? Draw(symbol, s) : symbol;

    private static Path Draw(string symbol, Figure s)
    {
        var path = new Path
        {
            Data = Geometry.Parse(s.Data),
            Width = s.Width,
            Height = s.Height,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            StrokeThickness = 1,
            Tag = symbol,
        };
        // the button's text colour, as its presenter has it (a theme's hover, pressed and disabled
        // colours included): the inherited foreground the character would have been drawn in
        if (s.Filled) path[!Shape.FillProperty] = path[!TextElement.ForegroundProperty];
        else path[!Shape.StrokeProperty] = path[!TextElement.ForegroundProperty];
        return path;
    }
}
