using System;

namespace SamplePlugin.Models;

public sealed class PlayerIdentity
{
    public string Name { get; init; } = string.Empty;

    public string World { get; init; } = string.Empty;

    public string FullId => string.IsNullOrEmpty(World) ? Name : $"{Name}@{World}";

    public static bool TryParseFullId(string fullId, out string name, out string world)
    {
        name = string.Empty;
        world = string.Empty;
        if (string.IsNullOrWhiteSpace(fullId))
        {
            return false;
        }

        var at = fullId.IndexOf('@');
        if (at <= 0 || at >= fullId.Length - 1)
        {
            return false;
        }

        name = fullId[..at].Trim();
        world = fullId[(at + 1)..].Trim();
        return !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(world);
    }
}
