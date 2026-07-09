using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace SamplePlugin.Services;

/// <summary>
/// 交易窗口 UI 操作：提出条件、确认交易、处理数量框。
/// </summary>
internal static unsafe class TradeUiHelper
{
    /// <summary>
    /// Trade 窗口确认按钮（提出条件 / 确认交易共用）。
    /// FF14 双按钮窗口约定：0 = 确认/OK，1 = 取消。此前误用 1 导致交易被取消。
    /// 「提出条件」与「确认交易」是同一按钮，游戏按 TradeState 切换其含义。
    /// </summary>
    public const int CallbackConfirm = 0;

    public static bool TryOffer(IGameGui gameGui, ActivityLog log)
    {
        if (!IsTradeWindowOpen(gameGui))
        {
            log.Warn("交易 UI：提出条件失败，Trade 窗口未打开。");
            return false;
        }

        if (IsBlockingDialogOpen(gameGui))
        {
            return false;
        }

        return TryFireTradeCallback(gameGui, CallbackConfirm, "提出条件", log);
    }

    public static bool TryConfirm(IGameGui gameGui, ActivityLog log)
    {
        if (!IsTradeWindowOpen(gameGui))
        {
            log.Warn("交易 UI：确认交易失败，Trade 窗口未打开。");
            return false;
        }

        if (IsBlockingDialogOpen(gameGui))
        {
            return false;
        }

        return TryFireTradeCallback(gameGui, CallbackConfirm, "确认交易", log);
    }

    public static bool TryAcceptSelectYesno(IGameGui gameGui, ActivityLog log)
    {
        return TryFireSelectYesno(gameGui, 0, "yes", log);
    }

    public static bool TryDeclineSelectYesno(IGameGui gameGui, ActivityLog log)
    {
        return TryFireSelectYesno(gameGui, 1, "no", log);
    }

    public static bool IsSelectYesnoOpen(IGameGui gameGui)
    {
        var yesnoPtr = gameGui.GetAddonByName("SelectYesno", 1);
        if (yesnoPtr.IsNull)
        {
            return false;
        }

        var yesno = (AtkUnitBase*)yesnoPtr.Address;
        return yesno->IsVisible;
    }

    private static bool TryFireSelectYesno(IGameGui gameGui, int callbackId, string label, ActivityLog log)
    {
        var yesnoPtr = gameGui.GetAddonByName("SelectYesno", 1);
        if (yesnoPtr.IsNull)
        {
            return false;
        }

        var yesno = (AtkUnitBase*)yesnoPtr.Address;
        if (!yesno->IsVisible)
        {
            return false;
        }

        var ok = yesno->FireCallbackInt(callbackId);
        log.Info($"SelectYesno: {label} FireCallbackInt({callbackId}) => {ok}");
        return ok;
    }

    /// <summary>阶段 1：仅写入数量到 InputNumeric，不点确认。</summary>
    public static bool TryPrepareQuantity(IGameGui gameGui, int quantity, ActivityLog log)
    {
        if (!TryGetQuantityAddon(gameGui, out var addon))
        {
            return false;
        }

        var desired = WriteQuantityValues(addon, quantity);
        log.Info($"交易数量输入: 已写入数量 {desired}");
        return true;
    }

    /// <summary>阶段 2：重新写入数量后 FireCallbackInt 确认（只调用一次）。</summary>
    public static bool TryConfirmQuantity(IGameGui gameGui, int quantity, ActivityLog log)
    {
        if (!TryGetQuantityAddon(gameGui, out var addon))
        {
            return false;
        }

        var desired = WriteQuantityValues(addon, quantity);
        var ok = addon->FireCallbackInt(desired);
        log.Info($"交易数量输入: 确认 {desired} => {ok}");
        return ok;
    }

    /// <summary>关闭可能残留的右键菜单，避免干扰交易。</summary>
    public static void TryDismissContextMenu(IGameGui gameGui)
    {
        var contextPtr = gameGui.GetAddonByName("ContextMenu", 1);
        if (contextPtr.IsNull)
        {
            return;
        }

        var menu = (AtkUnitBase*)contextPtr.Address;
        if (menu->IsVisible)
        {
            menu->Close(false);
        }
    }

    /// <summary>交易窗口是否已打开且可见。</summary>
    public static bool IsTradeWindowOpen(IGameGui gameGui)
    {
        var tradePtr = gameGui.GetAddonByName("Trade", 1);
        if (tradePtr.IsNull)
        {
            return false;
        }

        return ((AtkUnitBase*)tradePtr.Address)->IsVisible;
    }

    /// <summary>数量框 / 右键菜单等会阻塞交易操作的 UI 是否打开。</summary>
    public static bool IsBlockingDialogOpen(IGameGui gameGui)
    {
        if (IsQuantityPromptOpen(gameGui))
        {
            return true;
        }

        var contextPtr = gameGui.GetAddonByName("ContextMenu", 1);
        if (!contextPtr.IsNull && ((AtkUnitBase*)contextPtr.Address)->IsVisible)
        {
            return true;
        }

        return false;
    }

    /// <summary>「输入数量」对话框是否正打开。</summary>
    public static bool IsQuantityPromptOpen(IGameGui gameGui)
    {
        var ptr = gameGui.GetAddonByName("InputNumeric", 1);
        if (ptr.IsNull)
        {
            return false;
        }

        return ((AtkUnitBase*)ptr.Address)->IsVisible;
    }

    private static bool TryGetQuantityAddon(IGameGui gameGui, out AtkUnitBase* addon)
    {
        addon = null;
        if (!IsTradeWindowOpen(gameGui))
        {
            return false;
        }

        var ptr = gameGui.GetAddonByName("InputNumeric", 1);
        if (ptr.IsNull)
        {
            return false;
        }

        addon = (AtkUnitBase*)ptr.Address;
        return addon->IsVisible;
    }

    private static int WriteQuantityValues(AtkUnitBase* addon, int quantity)
    {
        var qty = quantity > 0 ? quantity : 1;

        if (addon->AtkValues == null || addon->AtkValuesCount < 5)
        {
            return qty;
        }

        var minVal = addon->AtkValues + 2;
        var maxVal = addon->AtkValues + 3;
        var defaultVal = addon->AtkValues + 4;
        var currentVal = addon->AtkValuesCount > 5 ? addon->AtkValues + 5 : null;

        if (maxVal->Type != AtkValueType.UInt || defaultVal->Type != AtkValueType.UInt)
        {
            return qty;
        }

        var max = maxVal->UInt;
        var min = minVal->Type == AtkValueType.UInt ? minVal->UInt : 1U;
        var desired = (uint)Math.Clamp(qty, (int)min, (int)max);
        defaultVal->UInt = desired;
        if (currentVal != null && currentVal->Type == AtkValueType.UInt)
        {
            currentVal->UInt = desired;
        }

        return (int)desired;
    }

    private static bool TryFireTradeCallback(IGameGui gameGui, int callbackId, string label, ActivityLog log)
    {
        var tradePtr = gameGui.GetAddonByName("Trade", 1);
        if (tradePtr.IsNull)
        {
            log.Warn($"交易 UI：{label} 失败，Trade 窗口未打开。");
            return false;
        }

        var trade = (AtkUnitBase*)tradePtr.Address;
        var ok = trade->FireCallbackInt(callbackId);
        log.Info($"交易 UI：{label} FireCallbackInt({callbackId}) => {ok}");
        return ok;
    }
}
