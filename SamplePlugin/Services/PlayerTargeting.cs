using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

/// <summary>
/// 在 ObjectTable 中查找附近玩家，并通过 TargetManager 设置目标。
/// 游戏内 /target 不支持 tell 用的 玩家名@服务器 格式。
/// </summary>
public sealed class PlayerTargeting
{
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;

    public PlayerTargeting(IObjectTable objectTable, ITargetManager targetManager)
    {
        this.objectTable = objectTable;
        this.targetManager = targetManager;
    }

    public bool TryFindNearbyPlayer(string playerName, string? world, out IPlayerCharacter? player)
    {
        player = null;
        NormalizePlayerRef(ref playerName, ref world);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return false;
        }

        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter pc)
            {
                continue;
            }

            if (!pc.Name.TextValue.Equals(playerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(world) && !WorldsMatch(pc, world))
            {
                continue;
            }

            player = pc;
            return true;
        }

        return false;
    }

    public bool TrySetTarget(string playerName, string? world, out string error)
    {
        if (!TryFindNearbyPlayer(playerName, world, out var player) || player == null)
        {
            var display = FormatPlayerRef(playerName, world);
            error = $"附近找不到玩家 {display}。请与对方站在同一地图且距离较近后再交易。";
            return false;
        }

        targetManager.Target = player;
        error = string.Empty;
        return true;
    }

    public static void NormalizePlayerRef(ref string playerName, ref string? world)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return;
        }

        if (PlayerIdentity.TryParseFullId(playerName, out var name, out var parsedWorld))
        {
            playerName = name;
            if (string.IsNullOrWhiteSpace(world))
            {
                world = parsedWorld;
            }
        }
    }

    public static void NormalizePlayerRef(AiSocialAction action)
    {
        if (action.TargetName == null)
        {
            return;
        }

        var name = action.TargetName;
        var world = action.TargetWorld;
        NormalizePlayerRef(ref name, ref world);
        action.TargetName = name;
        action.TargetWorld = world;
    }

    private static bool WorldsMatch(IPlayerCharacter player, string expectedWorld)
    {
        if (!player.HomeWorld.IsValid)
        {
            return true;
        }

        var homeWorld = player.HomeWorld.Value.Name.ExtractText();
        return homeWorld.Equals(expectedWorld, StringComparison.OrdinalIgnoreCase)
               || homeWorld.Contains(expectedWorld, StringComparison.OrdinalIgnoreCase)
               || expectedWorld.Contains(homeWorld, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPlayerRef(string playerName, string? world) =>
        string.IsNullOrWhiteSpace(world) ? playerName : $"{playerName}@{world}";
}
