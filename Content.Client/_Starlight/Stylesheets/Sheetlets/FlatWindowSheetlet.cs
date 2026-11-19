using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Stylesheets;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._Starlight.Stylesheets.Sheetlets;

[StarlightSheetlet]
public sealed class FlatWindowSheetlet : Sheetlet<NanotrasenStylesheet>
{
    public override StyleRule[] GetRules(NanotrasenStylesheet sheet, object config)
    {
        if (sheet.Theme is not { } theme)
            return Array.Empty<StyleRule>();

        var proto = theme.Proto;

        var windowBox = ThemeBox.Or(proto.Window, ResCache,
            FlatBox.Bordered(proto.Background, proto.Border, proto.BorderThickness));
        var headerBox = ThemeBox.Or(proto.Header, ResCache,
            FlatBox.Underlined(proto.Surface, proto.Border, 1).WithContentMargin(StyleBox.Margin.Bottom, 0));
        var alertHeaderBox = FlatBox.Underlined(theme.Negative.Background, theme.Negative.Base, 1)
            .WithContentMargin(StyleBox.Margin.Bottom, 0);

        return new StyleRule[]
        {
            E().Class(DefaultWindow.StyleClassWindowPanel).Panel(windowBox),
            E().Class(StyleClass.BorderedWindowPanel).Panel(windowBox),
            E().Class(DefaultWindow.StyleClassWindowHeader).Panel(headerBox),
            E().Class(StyleClass.AlertWindowHeader).Panel(alertHeaderBox),
            E<PanelContainer>().Class("WindowHeadingBackground").Panel(FlatBox.Fill(proto.Surface)),

            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoNormal().Modulate(proto.TextMuted),
            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoHovered().Modulate(theme.Negative.Text),
            E<TextureButton>().Class(DefaultWindow.StyleClassWindowCloseButton).PseudoPressed().Modulate(theme.Negative.Text.WithAlpha(0.7f)),

            E<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).PseudoNormal().Modulate(proto.TextMuted),
            E<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).PseudoHovered().Modulate(proto.Text),
            E<TextureButton>().Class(FancyWindow.StyleClassWindowHelpButton).PseudoPressed().Modulate(proto.Text.WithAlpha(0.7f)),

            E<Label>().Class("WindowFooterText").FontColor(proto.TextMuted),

            Panel(StyleClass.BackgroundPanel, BackgroundBox(proto.Background, proto.Border)),
            Panel(StyleClass.PanelDark, BackgroundBox(proto.Surface, proto.Border)),
            Panel("BackgroundPanelDark", BackgroundBox(proto.Surface, proto.Border)),
            Panel(StyleClass.BackgroundPanelOpenLeft, BackgroundBox(proto.Background, proto.Border)
                .WithContentMargin(StyleBox.Margin.Left, 8)),
            Panel(StyleClass.BackgroundPanelOpenRight, BackgroundBox(proto.Background, proto.Border)
                .WithContentMargin(StyleBox.Margin.Right, 8)),
            E<PanelContainer>().Class("BackgroundDark").Panel(FlatBox.Fill(proto.Background)),
        };
    }

    private static StyleRule Panel(string styleClass, StyleBox box)
        => E().Class(styleClass).Prop(PanelContainer.StylePropertyPanel, box).Modulate(Color.White);

    private static StyleBoxFlat BackgroundBox(Color color, Color border)
        => FlatBox.Bordered(color, border)
            .WithPadding(StyleBox.Margin.All, 1)
            .WithContentMargin(StyleBox.Margin.Vertical, 2)
            .WithContentMargin(StyleBox.Margin.Horizontal, 14);
}
