using System.Linq;

namespace Content.Client._Starlight.UserInterface.Controls;

/// <summary>
/// How a control should be placed in an <see cref="OverflowBar"/>.
/// </summary>
public enum BarPlacement : byte
{
    /// <summary>
    /// Always on the bar, even when it runs out of room.
    /// </summary>
    Pinned,

    /// <summary>
    /// On the bar while it fits, in the "more" menu otherwise.
    /// </summary>
    Auto,

    /// <summary>
    /// Always in the "more" menu.
    /// </summary>
    Menu,

    /// <summary>
    /// Not shown at all.
    /// </summary>
    Hidden,
}

/// <summary>
/// Represents a single entry in a bar layout, with a name and a label.
/// </summary>
public readonly record struct BarLayoutEntry(string Name, LocId Label);

/// <summary>
/// Provides methods for parsing and serializing bar layouts.
/// </summary>
public static class BarLayout
{
    public static Dictionary<string, BarPlacement> Parse(string layout)
    {
        var result = new Dictionary<string, BarPlacement>();
        foreach (var part in layout.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && Enum.TryParse<BarPlacement>(pair[1], out var placement))
                result[pair[0]] = placement;
        }

        return result;
    }

    public static string Serialize(IReadOnlyDictionary<string, BarPlacement> layout)
        => string.Join(';', layout
            .Where(p => p.Value != BarPlacement.Auto)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}"));
}
