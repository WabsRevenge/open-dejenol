using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Mordor.App;

/// <summary>
/// A TabControl drawn as the original's tab controls are: VideoSoft's IndexTab (VSVBX), which
/// frmCharStat3 has as tabItemTab and frmCharStat1 as tabStatTab. Their form data in MORDOR.EXE
/// (RCDATA 10 and 8) gives each the form's MS Sans Serif 8.25, a row of equal tabs across the
/// control's whole width - property 2C is 3 on tabItemTab's three tabs and 4 on tabStatTab's seven,
/// which wrap onto a second row - and the page below the tabs: frmCharStat3's page Elastic starts
/// 360 twips down the control (24 pixels), frmCharStat1's picTabPicture 510 (34 pixels) below two
/// rows. game_files/bm14.png shows the strip so: tabs about 20 pixels tall, the current one bold.
///
/// The theme's tabs were 48 pixels tall with a wide underline, in the theme's 12-pixel face, and
/// in a wrapping panel; the page started some 90 pixels below a window's title bar.
/// </summary>
public static class IndexTab
{
    /// <param name="tabs">The control, with its items already in it.</param>
    /// <param name="perRow">Tabs to a row (the IndexTab's property 2C).</param>
    /// <param name="strip">The height of the tab rows together, in the form's pixels: the page's top
    /// in the control, less the two pixels of the page's own edge.</param>
    /// <param name="form">The form it is on, and <paramref name="control"/> its name there: the
    /// captions are its design font (MS Sans Serif 8.25) through the Options fonts (UiFonts), and a
    /// row of tabs is never shorter than that font's line.</param>
    public static void Apply(TabControl tabs, int perRow, double strip, Mordor.Core.DesignFonts.Form form, string control)
    {
        int rows = Math.Max(1, (tabs.ItemCount + perRow - 1) / perRow);
        var font = UiFonts.Design(form, control);
        double rowHeight = Math.Max(strip / rows, UiFonts.LineHeight(form, font with { Bold = true }) + 2);
        tabs.ItemsPanel = new FuncTemplate<Panel?>(() => new IndexTabRows { Columns = perRow });
        // the theme reads these sizes as resources: the nearest one wins
        tabs.Resources["TabItemMinHeight"] = 0d;
        tabs.Resources["TabItemHeaderFontSize"] = UiFonts.SizeOf(form, font);
        tabs.Resources["TabItemHeaderMargin"] = new Thickness(0);
        tabs.Resources["TabItemHeaderSelectedPipeFill"] = Brushes.Transparent;
        tabs.Resources["TabItemPipeThickness"] = 0d;   // a Double in the Fluent theme (the pipe's height)
        tabs.Styles.Add(new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(Layoutable.HeightProperty, rowHeight),
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1, 1, 1, 0)),
                new Setter(TemplatedControl.BorderBrushProperty, FixedColour.TabEdge),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            },
        });
        // the caption's face on the caption alone: a page takes its font from its TabItem, and the
        // character window's pages keep their own
        tabs.Styles.Add(new Style(x => x.OfType<TabItem>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(ContentPresenter.FontSizeProperty, UiFonts.SizeOf(form, font)),
                new Setter(ContentPresenter.FontFamilyProperty, UiFonts.FamilyOf(form, font)),
                new Setter(ContentPresenter.FontWeightProperty, FontWeight.Normal),
            },
        });
        // the current tab bold, as bm14.png shows "Items"
        tabs.Styles.Add(new Style(x => x.OfType<TabItem>().Class(":selected").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.FontWeightProperty, FontWeight.Bold) },
        });
        tabs.Styles.Add(new Style(x => x.OfType<TabItem>().Template().OfType<Border>().Name("PART_SelectedPipe"))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, false) },
        });
    }
}

/// <summary>
/// The IndexTab's rows of tabs, <see cref="Columns"/> to a row, each tab a column wide (a short last
/// row leaves the rest of its width empty). The row holding the selected tab is drawn last, next to
/// the page, and the others above it in turn after it: the original's tab moves its current row
/// down so (bm13.png: Stats chosen, the rows read Look/Buffers/Misc over Stats/Resist./Char/Guild,
/// and the game in play shows the same; CharacterPanel's summary has the order of the tabs).
/// </summary>
public sealed class IndexTabRows : Panel
{
    public int Columns { get; set; } = 1;

    static IndexTabRows() => AffectsParentArrange<IndexTabRows>(TabItem.IsSelectedProperty);

    private int Rows => Math.Max(1, (Children.Count + Columns - 1) / Columns);

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 0, h = 0;
        var cell = new Size(availableSize.Width / Columns, availableSize.Height / Rows);
        foreach (var c in Children)
        {
            c.Measure(cell);
            w = Math.Max(w, c.DesiredSize.Width);
            h = Math.Max(h, c.DesiredSize.Height);
        }
        return new Size(w * Columns, h * Rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int rows = Rows;
        double w = finalSize.Width / Columns, h = finalSize.Height / rows;
        int selectedRow = 0;
        for (int i = 0; i < Children.Count; i++)
            if (Children[i] is TabItem { IsSelected: true }) selectedRow = i / Columns;
        for (int i = 0; i < Children.Count; i++)
        {
            int row = i / Columns;
            // where the row is drawn, top to bottom: the rows after the selected one, wrapping round,
            // and the selected one last
            int place = (row - selectedRow - 1 + rows) % rows;
            Children[i].Arrange(new Rect(w * (i % Columns), h * place, w, h));
        }
        return finalSize;
    }
}
