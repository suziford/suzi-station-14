using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Chat.UI;

public sealed class RunechatFontCache
{
    [Dependency] private IResourceCache _resources = default!;

    private readonly Dictionary<(string, int), Font> _fonts = new();
    private readonly Dictionary<(string, int), Stylesheet> _sheets = new();

    public Font GetFont(string path, int size)
    {
        if (_fonts.TryGetValue((path, size), out var font))
            return font;

        var primaryFont = new VectorFont(_resources.GetResource<FontResource>(path), size);
        var isWhisper = path == RunechatStyle.WhisperFontPath;
        var fallbackPath = isWhisper
            ? RunechatStyle.WhisperFallbackFontPath
            : RunechatStyle.FallbackFontPath;
        // TinyUnicodeRus.ttf is authored at 16px/em vs Wormtown9k-Small.ttf's 8px/em (exactly 2x smaller per pt)
        var fallbackSize = isWhisper ? size * 2 : size;
        var fallbackFont = new VectorFont(_resources.GetResource<FontResource>(fallbackPath), fallbackSize);
        font = new StackedFont(primaryFont, fallbackFont);
        _fonts[(path, size)] = font;
        return font;
    }

    public Stylesheet GetStylesheet(string path, int size)
    {
        if (_sheets.TryGetValue((path, size), out var sheet))
            return sheet;

        sheet = new Stylesheet(new[]
        {
            new StyleRule(
                new SelectorElement(typeof(RichTextLabel), null, null, null),
                new[] { new StyleProperty(Label.StylePropertyFont, GetFont(path, size)) }),
        });
        _sheets[(path, size)] = sheet;
        return sheet;
    }
}
