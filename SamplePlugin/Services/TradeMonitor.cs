using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

/// <summary>
/// 待监控的交易会话。
/// </summary>
public sealed class PendingTrade
{
    public string PartnerName { get; init; } = string.Empty;
    public string PartnerWorld { get; init; } = string.Empty;
    public uint ItemId { get; init; }
    public ItemQualityPreference Quality { get; init; } = ItemQualityPreference.Any;
    public InventoryType SourceContainer { get; init; } = InventoryType.Invalid;
    public int SourceSlot { get; init; } = -1;
    public string ItemName { get; init; } = string.Empty;
    public int ItemQuantity { get; init; }
    public long ExpectedGil { get; init; }
    public string ReplyChannel { get; init; } = "tell";
    public string ReplyTargetName { get; init; } = string.Empty;
    public string ReplyTargetWorld { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>己方是否已将物品放入交易窗口。</summary>
    public bool ItemPlaced { get; set; }

    /// <summary>是否已通知买家金额正确。</summary>
    public bool GilConfirmedNotified { get; set; }

    /// <summary>己方是否已点击「提出条件」。</summary>
    public bool OfferSubmitted { get; set; }

    /// <summary>己方是否已点击最终「确认」。</summary>
    public bool FinalConfirmAttempted { get; set; }

    /// <summary>最终交易二次确认框是否已点击「是」。</summary>
    public bool FinalYesNoAttempted { get; set; }

    public bool LocalFinalConfirmedSeen { get; set; }

    public bool FinalConfirmPromptSeen { get; set; }

    public DateTime LastPlaceAttemptAt { get; set; } = DateTime.MinValue;

    public int PlaceAttempts { get; set; }

    /// <summary>已触发过放入动作（避免重复打开右键菜单导致交易取消）。</summary>
    public bool PlacementTriggered { get; set; }

    /// <summary>数量框 AtkValues 已写入，等待一帧后再确认。</summary>
    public bool QuantityValuesSet { get; set; }

    public DateTime QuantityValuesSetAt { get; set; } = DateTime.MinValue;

    /// <summary>数量框已确认（只点一次）。</summary>
    public bool QuantityConfirmed { get; set; }

    public DateTime QuantityConfirmedAt { get; set; } = DateTime.MinValue;
}

internal sealed class UnplannedTradeSession
{
    public string PartnerName { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public bool PromptSent { get; set; }
}

/// <summary>
/// 交易窗口监控：自动放入己方物品，校验对方金币，不符则拒绝并私聊告知。
/// </summary>
public sealed class TradeMonitor : IDisposable
{
    private const int GilSlotIndex = 5;
    private const int MonitorTimeoutSeconds = 120;
    private const int PollIntervalMs = 500;
    private const int TradeUiWarmupMs = 1500;
    private const int PlaceRetryCooldownMs = 1200;
    private const int MaxPlaceAttempts = 12;
    private const int QuantityConfirmDelayMs = 80;
    private const int QuantitySettleMs = 3000;
    private const int UnplannedTradeTimeoutSeconds = 45;

    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly Configuration configuration;
    private readonly RuntimeControls runtimeControls;
    private readonly ActivityLog log;
    private readonly OutboundEchoGuard echoGuard;
    private readonly PresetTradeService presetTradeService;
    private readonly List<PendingTrade> pendingTrades = [];
    private readonly object lockObj = new();
    private CancellationTokenSource? monitorCts;
    private UnplannedTradeSession? unplannedTrade;

    public TradeMonitor(
        IFramework framework,
        IGameGui gameGui,
        IObjectTable objectTable,
        Configuration configuration,
        RuntimeControls runtimeControls,
        ActivityLog log,
        OutboundEchoGuard echoGuard,
        PresetTradeService presetTradeService)
    {
        this.framework = framework;
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.configuration = configuration;
        this.runtimeControls = runtimeControls;
        this.log = log;
        this.echoGuard = echoGuard;
        this.presetTradeService = presetTradeService;

        EnsureMonitorRunning();
    }

    public void Register(PendingTrade trade)
    {
        lock (lockObj)
        {
            pendingTrades.RemoveAll(t =>
                string.Equals(t.PartnerName, trade.PartnerName, StringComparison.OrdinalIgnoreCase));
            pendingTrades.Add(trade);
            if (unplannedTrade != null
                && string.Equals(unplannedTrade.PartnerName, trade.PartnerName, StringComparison.OrdinalIgnoreCase))
            {
                unplannedTrade = null;
            }

            log.Info($"交易监控已注册: {trade.PartnerName}, 物品={trade.ItemName}x{trade.ItemQuantity}, 期望={trade.ExpectedGil}金币");
        }

        EnsureMonitorRunning();
    }

    public bool HasPendingTrade(string playerName)
    {
        lock (lockObj)
        {
            return pendingTrades.Exists(t =>
                string.Equals(t.PartnerName, playerName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public int PendingCount
    {
        get
        {
            lock (lockObj)
            {
                return pendingTrades.Count;
            }
        }
    }

    private void EnsureMonitorRunning()
    {
        if (monitorCts is { IsCancellationRequested: false })
        {
            return;
        }

        monitorCts?.Dispose();
        monitorCts = new CancellationTokenSource();
        _ = MonitorLoopAsync(monitorCts.Token);
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                PendingTrade[] snapshot;
                lock (lockObj)
                {
                    if (pendingTrades.Count == 0)
                    {
                        snapshot = [];
                    }
                    else
                    {
                        snapshot = pendingTrades.ToArray();
                    }
                }

                var canMonitorPending = RuntimeControls.CanMonitorTrade(configuration, runtimeControls);
                var canDetectUnplanned = RuntimeControls.CanOfferTradeInteraction(configuration, runtimeControls)
                                         && configuration.AllowTradeMonitor;

                if (canDetectUnplanned)
                {
                    await framework.RunOnFrameworkThread(CheckUnplannedTrade).ConfigureAwait(false);
                }
                else
                {
                    unplannedTrade = null;
                }

                if (canMonitorPending)
                {
                    foreach (var trade in snapshot)
                    {
                        if (DateTime.UtcNow - trade.StartedAt > TimeSpan.FromSeconds(MonitorTimeoutSeconds))
                        {
                            log.Info($"交易监控超时: {trade.PartnerName}");
                            RemovePending(trade.PartnerName);
                            continue;
                        }

                        await framework.RunOnFrameworkThread(() => CheckTrade(trade)).ConfigureAwait(false);
                    }
                }

                await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Error($"交易监控异常: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void CheckTrade(PendingTrade trade)
    {
        unsafe
        {
            var inv = InventoryManager.Instance();
            if (inv == null)
            {
                return;
            }

            if (inv->TradePartnerEntityId == 0)
            {
                CompleteIfFinalConfirmWasSent(trade, "交易窗口已关闭");
                return;
            }

            var partnerName = inv->TradePartnerNameString;
            if (string.IsNullOrWhiteSpace(partnerName))
            {
                return;
            }

            if (!partnerName.Contains(trade.PartnerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!TradeUiHelper.IsTradeWindowOpen(gameGui))
            {
                CompleteIfFinalConfirmWasSent(trade, "交易窗口已关闭");
                return;
            }

            var needsItem = trade.ItemId > 0 && trade.ItemQuantity > 0;
            var tradeAge = DateTime.UtcNow - trade.StartedAt;
            var now = DateTime.UtcNow;

            SyncSubmittedState(trade, inv->TradeLocalState);

            // 步骤 0：两阶段处理数量输入框（先写值 → 等一帧 → 再确认，且只确认一次）
            if (needsItem && !trade.ItemPlaced && TradeUiHelper.IsQuantityPromptOpen(gameGui))
            {
                if (!trade.QuantityValuesSet)
                {
                    if (TradeUiHelper.TryPrepareQuantity(gameGui, trade.ItemQuantity, log))
                    {
                        trade.QuantityValuesSet = true;
                        trade.QuantityValuesSetAt = now;
                    }
                }
                else if (!trade.QuantityConfirmed
                         && now - trade.QuantityValuesSetAt >= TimeSpan.FromMilliseconds(QuantityConfirmDelayMs))
                {
                    if (TradeUiHelper.TryConfirmQuantity(gameGui, trade.ItemQuantity, log))
                    {
                        trade.QuantityConfirmed = true;
                        trade.QuantityConfirmedAt = now;
                        TradeUiHelper.TryDismissContextMenu(gameGui);
                    }
                }

                return;
            }

            // 数量刚确认：等待物品出现在交易栏，期间禁止任何其他操作
            if (needsItem && !trade.ItemPlaced && trade.QuantityConfirmed
                && now - trade.QuantityConfirmedAt < TimeSpan.FromMilliseconds(QuantitySettleMs))
            {
                if (TradeItemPlacer.HasPlacedEnough(trade.ItemId, trade.ItemQuantity, trade.Quality))
                {
                    trade.ItemPlaced = true;
                    log.Info($"已确认 {trade.ItemName}x{trade.ItemQuantity} 在交易栏中，准备提出条件。");
                }

                return;
            }

            if (TradeUiHelper.IsBlockingDialogOpen(gameGui))
            {
                return;
            }

            // 步骤 1：放入物品（只触发一次，避免重复打开菜单取消交易）
            if (needsItem && !trade.ItemPlaced)
            {
                if (inv->TradeLocalState != TradeState.SelectingTradeGoods)
                {
                    return;
                }

                if (tradeAge < TimeSpan.FromMilliseconds(TradeUiWarmupMs))
                {
                    return;
                }

                if (TradeItemPlacer.HasPlacedEnough(trade.ItemId, trade.ItemQuantity, trade.Quality))
                {
                    trade.ItemPlaced = true;
                    log.Info($"已确认 {trade.ItemName}x{trade.ItemQuantity} 在交易栏中，准备提出条件。");
                    return;
                }

                // 已触发放入且数量流程进行中：不再重试
                if (trade.PlacementTriggered && (trade.QuantityValuesSet || trade.QuantityConfirmed))
                {
                    return;
                }

                // 已触发放入但数量框未出现（不可堆叠物品）：等待结算
                if (trade.PlacementTriggered && !trade.QuantityConfirmed
                    && now - trade.LastPlaceAttemptAt < TimeSpan.FromMilliseconds(QuantitySettleMs))
                {
                    return;
                }

                if (trade.PlaceAttempts >= MaxPlaceAttempts)
                {
                    log.Warn($"交易放入超时: {trade.ItemName}，已尝试 {trade.PlaceAttempts} 次。");
                    return;
                }

                if (trade.PlacementTriggered
                    && now - trade.LastPlaceAttemptAt < TimeSpan.FromMilliseconds(PlaceRetryCooldownMs))
                {
                    return;
                }

                trade.LastPlaceAttemptAt = now;
                trade.PlaceAttempts++;
                trade.PlacementTriggered = true;
                TradeItemPlacer.TryPlaceItem(
                    trade.ItemId,
                    trade.ItemQuantity,
                    trade.Quality,
                    trade.SourceContainer,
                    trade.SourceSlot,
                    gameGui,
                    log);
                return;
            }

            // 无物品交易（仅收金币）：跳过放入，但不自动提出条件
            if (!needsItem)
            {
                trade.ItemPlaced = true;
            }

            // 步骤 2：提出条件（必须有物品时才自动点）
            if (trade.ItemPlaced && needsItem && inv->TradeLocalState == TradeState.SelectingTradeGoods)
            {
                if (!TradeItemPlacer.HasPlacedEnough(trade.ItemId, trade.ItemQuantity, trade.Quality))
                {
                    // 数量确认后给一点缓冲，避免误判导致重新放入
                    if (trade.QuantityConfirmed
                        && now - trade.QuantityConfirmedAt < TimeSpan.FromMilliseconds(QuantitySettleMs))
                    {
                        return;
                    }

                    trade.ItemPlaced = false;
                    trade.PlacementTriggered = false;
                    trade.QuantityValuesSet = false;
                    trade.QuantityConfirmed = false;
                    log.Warn($"交易栏未检测到 {trade.ItemName}，重新尝试放入。");
                    return;
                }

                if (tradeAge < TimeSpan.FromMilliseconds(TradeUiWarmupMs))
                {
                    return;
                }

                if (!trade.OfferSubmitted && configuration.AllowTradeAutoConfirm)
                {
                    if (TradeUiHelper.TryOffer(gameGui, log))
                    {
                        trade.OfferSubmitted = true;
                    }
                }

                return;
            }

            if (trade.ItemPlaced && inv->TradeLocalState >= TradeState.LockedIn && !trade.OfferSubmitted)
            {
                trade.OfferSubmitted = true;
            }

            // 无金币监控时，提出条件后即可结束监控
            if (trade.ExpectedGil <= 0)
            {
                if (trade.ItemPlaced && inv->TradeLocalState >= TradeState.LockedIn)
                {
                    RemovePending(trade.PartnerName);
                }

                return;
            }

            // 步骤 3：监控买家放入的金币
            var remoteItems = inv->TradeItemsRemote;
            if (remoteItems.Length <= GilSlotIndex)
            {
                return;
            }

            var remoteGil = remoteItems[GilSlotIndex].Quantity;
            if (remoteGil <= 0)
            {
                return;
            }

            // 仅在对方已提出条件后再校验金币，避免交易刚打开时的脏数据误判
            if (inv->TradeRemoteState < TradeState.LockedIn)
            {
                return;
            }

            if (configuration.AllowTradeAutoConfirm
                && inv->TradeLocalState == TradeState.WaitingForConfirmation
                && inv->TradeRemoteState >= TradeState.LockedIn
                && TradeUiHelper.IsSelectYesnoOpen(gameGui))
            {
                trade.FinalConfirmPromptSeen = true;
                if (remoteGil == trade.ExpectedGil)
                {
                    if (!trade.FinalYesNoAttempted && TradeUiHelper.TryAcceptSelectYesno(gameGui, log))
                    {
                        trade.FinalYesNoAttempted = true;
                    }
                }
                else
                {
                    log.Warn($"Final trade confirm blocked: {trade.PartnerName} gil={remoteGil}, expected {trade.ExpectedGil}.");
                    TradeUiHelper.TryDeclineSelectYesno(gameGui, log);
                    inv->RefuseTrade();
                    SendTell(trade, PluginLocale.TradeWrongGilAmount(configuration.InteractionLanguage, trade.ExpectedGil, remoteGil));
                    RemovePending(trade.PartnerName);
                }

                return;
            }

            if (remoteGil == trade.ExpectedGil)
            {
                if (!trade.GilConfirmedNotified)
                {
                    log.Info($"交易金额正确: {trade.PartnerName} 放入 {remoteGil} 金币（期望 {trade.ExpectedGil}）");
                    trade.GilConfirmedNotified = true;
                }

                // 步骤 4：双方都已提出条件后，己方自动点「确认」
                if (configuration.AllowTradeAutoConfirm
                    && inv->TradeLocalState == TradeState.WaitingForConfirmation
                    && inv->TradeRemoteState >= TradeState.LockedIn
                    && !trade.FinalConfirmAttempted
                    && !trade.FinalConfirmPromptSeen)
                {
                    if (TradeUiHelper.TryConfirm(gameGui, log))
                    {
                        trade.FinalConfirmAttempted = true;
                    }
                }

                if (inv->TradeLocalState == TradeState.Confirmed)
                {
                    trade.LocalFinalConfirmedSeen = true;
                }

                return;
            }

            log.Warn($"交易金额不符: {trade.PartnerName} 放入 {remoteGil} 金币，期望 {trade.ExpectedGil} 金币，已拒绝。");
            inv->RefuseTrade();
            SendTell(trade, PluginLocale.TradeWrongGilAmount(configuration.InteractionLanguage, trade.ExpectedGil, remoteGil));
            RemovePending(trade.PartnerName);
        }
    }

    private void CheckUnplannedTrade()
    {
        if (!RuntimeControls.CanOfferTradeInteraction(configuration, runtimeControls))
        {
            unplannedTrade = null;
            return;
        }

        unsafe
        {
            var inv = InventoryManager.Instance();
            if (inv == null)
            {
                return;
            }

            if (inv->TradePartnerEntityId == 0 || !TradeUiHelper.IsTradeWindowOpen(gameGui))
            {
                unplannedTrade = null;
                return;
            }

            var partnerName = inv->TradePartnerNameString;
            if (string.IsNullOrWhiteSpace(partnerName))
            {
                return;
            }

            lock (lockObj)
            {
                if (pendingTrades.Exists(t =>
                        partnerName.Contains(t.PartnerName, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }

            if (!configuration.AllowTrade || !configuration.AllowTradeMonitor)
            {
                log.Warn($"Unplanned trade refused: trade automation disabled. Partner={partnerName}");
                inv->RefuseTrade();
                unplannedTrade = null;
                return;
            }

            if (presetTradeService.GetEnabledEntries().Count == 0)
            {
                log.Warn($"Unplanned trade refused: no enabled preset trade entries. Partner={partnerName}");
                inv->RefuseTrade();
                unplannedTrade = null;
                return;
            }

            if (unplannedTrade == null
                || !string.Equals(unplannedTrade.PartnerName, partnerName, StringComparison.OrdinalIgnoreCase))
            {
                unplannedTrade = new UnplannedTradeSession
                {
                    PartnerName = partnerName,
                    StartedAt = DateTime.UtcNow,
                };
            }

            if (!unplannedTrade.PromptSent)
            {
                SendTell(partnerName, ResolveNearbyWorld(partnerName), PluginLocale.TradeUnplannedPrompt(configuration.InteractionLanguage));
                unplannedTrade.PromptSent = true;
                log.Info($"Unplanned trade prompt sent: {partnerName}");
                return;
            }

            if (DateTime.UtcNow - unplannedTrade.StartedAt > TimeSpan.FromSeconds(UnplannedTradeTimeoutSeconds))
            {
                SendTell(partnerName, ResolveNearbyWorld(partnerName), PluginLocale.TradeUnplannedTimeout(configuration.InteractionLanguage));
                inv->RefuseTrade();
                unplannedTrade = null;
            }
        }
    }

    private void SendTell(PendingTrade trade, string message)
    {
        if (string.IsNullOrWhiteSpace(trade.ReplyTargetName))
        {
            return;
        }

        var chat = new AiChatAction
        {
            Channel = trade.ReplyChannel,
            TargetName = trade.ReplyTargetName,
            TargetWorld = trade.ReplyTargetWorld,
            Message = message,
        };

        var targetId = string.IsNullOrEmpty(chat.TargetWorld)
            ? chat.TargetName ?? string.Empty
            : $"{chat.TargetName}@{chat.TargetWorld}";

        echoGuard.Record(chat.Channel, chat.Message, targetId);
        GameActionExecutor.SendChat(chat);
    }

    private void SendTell(string targetName, string targetWorld, string message)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(targetWorld))
        {
            log.Warn($"Tell skipped: missing world for {targetName}.");
            return;
        }

        var chat = new AiChatAction
        {
            Channel = "tell",
            TargetName = targetName,
            TargetWorld = targetWorld,
            Message = message,
        };

        echoGuard.Record(chat.Channel, chat.Message, $"{targetName}@{targetWorld}");
        GameActionExecutor.SendChat(chat);
    }

    private string ResolveNearbyWorld(string playerName)
    {
        foreach (var obj in objectTable)
        {
            if (obj is not IPlayerCharacter player)
            {
                continue;
            }

            if (!string.Equals(player.Name.TextValue, playerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return player.HomeWorld.IsValid
                ? player.HomeWorld.Value.Name.ExtractText()
                : string.Empty;
        }

        return string.Empty;
    }

    private void CompleteIfFinalConfirmWasSent(PendingTrade trade, string reason)
    {
        if (!trade.LocalFinalConfirmedSeen)
        {
            return;
        }

        log.Info($"交易已完成: {trade.PartnerName}（{reason}）");
        SendTell(trade, PluginLocale.TradeCompleteThanks(configuration.InteractionLanguage));
        RemovePending(trade.PartnerName);
    }

    private void SyncSubmittedState(PendingTrade trade, TradeState localState)
    {
        if (localState == TradeState.SelectingTradeGoods)
        {
            if (trade.OfferSubmitted || trade.FinalConfirmAttempted)
            {
                log.Info($"交易内容变更，准备重新提出条件: {trade.PartnerName}");
            }

            trade.OfferSubmitted = false;
            trade.FinalConfirmAttempted = false;
            trade.FinalYesNoAttempted = false;
            trade.FinalConfirmPromptSeen = false;
            trade.LocalFinalConfirmedSeen = false;
            return;
        }

    }

    private void RemovePending(string partnerName)
    {
        lock (lockObj)
        {
            pendingTrades.RemoveAll(t =>
                string.Equals(t.PartnerName, partnerName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Dispose()
    {
        monitorCts?.Cancel();
        monitorCts?.Dispose();
    }
}
