using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Mordor.App;

/// <summary>
/// A caption as VB draws one with an "&amp;" in it: one line of text, the letter after the "&amp;"
/// underlined, there all the time as on Windows 3.1 and not only while Alt is held. Drawn as a
/// single run of text, so the letters keep their spacing ("Exit" had read "Ex it" when the
/// underlined letter was a piece of its own), and with no inlines: the town's captions, drawn with
/// a TextBlock's inlines, came back empty after a trip below. The font and the ink are the ones the
/// caption inherits, as a TextBlock's would be.
/// </summary>
public sealed class AccessCaption : Control
{
    /// <summary>The caption without its "&amp;".</summary>
    public string Text { get; }
    /// <summary>Where the underlined letter is in <see cref="Text"/>, or -1.</summary>
    public int AccessIndex { get; }

    public AccessCaption(string withAmpersand)
    {
        int at = withAmpersand.IndexOf('&');
        AccessIndex = at >= 0 && at < withAmpersand.Length - 1 ? at : -1;
        Text = withAmpersand.Remove(at >= 0 ? at : 0, at >= 0 ? 1 : 0);
    }

    static AccessCaption()
    {
        AffectsMeasure<AccessCaption>(TextElement.FontFamilyProperty, TextElement.FontSizeProperty,
                                      TextElement.FontWeightProperty, TextElement.FontStyleProperty);
        AffectsRender<AccessCaption>(TextElement.ForegroundProperty);
    }

    private FormattedText Layout()
    {
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(GetValue(TextElement.FontFamilyProperty), GetValue(TextElement.FontStyleProperty), GetValue(TextElement.FontWeightProperty)),
            GetValue(TextElement.FontSizeProperty), GetValue(TextElement.ForegroundProperty));
        if (AccessIndex >= 0) text.SetTextDecorations(TextDecorations.Underline, AccessIndex, 1);
        return text;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Layout();
        return new Size(text.WidthIncludingTrailingWhitespace, text.Height);
    }

    public override void Render(DrawingContext context) => context.DrawText(Layout(), new Point(0, 0));
}
