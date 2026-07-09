using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public static partial class SeStringPlayerParser
{
    public static PlayerIdentity ExtractSender(SeString sender)
    {
        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload player)
            {
                var worldName = player.World.IsValid
                    ? player.World.Value.Name.ExtractText()
                    : string.Empty;
                return new PlayerIdentity
                {
                    Name = player.PlayerName,
                    World = worldName,
                };
            }
        }

        var text = sender.TextValue.Trim();
        if (PlayerIdentity.TryParseFullId(text, out var name, out var world))
        {
            return new PlayerIdentity { Name = name, World = world };
        }

        var match = NameWorldRegex().Match(text);
        if (match.Success)
        {
            return new PlayerIdentity
            {
                Name = match.Groups["name"].Value,
                World = match.Groups["world"].Value,
            };
        }

        return new PlayerIdentity { Name = text };
    }

    [GeneratedRegex(@"^(?<name>.+?)@(?<world>.+)$")]
    private static partial Regex NameWorldRegex();
}
