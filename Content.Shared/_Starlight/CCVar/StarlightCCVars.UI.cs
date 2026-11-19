using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Starlight.CCVar;

[CVarDefs]
public sealed partial class StarlightCCVars : CVars
{
    /// <summary>
    /// The theme of the interface.
    /// </summary>
    public static readonly CVarDef<string> StyleTheme =
        CVarDef.Create("ui.style_theme", "Void", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Accent color of the interface: window titles, headings, selected tabs, pressed buttons.
    /// </summary>
    public static readonly CVarDef<string> StyleAccent =
        CVarDef.Create("ui.style_accent", "#D4D4D8", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Use <see cref="StyleAccent"/> instead of the accent of the selected theme.
    /// </summary>
    public static readonly CVarDef<bool> StyleCustomAccent =
        CVarDef.Create("ui.style_custom_accent", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Where each button of the top menu bar goes: <c>Name=Pinned|Auto|Menu|Hidden;...</c>, see BarLayout.
    /// Buttons left out are Auto: on the bar while it fits, in the "more" menu otherwise.
    /// </summary>
    public static readonly CVarDef<string> MenuBarLayout =
        CVarDef.Create("ui.menu_bar_layout",
            "SandboxButton=Menu",
            CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// When the top menu bar shows keybinds on its buttons: Always, Hold (while Alt is held) or Never.
    /// </summary>
    public static readonly CVarDef<string> MenuBarKeyHints =
        CVarDef.Create("ui.menu_bar_key_hints", "Always", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Where each action and setting of the admin help header goes, same format as <see cref="MenuBarLayout"/>.
    /// </summary>
    public static readonly CVarDef<string> AHelpBarLayout =
        CVarDef.Create("ui.ahelp_bar_layout", "Ban=Menu;Bans=Menu;Kick=Menu;PopOut=Menu;Respawn=Menu",
            CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Minimum width of the separated chat panel.
    /// </summary>
    public static readonly CVarDef<int> ChatSeparatedMinWidth =
        CVarDef.Create("ui.chat_separated_min_width", 300, CVar.CLIENTONLY | CVar.ARCHIVE);
}
