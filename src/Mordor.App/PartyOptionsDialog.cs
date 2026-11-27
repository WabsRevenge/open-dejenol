using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Mordor.Core;

namespace Mordor.App;

/// <summary>
/// <c>frmPartyOptions</c>, from the help's screenshot of it (game_files/bm6.png, 230x151):
/// the party down the left, a "Defaults" frame down the right holding four radio buttons -
/// Nothing, Fight, Defend, Buffer - a buffer number under them, and one OK button.
///
/// The Party Information window's Options button shows it modally, "frmPartyOptions.Show
/// mc003C", whenever there are no monsters (FRMGROUP.BAS:37-42) - which is in the dungeon and in
/// the city alike, so both windows open this one copy.
///
/// The captions are the screenshot's. <c>gv058A$</c> (MODULE1.BAS:419-423) holds "None",
/// "Fight", "Defend", "Spell" and this port cited it for these buttons, but that table is not
/// what the form is captioned with: the picture plainly reads Nothing and Buffer.
///
/// It used to be a combo box per member with a paragraph of this port's own explaining what
/// combat settings are. The help says only "This is the default setting for the selected
/// character when combat starts. None, Fight, Defend, or a spell buffer can be selected."
/// </summary>
internal static class PartyOptionsDialog
{
    public static void Show(Window owner, Party party, Action closed)
    {
        var characterPage = new FormPage(1650, 1800, new FormRect(0, 0, 1650, 1800));
        var who = FormPage.List(characterPage.Root, new FormRect(0, 0, 1650, 1800));
        who.Foreground = Palette.Text;
        who.ItemsSource = party.Members.Select(m => m.Name.TrimEnd()).ToList();
        who.SelectedIndex = party.SelectedIndex;

        // a row each, as tight as bm6.png's: the theme's radio buttons are 32 pixels tall with room around them
        RadioButton Option(string text) => new()
        {
            Content = text,
            GroupName = "defaults",
            Foreground = Palette.Text,
            MinHeight = 0,
            Padding = new Thickness(4, 0, 0, 0),
        };
        var nothing = Option("Nothing");
        var fight = Option("Fight");
        var defend = Option("Defend");
        var buffer = Option("Buffer");

        // cmbBuffer, filled "For l003C = 0 To 9: cmbBuffer.AddItem CStr(l003C + 1)"
        // (FRMPINFO.BAS:35-37) - so it reads 1 to 10. The port had written 1..9 then 0 on the
        // grounds that Shift+0 fires the tenth, which is true of the key and not of this list.
        var whichBuffer = new ComboBox
        {
            Width = 70,
            Background = Palette.List,
            Foreground = Palette.Text,
            ItemsSource = Enumerable.Range(1, SpellBuffers.Count).Select(i => i.ToString()).ToList(),
        };

        bool loading = false;
        Character Current() => party.Members[Math.Clamp(who.SelectedIndex, 0, party.Size - 1)];

        void Load()
        {
            loading = true;
            int s = Current().CombatSetting;
            nothing.IsChecked = s == 0;
            fight.IsChecked = s == 1;
            defend.IsChecked = s == 2;
            buffer.IsChecked = s >= 3;
            whichBuffer.SelectedIndex = s >= 3 ? Math.Clamp(s - 3, 0, SpellBuffers.Count - 1) : 0;
            whichBuffer.IsEnabled = s >= 3;
            loading = false;
        }

        void Store()
        {
            if (loading) return;
            var c = Current();
            c.CombatSetting = fight.IsChecked == true ? 1
                            : defend.IsChecked == true ? 2
                            : buffer.IsChecked == true ? 3 + Math.Max(0, whichBuffer.SelectedIndex)
                            : 0;
            whichBuffer.IsEnabled = buffer.IsChecked == true;
        }

        who.SelectionChanged += (_, _) => Load();
        foreach (var r in new[] { nothing, fight, defend, buffer }) r.IsCheckedChanged += (_, _) => Store();
        whichBuffer.SelectionChanged += (_, _) => Store();
        Load();

        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            Width = 96,
            Background = Palette.Window,
            Foreground = Palette.Text,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };

        var defaults = ClassicLook.Sunken(new Border
        {
            Background = Palette.Window,
            BorderBrush = Palette.Shadow,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 6),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    // the frame's caption, black and bold as bm6.png has it (it had been the form's blue)
                    new TextBlock { Text = "Defaults", Foreground = Palette.Text, FontWeight = FontWeight.Bold },
                    nothing, fight, defend, buffer, whichBuffer,
                },
            },
        });
        ok.Width = double.NaN;
        ok.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;

        // frmPartyOptions' own font, MS Sans Serif 8.25 (lstCharacters), which its Form_Load puts
        // through sub0784 (FRMPINFO.BAS): the Options fonts, UiFonts. It had been the frame's Cascadia Mono.
        // bm6.png's arrangement: the list with OK under it on the left, the Defaults frame the whole
        // height on the right, the window no bigger than they are
        var win = UiFonts.Set(new Window
        {
            Title = "Party Options",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            Background = Palette.Window,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Background = Palette.Window,
                Padding = new Thickness(6),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,6,Auto"),
                    Children =
                    {
                        new StackPanel { Spacing = 6, Children = { characterPage.Page, ok } },
                        new ContentControl { Content = defaults, [Grid.ColumnProperty] = 2 },
                    },
                },
            },
        }, DesignFonts.PartyOptions, "lstCharacters");
        ok.Click += (_, _) => win.Close();
        win.KeyDown += (_, ke) => { if (ke.Key == Key.Escape) win.Close(); };
        win.Closed += (_, _) => closed();
        win.ShowDialog(owner);
    }
}

/// <summary>
/// The Party Information rows' colours, as frmParty paints them, carried onto the port's dark
/// background the way the rebuilt Store does it - the original's black list text reads as
/// WhiteSmoke, its red as IndianRed, its blue as CornflowerBlue.
/// </summary>
internal static class PartyRowColours
{
    /// <summary>QBColor(0): every Name and Hits label before the switch colours one (MULTIMOD.BAS:613-616).</summary>
    public static IBrush Plain => Palette.Text;
    /// <summary>QBColor(mc0116 = 2): the selected member's Name and Hits (MULTIMOD.BAS:640-641).</summary>
    public static IBrush Selected => Palette.Green;
    /// <summary>QBColor(mc014C = 1): lblStatus reading "OK" (FRMGROUP.BAS:446-448).</summary>
    public static IBrush Well => Palette.Navy;
    /// <summary>QBColor(mc014E = 4): lblStatus with Po/Di/Pa in it (FRMGROUP.BAS:449-450).</summary>
    public static IBrush Ailing => Palette.Maroon;
    /// <summary>lblSpells and lblOption are never recoloured in code; they are blue in game_files/bm5.png.</summary>
    public static IBrush Fixed => Palette.Navy;

    public static IBrush NameAndHits(bool selected) => selected ? Selected : Plain;
    /// <summary>Only lblName is made bold, "frmParty!lblName(p00FA).FontBold = True" (MULTIMOD.BAS:642).</summary>
    public static FontWeight NameWeight(bool selected) => selected ? FontWeight.Bold : FontWeight.Normal;
    public static IBrush Status(bool well) => well ? Well : Ailing;
}
