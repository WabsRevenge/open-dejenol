using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using Mordor.Modding;

namespace Mordor.App;

/// <summary>
/// The game's colours: a short list of roles, each with one value in the Light theme and one in the
/// Dark, and nowhere else. A window asks for a role - the list's ground, a heading's ink - never for a
/// colour, and a change of theme recolours every role's one brush in place, so whatever holds it
/// follows. The mods take the same brushes by name from the application's resources (ThemeKeys).
///
/// Light is the original's: Windows 3.1's grey form (&amp;HC0C0C0), its pale yellow lists and boxes
/// (the Party window's rows, picTrap), black ink, and the QBColor navy, blue, maroon and green the
/// forms and the code use (QBColor 1, 9, 4, 2). Dark is the port's own, kept as it was where two
/// names had two near-identical colours for one job.
///
/// A colour that is not a role - a picture's, a bar's own fill, a mod's own choice - is drawn as it
/// is. The UI checks hold the window code to this (ContrastCheck for what can be read,
/// ThemeRegressionCheck for the roles the original has).
/// </summary>
public static class Palette
{
    private sealed record Role(string Key, SolidColorBrush Brush, Color Light, Color Dark);

    private static readonly List<Role> Roles = new();

    private static SolidColorBrush Add(string key, string light, string dark)
    {
        var role = new Role(key, new SolidColorBrush(Color.Parse(dark)), Color.Parse(light), Color.Parse(dark));
        Roles.Add(role);
        return role.Brush;
    }

    // ---- grounds ----
    /// <summary>A window's own ground: the form's grey.</summary>
    public static SolidColorBrush Window { get; } = Add(ThemeKeys.Window, "#C0C0C0", "#101018");
    /// <summary>The ground inside a window's frame where its content sits: Messages, the Automap's map, the Help's list.</summary>
    public static SolidColorBrush Well { get; } = Add(ThemeKeys.Well, "#C0C0C0", "#0a0a10");
    /// <summary>A list's or an input's ground, and the toolbar's: the Party window's rows, picTrap, picToolbar.</summary>
    public static SolidColorBrush List { get; } = Add(ThemeKeys.List, "#FFFFE0", "#0a0a10");
    /// <summary>A field that only shows something (a label with BorderStyle 1).</summary>
    public static SolidColorBrush Field { get; } = Add(ThemeKeys.Field, "#FFFFFF", "#33333a");
    /// <summary>A button's face.</summary>
    public static SolidColorBrush Button { get; } = Add(ThemeKeys.Button, "#C0C0C0", "#3b3b40");
    /// <summary>A heading band: the Help's.</summary>
    public static SolidColorBrush Banner { get; } = Add(ThemeKeys.Banner, "#C0C0C0", "#1c2740");

    // ---- lines ----
    /// <summary>The light edge of a bevel (ClassicLook).</summary>
    public static SolidColorBrush Highlight { get; } = Add(ThemeKeys.Highlight, "#FFFFFF", "#3a3a4c");
    /// <summary>The dark edge of a bevel, and a plain frame's line.</summary>
    public static SolidColorBrush Shadow { get; } = Add(ThemeKeys.Shadow, "#808080", "#555566");
    /// <summary>A button's black line (ClassicLook).</summary>
    public static SolidColorBrush Outline { get; } = Add(ThemeKeys.Outline, "#000000", "#000000");

    // ---- inks ----
    /// <summary>Ordinary text.</summary>
    public static SolidColorBrush Text { get; } = Add(ThemeKeys.Text, "#000000", "#d8d8e0");
    /// <summary>Text that says less: a hint, a thing out of reach.</summary>
    public static SolidColorBrush Muted { get; } = Add(ThemeKeys.Muted, "#505050", "#808080");
    /// <summary>A table's column captions: the Party window's, black in the original.</summary>
    public static SolidColorBrush Caption { get; } = Add(ThemeKeys.Caption, "#000000", "#888888");
    /// <summary>QBColor 1, the form's navy: headings and labels.</summary>
    public static SolidColorBrush Navy { get; } = Add(ThemeKeys.Navy, "#000080", "#8aa8e8");
    /// <summary>QBColor 9, the form's blue (lblWelcome's &amp;HFF0000).</summary>
    public static SolidColorBrush Blue { get; } = Add(ThemeKeys.Blue, "#0000FF", "#6495ed");
    /// <summary>QBColor 4, the form's maroon: a sprung trap, an ailing member, a foe's blow.</summary>
    public static SolidColorBrush Maroon { get; } = Add(ThemeKeys.Maroon, "#800000", "#cd5c5c");
    /// <summary>QBColor 2, the form's green: the chosen member, a trap disarmed, the party's doing.</summary>
    public static SolidColorBrush Green { get; } = Add(ThemeKeys.Green, "#008000", "#90ee90");
    /// <summary>A warning, and a chest's news.</summary>
    public static SolidColorBrush Warning { get; } = Add(ThemeKeys.Warning, "#8B4500", "#e0a050");
    /// <summary>A name picked out: a monster's, a companion's, the town's notice.</summary>
    public static SolidColorBrush Name { get; } = Add(ThemeKeys.Name, "#800000", "#F0E68C");
    /// <summary>Something out of the ordinary: a join.</summary>
    public static SolidColorBrush Magic { get; } = Add(ThemeKeys.Magic, "#800080", "#dda0dd");

    // ---- selection ----
    /// <summary>A chosen row's ground: Windows 3.1's navy in Light.</summary>
    public static SolidColorBrush Selection { get; } = Add(ThemeKeys.Selection, "#000080", "#2F5597");
    /// <summary>A chosen row's ink.</summary>
    public static SolidColorBrush SelectionText { get; } = Add(ThemeKeys.SelectionText, "#FFFFFF", "#FFFFFF");

    /// <summary>A role's colour in the Light theme, for what is drawn in Light alone (ClassicLook's relief).</summary>
    public static Color LightOf(SolidColorBrush role) => Roles.Find(r => r.Brush == role)!.Light;

    /// <summary>The brush of a role by its ThemeKeys name, or null.</summary>
    public static SolidColorBrush? ByKey(string key) => Roles.Find(r => r.Key == key)?.Brush;

    /// <summary>Every role's brush in the Light or the Dark theme's colour.</summary>
    public static void Paint(bool light)
    {
        foreach (var r in Roles) r.Brush.Color = light ? r.Light : r.Dark;
    }

    /// <summary>Every role in the application's resources under its ThemeKeys name, for the mods.</summary>
    public static void Publish(IResourceDictionary resources)
    {
        foreach (var r in Roles) resources[r.Key] = r.Brush;
    }
}

/// <summary>
/// The colours that are not the theme's: the same in Light and Dark, each named for what it is.
/// Windows 3.1's own on the forms drawn as the original's whatever the theme (the About box, the Hall,
/// the exchange, the Confinement's list), the dungeon view's black, the inner windows' title bars, the
/// refusals' red, the developers' map window. A colour here is one the code draws on purpose in one
/// shade; a colour that should follow the theme belongs in <see cref="Palette"/>'s roles instead.
/// </summary>
public static class FixedColour
{
    private static SolidColorBrush Of(string hex) => new(Color.Parse(hex));
    private static SolidColorBrush Of(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));

    // ---- Windows 3.1's, on the forms drawn as the original's ----
    public static SolidColorBrush Black { get; } = Of("#000000");
    public static SolidColorBrush White { get; } = Of("#FFFFFF");
    public static SolidColorBrush Win31Grey { get; } = Of("#C0C0C0");
    public static SolidColorBrush Win31Shadow { get; } = Of("#808080");
    public static SolidColorBrush Win31ButtonFace { get; } = Of("#F0F0F0");
    public static SolidColorBrush Win31Hover { get; } = Of("#D0D0D0");
    public static SolidColorBrush PaleYellow { get; } = Of("#FFFFE0");
    /// <summary>QBColor 1.</summary>
    public static SolidColorBrush Win31Navy { get; } = Of("#000080");
    /// <summary>QBColor 9.</summary>
    public static SolidColorBrush Win31Blue { get; } = Of("#0000FF");
    /// <summary>QBColor 2.</summary>
    public static SolidColorBrush Win31Green { get; } = Of("#008000");
    /// <summary>QBColor 4.</summary>
    public static SolidColorBrush Win31Maroon { get; } = Of("#800000");
    /// <summary>The Credits' "ForeColor = 9" taken as a colour, &amp;H9: as near black as makes no difference.</summary>
    public static SolidColorBrush CreditsForeColor9 { get; } = Of("#090000");
    /// <summary>The tan-yellow of a field you type into (the help's "tan-yellow fields take input").</summary>
    public static SolidColorBrush InputField { get; } = Of("#d8c88a");

    // ---- the port's own, the same in either theme ----
    /// <summary>The dungeon view's ground behind the drawing.</summary>
    public static SolidColorBrush ViewBlack { get; } = Of("#000000");
    /// <summary>The view's frame (shpMazeBox) in Dark.</summary>
    public static SolidColorBrush ViewFrame { get; } = Of("#333333");
    /// <summary>lblDead's band across the view, and its pale-red word.</summary>
    public static SolidColorBrush DeathBand { get; } = Of("#808080");
    public static SolidColorBrush DeathWord { get; } = Of("#fff0f0");
    /// <summary>The character sheet's states line and the Objects window's carry line.</summary>
    public static SolidColorBrush StatesInk { get; } = Of("#8fd9d9");
    /// <summary>A refusal said in a page (the Morgue's, the Seer's, the Store's): indian red.</summary>
    public static SolidColorBrush Refusal { get; } = Of("#CD5C5C");
    /// <summary>The Options window's found and not-found notes.</summary>
    public static SolidColorBrush Found { get; } = Of("#6f8fff");
    public static SolidColorBrush NotFound { get; } = Of("#e04040");
    /// <summary>The import window's headings and its warning.</summary>
    public static SolidColorBrush ImportHeading { get; } = Of("#8fb0ff");
    public static SolidColorBrush ImportWarning { get; } = Of("#e0b040");
    /// <summary>A help topic's jump, underlined, in the help's green.</summary>
    public static SolidColorBrush HelpJump { get; } = Of(255, 0, 128, 0);
    /// <summary>The character-creation window's frames and its green Save button.</summary>
    public static SolidColorBrush CreationFrame { get; } = Of("#555555");
    public static SolidColorBrush GoButton { get; } = Of("#2d6a2d");
    /// <summary>A frame's line, and a heading, in the port's own boxes (Control Panel, its groups).</summary>
    public static SolidColorBrush BoxLine { get; } = Of("#3a3a4e");
    public static SolidColorBrush BoxHeading { get; } = Of("#8fb0ff");
    /// <summary>An index tab's edge before the relief takes it.</summary>
    public static SolidColorBrush TabEdge { get; } = Of(255, 0x55, 0x55, 0x66);
    /// <summary>The toolbar's foot line and its icons' ground.</summary>
    public static SolidColorBrush ToolbarLine { get; } = Of("#2a2a38");
    /// <summary>A name picker's frame.</summary>
    public static SolidColorBrush PickerLine { get; } = Of("#808080");

    // ---- the inner windows' frames (Mdi) ----
    public static SolidColorBrush ChildFrame { get; } = Of("#3a3a4e");
    public static SolidColorBrush ChildTitle { get; } = Of("#2b2b52");
    public static SolidColorBrush ChildTitleInk { get; } = Of("#FFFFFF");
    public static SolidColorBrush ChildGrip { get; } = Of("#55556e");
    public static SolidColorBrush ChildButton { get; } = Of("#3c3c66");

    // ---- the Automap ----
    /// <summary>The shade over the squares the map does not show.</summary>
    public static SolidColorBrush MapShade { get; } = Of(170, 0, 0, 0);
    /// <summary>A colour swatch's edge (the legend's).</summary>
    public static SolidColorBrush SwatchEdge { get; } = Of(255, 0x20, 0x20, 0x20);

    // ---- the developers' map window and the MDI demo (MORDOR_TEST) ----
    public static SolidColorBrush DevBar { get; } = Of("#1e1e28");
    public static SolidColorBrush DevStatus { get; } = Of("#8fd98f");
    public static SolidColorBrush DevInfo { get; } = Of("#c8c8d8");
    public static SolidColorBrush DevGround { get; } = Of("#101018");
    public static SolidColorBrush DevWall { get; } = Of(255, 0xE0, 0xE0, 0xE8);
    public static SolidColorBrush DevDoor { get; } = Of(255, 0xC8, 0x90, 0x40);
    public static SolidColorBrush DevSecret { get; } = Of(255, 0x80, 0x50, 0xC0);
    public static SolidColorBrush DevRock { get; } = Of(255, 0x33, 0x33, 0x3b);
    public static SolidColorBrush DevWater { get; } = Of(255, 0x1e, 0x3a, 0x6a);
    public static SolidColorBrush DevQuicksand { get; } = Of(255, 0x5a, 0x4a, 0x20);
    public static SolidColorBrush DevFog { get; } = Of(255, 0x40, 0x40, 0x48);
    public static SolidColorBrush DevAntimagic { get; } = Of(255, 0x50, 0x20, 0x40);
    public static SolidColorBrush DevGrid { get; } = Of("#808080");
    public static SolidColorBrush DemoInk { get; } = Of("#DCDCDC");
    public static SolidColorBrush DemoGround { get; } = Of("#14141c");
}
