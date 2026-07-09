using System;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class InteractionSafetyService
{
    private readonly Configuration configuration;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly ActivityLog log;

    public InteractionSafetyService(
        Configuration configuration,
        IObjectTable objectTable,
        IPartyList partyList,
        ActivityLog log)
    {
        this.configuration = configuration;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.log = log;
    }

    public bool CanReceiveInteraction(ChatContextEntry entry)
    {
        if (entry.IsEchoDebug)
        {
            return true;
        }

        if (!configuration.InteractOnlyWithFriendsOrParty)
        {
            return true;
        }

        if (IsPartyMember(entry.Sender, entry.SenderWorld) || IsFriendNearby(entry.Sender))
        {
            return true;
        }

        log.Info($"Safety: ignored non-friend/non-party interaction from {entry.SenderFullId}.");
        return false;
    }

    public bool CanSendChat(AiChatAction chat)
    {
        if (configuration.AllowSayChannelOutput)
        {
            return true;
        }

        if (chat.Channel is "say" or "s")
        {
            log.Warn("Safety: blocked AI say-channel output.");
            return false;
        }

        return true;
    }

    private bool IsPartyMember(string name, string world)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var member in partyList)
        {
            if (!string.Equals(member.Name.TextValue, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(world)
                || string.Equals(member.World.Value.Name.ExtractText(), world, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFriendNearby(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter player)
            {
                continue;
            }

            if (!string.Equals(player.Name.TextValue, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return player.StatusFlags.HasFlag(StatusFlags.Friend);
        }

        return false;
    }
}
