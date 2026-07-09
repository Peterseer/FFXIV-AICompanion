using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class SocialActionService
{
    private readonly Configuration configuration;
    private readonly RuntimeControls runtimeControls;
    private readonly AetheryteResolver aetheryteResolver;
    private readonly InventoryService inventoryService;
    private readonly TradeMonitor tradeMonitor;
    private readonly NavmeshIpc navmeshIpc;
    private readonly PresetTradeService presetTradeService;
    private readonly CharacterProfileService characterProfileService;
    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly PlayerTargeting playerTargeting;
    private readonly PartyInviteHelper partyInviteHelper;
    private readonly ActivityLog log;

    public SocialActionService(
        Configuration configuration,
        RuntimeControls runtimeControls,
        AetheryteResolver aetheryteResolver,
        InventoryService inventoryService,
        TradeMonitor tradeMonitor,
        NavmeshIpc navmeshIpc,
        PresetTradeService presetTradeService,
        CharacterProfileService characterProfileService,
        IObjectTable objectTable,
        IClientState clientState,
        PlayerTargeting playerTargeting,
        PartyInviteHelper partyInviteHelper,
        ActivityLog log)
    {
        this.configuration = configuration;
        this.runtimeControls = runtimeControls;
        this.aetheryteResolver = aetheryteResolver;
        this.inventoryService = inventoryService;
        this.tradeMonitor = tradeMonitor;
        this.navmeshIpc = navmeshIpc;
        this.presetTradeService = presetTradeService;
        this.characterProfileService = characterProfileService;
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.playerTargeting = playerTargeting;
        this.partyInviteHelper = partyInviteHelper;
        this.log = log;
    }

    public bool TryExecute(AiSocialAction action, ChatContextEntry? trigger, out string message)
    {
        return TryExecuteInternal(action, trigger, isAdminOverride: false, out message);
    }

    public bool TryExecuteAdmin(AiSocialAction action, out string message)
    {
        return TryExecuteInternal(action, null, isAdminOverride: true, out message);
    }

    private bool TryExecuteInternal(AiSocialAction action, ChatContextEntry? trigger, bool isAdminOverride, out string message)
    {
        message = string.Empty;
        if (!isAdminOverride)
        {
            if (!runtimeControls.SocialEnabled || !configuration.AllowSocialAutomation)
            {
                message = "社交自动化未启用。";
                return false;
            }
        }
        else if (!runtimeControls.SocialEnabled)
        {
            message = "社交行为已被管理员临时禁用 (social off)。";
            return false;
        }

        ResolveTarget(action, trigger);

        return action.Action.ToLowerInvariant() switch
        {
            "friend" or "friendrequest" or "addfriend" => ExecuteFriend(action, isAdminOverride, out message),
            "trade" => ExecuteTrade(action, trigger, isAdminOverride, out message),
            "invite" or "party" => ExecuteInvite(action, isAdminOverride, out message),
            "target" => ExecuteTarget(action, out message),
            "teleport" or "tp" => ExecuteTeleport(action, isAdminOverride, out message),
            "navigate" or "nav" or "goto" => ExecuteNavigate(action, trigger, isAdminOverride, out message),
            "gearset" or "equip" or "gs" => ExecuteGearset(action, isAdminOverride, out message),
            _ => false,
        };
    }

    private bool ExecuteFriend(AiSocialAction action, bool isAdminOverride, out string message)
    {
        if (!isAdminOverride && !configuration.AllowFriendRequest)
        {
            message = "好友申请功能未在设置中启用。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.TargetName))
        {
            message = "缺少目标玩家名。";
            return false;
        }

        try
        {
            GameActionExecutor.SendFriendRequest(action.TargetName, action.TargetWorld);
            message = $"已发送好友申请 -> {FormatPlayerRef(action)}";
            log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            log.Error(message);
            return false;
        }
    }

    private bool ExecuteTrade(AiSocialAction action, ChatContextEntry? trigger, bool isAdminOverride, out string message)
    {
        if (!isAdminOverride && !configuration.AllowTrade)
        {
            message = "交易功能未在设置中启用。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.TargetName))
        {
            message = "缺少交易目标玩家名。";
            return false;
        }

        var resolution = presetTradeService.TryResolveTrade(action.ItemName, action.ItemQuantity, action.ExpectedGil);
        if (resolution == null)
        {
            message = string.IsNullOrWhiteSpace(action.ItemName)
                ? "交易缺少物品名，无法匹配预设交易清单。"
                : $"「{action.ItemName}」不在启用的预设交易清单中，已拒绝交易。";
            log.Warn(message);
            return false;
        }

        var bagCount = inventoryService.GetCount(resolution.ItemId, resolution.Quality);
        if (bagCount < resolution.Quantity)
        {
            message = $"预设物品库存不足: {resolution.ItemName} {PresetTradeService.FormatQualityLabel(resolution.Quality)}，需要 {resolution.Quantity}，背包 {bagCount}。";
            log.Warn(message);
            inventoryService.SearchViaGameCommand(resolution.ItemName);
            return false;
        }

        action.ItemName = resolution.ItemName;
        action.ItemQuantity = resolution.Quantity;
        action.ExpectedGil = resolution.ExpectedGil;

        try
        {
            if (!playerTargeting.TrySetTarget(action.TargetName, action.TargetWorld, out message))
            {
                log.Warn(message);
                return false;
            }

            GameActionExecutor.ExecuteSlashCommand("/trade");

            // 注册交易监控：自动放入物品 + 校验买家金币
            if (configuration.AllowTradeMonitor)
            {
                uint itemId = 0;
                InventoryType sourceContainer = InventoryType.Invalid;
                var sourceSlot = -1;

                itemId = resolution.ItemId;
                var slot = inventoryService.FindFirstSlot(resolution.ItemId, resolution.Quality);
                if (slot != null)
                {
                    sourceContainer = slot.Value.Container;
                    sourceSlot = slot.Value.Slot;
                }

                if (itemId > 0 || resolution.ExpectedGil > 0)
                {
                    tradeMonitor.Register(new PendingTrade
                    {
                        PartnerName = action.TargetName,
                        PartnerWorld = action.TargetWorld ?? string.Empty,
                        ItemId = itemId,
                        SourceContainer = sourceContainer,
                        SourceSlot = sourceSlot,
                        ItemName = resolution.ItemName,
                        ItemQuantity = resolution.Quantity,
                        Quality = resolution.Quality,
                        ExpectedGil = resolution.ExpectedGil,
                        ReplyChannel = trigger?.Channel ?? "tell",
                        ReplyTargetName = action.TargetName,
                        ReplyTargetWorld = action.TargetWorld ?? string.Empty,
                    });
                }
            }

            var itemInfo = string.IsNullOrWhiteSpace(action.ItemName)
                ? string.Empty
                : $", 物品={action.ItemName}x{action.ItemQuantity}({PresetTradeService.FormatQualityLabel(resolution.Quality)})";
            var gilInfo = action.ExpectedGil > 0 ? $", 期望={action.ExpectedGil}金币" : string.Empty;
            message = $"已发起交易 -> {FormatPlayerRef(action)}{itemInfo}{gilInfo}";
            log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            log.Error(message);
            return false;
        }
    }

    private bool ExecuteInvite(AiSocialAction action, bool isAdminOverride, out string message)
    {
        if (!isAdminOverride && !configuration.AllowPartyInvite)
        {
            message = "组队邀请功能未在设置中启用。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.TargetName))
        {
            message = "缺少邀请目标玩家名。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.TargetWorld))
        {
            message = "缺少邀请目标服务器名（targetWorld）。请从聊天上下文的 玩家名@服务器名 获取。";
            log.Warn(message);
            return false;
        }

        return partyInviteHelper.TryInvite(action.TargetName, action.TargetWorld, out message);
    }

    private bool ExecuteTarget(AiSocialAction action, out string message)
    {
        if (string.IsNullOrWhiteSpace(action.TargetName))
        {
            message = "缺少目标玩家名。";
            return false;
        }

        try
        {
            if (playerTargeting.TrySetTarget(action.TargetName, action.TargetWorld, out message))
            {
                message = $"已选中玩家 -> {FormatPlayerRef(action)}";
            }
            else
            {
                GameActionExecutor.TargetPlayerInWorld(action.TargetName);
                message = $"已执行 /target（未在附近找到实体，可能失败）-> {FormatPlayerRef(action)}";
            }

            log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            log.Error(message);
            return false;
        }
    }

    private bool ExecuteGearset(AiSocialAction action, bool isAdminOverride, out string message)
    {
        if (!isAdminOverride && !configuration.AllowGearsetSwitch)
        {
            message = "装备组切换功能未在设置中启用。";
            return false;
        }

        if (characterProfileService.TryEquipGearset(action.GearsetName, action.GearsetSlot, out message))
        {
            return true;
        }

        log.Warn(message);
        return false;
    }

    private bool ExecuteTeleport(AiSocialAction action, bool isAdminOverride, out string message)
    {
        if (!isAdminOverride && !configuration.AllowTeleport)
        {
            message = "传送功能未在设置中启用。";
            return false;
        }

        var name = action.AetheryteName;
        if (string.IsNullOrWhiteSpace(name))
        {
            message = "缺少水晶名称。";
            return false;
        }

        if (aetheryteResolver.TryTeleport(name, out var error))
        {
            message = $"传送已发起 -> {name}";
            return true;
        }

        message = error;
        log.Warn(message);
        return false;
    }

    /// <summary>
    /// 根据 flag 坐标寻路：若不在目标区域则先传送到最近水晶，再通过 vnavmesh 寻路。
    /// </summary>
    private bool ExecuteNavigate(AiSocialAction action, ChatContextEntry? trigger, bool isAdminOverride, out string message)
    {
        if (!isAdminOverride && !configuration.AllowTeleport)
        {
            message = "寻路/传送功能未在设置中启用。";
            return false;
        }

        if (trigger is not { HasMapLink: true })
        {
            message = "缺少地图坐标信息（flag）。";
            return false;
        }

        var destTerritory = trigger.MapTerritoryId;
        var worldX = trigger.MapWorldX;
        var worldZ = trigger.MapWorldZ;
        var currentTerritory = clientState.TerritoryType;

        // 不在目标区域 → 先传送到该区域最近的水晶
        if (currentTerritory != destTerritory)
        {
            var nearest = aetheryteResolver.FindNearestInTerritory(destTerritory);
            if (nearest == null)
            {
                message = $"未找到区域 {trigger.MapPlaceName} 的已解锁水晶，无法传送。";
                log.Warn(message);
                return false;
            }

            if (!aetheryteResolver.TryTeleport(nearest.Name, out var tpError))
            {
                message = tpError;
                return false;
            }

            log.Info($"已传送到 {nearest.Name}，准备寻路至 {trigger.MapCoords}");
            message = $"已传送到 {nearest.Name}，正在寻路至 {trigger.MapPlaceName} {trigger.MapCoords}...";

            // 传送后延迟寻路（等待加载）
            if (configuration.AllowNavigation && navmeshIpc.IsAvailable)
            {
                ScheduleDelayedNavigation(worldX, worldZ, trigger);
            }

            return true;
        }

        // 已在目标区域 → 直接寻路
        return TryNavigateTo(worldX, worldZ, trigger, out message);
    }

    private void ScheduleDelayedNavigation(float worldX, float worldZ, ChatContextEntry trigger)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000).ConfigureAwait(false);
            var localPlayer = objectTable.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            var y = localPlayer.Position.Y;
            var dest = new Vector3(worldX, y, worldZ);

            if (!navmeshIpc.IsReady())
            {
                log.Warn("vnavmesh 未就绪，跳过寻路。");
                return;
            }

            if (navmeshIpc.MoveCloseTo(dest, 3f))
            {
                log.Info($"vnav 寻路已启动 -> {trigger.MapPlaceName} {trigger.MapCoords}");
            }
            else
            {
                log.Warn($"vnav 寻路启动失败 -> {trigger.MapCoords}");
            }
        });
    }

    private bool TryNavigateTo(float worldX, float worldZ, ChatContextEntry trigger, out string message)
    {
        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer == null)
        {
            message = "无法获取本地玩家位置。";
            return false;
        }

        var dest = new Vector3(worldX, localPlayer.Position.Y, worldZ);

        if (!configuration.AllowNavigation)
        {
            message = $"寻路未启用。目标坐标: {trigger.MapPlaceName} {trigger.MapCoords}";
            return false;
        }

        if (!navmeshIpc.IsAvailable)
        {
            message = $"vnavmesh 未安装。目标: {trigger.MapPlaceName} {trigger.MapCoords}";
            log.Warn(message);
            return false;
        }

        if (!navmeshIpc.IsReady())
        {
            message = "vnavmesh 导航网格未就绪，请稍后再试。";
            return false;
        }

        if (navmeshIpc.MoveCloseTo(dest, 3f))
        {
            message = $"寻路已启动 -> {trigger.MapPlaceName} {trigger.MapCoords}";
            log.Info(message);
            return true;
        }

        message = $"寻路启动失败 -> {trigger.MapCoords}";
        log.Warn(message);
        return false;
    }

    private static void ResolveTarget(AiSocialAction action, ChatContextEntry? trigger)
    {
        if (trigger == null)
        {
            PlayerTargeting.NormalizePlayerRef(action);
            return;
        }

        if (string.IsNullOrWhiteSpace(action.TargetName))
        {
            action.TargetName = trigger.Sender;
        }

        if (string.IsNullOrWhiteSpace(action.TargetWorld))
        {
            action.TargetWorld = trigger.SenderWorld;
        }

        PlayerTargeting.NormalizePlayerRef(action);
    }

    private static string FormatPlayerRef(AiSocialAction action)
    {
        return string.IsNullOrWhiteSpace(action.TargetWorld)
            ? action.TargetName ?? string.Empty
            : $"{action.TargetName}@{action.TargetWorld}";
    }
}
