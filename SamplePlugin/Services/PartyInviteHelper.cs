using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

/// <summary>
/// 组队邀请：使用游戏原生 InfoProxyPartyInvite。
/// 外域（城市/野外）邀请用通用 InviteToParty(contentId, name, worldId)，
/// 该函数支持仅凭「名字 + 服务器」邀请（contentId 可为 0），因此即便对方不在附近、
/// 只要聊天里能拿到 玩家名 + 服务器名 就能发起邀请。
/// InInstance 系列函数仅在副本内有效，不能用于外域。
/// </summary>
public sealed class PartyInviteHelper
{
    private readonly PlayerTargeting playerTargeting;
    private readonly IDataManager dataManager;
    private readonly ActivityLog log;

    public PartyInviteHelper(PlayerTargeting playerTargeting, IDataManager dataManager, ActivityLog log)
    {
        this.playerTargeting = playerTargeting;
        this.dataManager = dataManager;
        this.log = log;
    }

    public bool TryInvite(string playerName, string? world, out string message)
    {
        PlayerTargeting.NormalizePlayerRef(ref playerName, ref world);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            message = "缺少邀请目标玩家名。";
            return false;
        }

        // 收集 contentId / worldId：优先用附近玩家对象，其次用聊天里的服务器名
        ulong contentId = 0;
        ushort worldId = 0;
        string resolvedName = playerName;

        if (playerTargeting.TryFindNearbyPlayer(playerName, world, out var nearby) && nearby != null)
        {
            resolvedName = nearby.Name.TextValue;
            worldId = ResolveWorldId(nearby);
            contentId = ReadContentId(nearby);
        }

        if (worldId == 0 && !string.IsNullOrWhiteSpace(world))
        {
            worldId = ResolveWorldId(world);
        }

        if (TryNativeInvite(contentId, resolvedName, worldId, out var method))
        {
            message = $"已发送组队邀请（{method}）-> {FormatRef(resolvedName, world)}";
            log.Info(message);
            return true;
        }

        // 兜底：文字指令
        try
        {
            GameActionExecutor.InviteToParty(resolvedName, worldId != 0 ? world : null);
            message = $"已发送组队邀请（/invite 兜底）-> {FormatRef(resolvedName, world)}";
            log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = $"邀请失败: {ex.Message}。请确认对方名字/服务器是否正确，或对方是否可被邀请。";
            log.Error(message);
            return false;
        }
    }

    private unsafe bool TryNativeInvite(ulong contentId, string name, ushort worldId, out string method)
    {
        method = string.Empty;

        var proxy = InfoProxyPartyInvite.Instance();
        if (proxy == null)
        {
            log.Warn("InfoProxyPartyInvite 不可用。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(name) || worldId == 0)
        {
            log.Warn($"邀请缺少必要信息: name='{name}', worldId={worldId}");
            return false;
        }

        // 通用邀请：外域可用，contentId 可为 0（仅凭名字+服务器）
        if (proxy->InviteToParty(contentId, name, worldId))
        {
            method = contentId != 0 ? "contentId+name+world" : "name+world";
            return true;
        }

        // 已知 contentId 时再试一次纯 contentId 邀请
        if (contentId != 0 && proxy->InviteToPartyContentId(contentId, worldId))
        {
            method = "contentId";
            return true;
        }

        log.Warn($"原生邀请失败 contentId={contentId} name={name} worldId={worldId}");
        return false;
    }

    private static unsafe ulong ReadContentId(IPlayerCharacter player)
    {
        var character = (Character*)player.Address;
        return character == null ? 0UL : character->ContentId;
    }

    private static ushort ResolveWorldId(IPlayerCharacter player)
    {
        if (player.HomeWorld.IsValid)
        {
            return (ushort)player.HomeWorld.Value.RowId;
        }

        return 0;
    }

    private ushort ResolveWorldId(string worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
        {
            return 0;
        }

        var sheet = dataManager.GetExcelSheet<World>();
        if (sheet == null)
        {
            return 0;
        }

        foreach (var row in sheet)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            var name = row.Name.ExtractText();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            if (name.Equals(worldName, StringComparison.OrdinalIgnoreCase))
            {
                return (ushort)row.RowId;
            }
        }

        log.Warn($"无法解析服务器名: {worldName}");
        return 0;
    }

    private static string FormatRef(string playerName, string? world) =>
        string.IsNullOrWhiteSpace(world) ? playerName : $"{playerName}@{world}";
}
