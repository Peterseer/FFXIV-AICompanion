using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SamplePlugin.Models;
using AtkValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace SamplePlugin.Services;

/// <summary>
/// 在交易窗口打开后，将背包物品自动放入己方交易栏。
/// </summary>
internal static unsafe class TradeItemPlacer
{
    private const int MaxTradeItemSlots = 5;

    private static readonly InventoryType[] PlayerInventoryTypes =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    /// <summary>当前己方交易栏中某物品的已放入数量。</summary>
    public static int GetPlacedQuantity(uint itemId, ItemQualityPreference quality = ItemQualityPreference.Any) =>
        HasActiveTrade() ? GetLocalTradeQuantity(InventoryManager.Instance()!, itemId, quality) : 0;

    /// <summary>己方交易栏是否已有足量物品。</summary>
    public static bool HasPlacedEnough(uint itemId, int quantity, ItemQualityPreference quality = ItemQualityPreference.Any) =>
        itemId > 0 && quantity > 0 && GetPlacedQuantity(itemId, quality) >= quantity;

    /// <summary>己方交易栏是否有任意物品（不含金币槽）。</summary>
    public static bool HasLocalTradeItems()
    {
        if (!HasActiveTrade())
        {
            return false;
        }

        var inv = InventoryManager.Instance()!;
        var localItems = inv->TradeItemsLocal;
        var count = Math.Min(localItems.Length, MaxTradeItemSlots);
        for (var i = 0; i < count; i++)
        {
            if (localItems[i].ItemId != 0 && localItems[i].Quantity > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasActiveTrade()
    {
        var inv = InventoryManager.Instance();
        return inv != null && inv->TradePartnerEntityId != 0;
    }

    /// <summary>
    /// 发起将背包物品放入己方交易栏的动作。
    /// 可堆叠物品会弹出「输入数量」对话框（由 TradeMonitor 轮询处理），因此本方法只负责
    /// 触发放入，不在此判定最终数量。返回是否成功发起放入动作。
    /// </summary>
    public static bool TryPlaceItem(
        uint itemId,
        int quantity,
        ItemQualityPreference quality,
        InventoryType sourceContainer,
        int sourceSlot,
        IGameGui gameGui,
        ActivityLog log)
    {
        if (itemId == 0 || quantity <= 0)
        {
            return false;
        }

        var inv = InventoryManager.Instance();
        if (inv == null || inv->TradePartnerEntityId == 0)
        {
            return false;
        }

        if (inv->TradeLocalState != TradeState.SelectingTradeGoods)
        {
            return false;
        }

        // 已放足量，无需再操作
        if (GetLocalTradeQuantity(inv, itemId, quality) >= quantity)
        {
            return true;
        }

        var slotInfo = ResolveItemSlot(inv, itemId, quality, sourceContainer, sourceSlot);
        if (slotInfo == null)
        {
            log.Warn($"交易放入失败：背包中找不到 itemId={itemId}");
            return false;
        }

        // 优先：AgentTrade.HandleCallback（不依赖菜单语言）
        if (TryPlaceViaAgentTradeCallback(slotInfo.Value, gameGui, log))
        {
            return true;
        }

        // 备选：模拟上下文菜单点击「交易」（支持中/英/日客户端）
        return TryPlaceViaContextMenu(slotInfo.Value, gameGui, log);
    }

    private static (InventoryType Container, int Slot)? ResolveItemSlot(
        InventoryManager* inv,
        uint itemId,
        ItemQualityPreference quality,
        InventoryType sourceContainer,
        int sourceSlot)
    {
        if (sourceContainer != InventoryType.Invalid && sourceSlot >= 0)
        {
            var item = inv->GetInventorySlot(sourceContainer, sourceSlot);
            if (item != null && item->ItemId != 0 && MatchesInventoryItem(item, itemId, quality))
            {
                return (sourceContainer, sourceSlot);
            }
        }

        return FindItemSlot(inv, itemId, quality);
    }

    /// <summary>
    /// 通过 AgentTrade.HandleCallback 直接放入，避免依赖菜单文本语言。
    /// </summary>
    private static bool TryPlaceViaAgentTradeCallback(
        (InventoryType Container, int Slot) slotInfo,
        IGameGui gameGui,
        ActivityLog log)
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
        {
            return false;
        }

        var agentContext = (AgentInventoryContext*)agentModule->GetAgentByInternalId(AgentId.InventoryContext);
        var agentTrade = AgentTrade.Instance();
        if (agentContext == null || agentTrade == null)
        {
            return false;
        }

        if (!agentTrade->IsAgentActive())
        {
            return false;
        }

        var tradeAddonId = agentTrade->AddonId;
        if (tradeAddonId == 0)
        {
            var tradePtr = gameGui.GetAddonByName("Trade", 1);
            if (!tradePtr.IsNull)
            {
                tradeAddonId = ((AtkUnitBase*)tradePtr.Address)->Id;
            }
        }

        if (tradeAddonId == 0)
        {
            return false;
        }

        agentContext->OpenForItemSlot(slotInfo.Container, slotInfo.Slot, 0, tradeAddonId);

        var callbackInfos = agentContext->ContextCallbackInfos;
        if (callbackInfos == null)
        {
            return false;
        }

        var tradeEventVtable = agentTrade->InventoryContextEvent.VirtualTable;
        var count = Math.Min(agentContext->ContextItemCount, 32);

        for (var i = 0; i < count; i++)
        {
            var info = callbackInfos[i];
            if (info.Handler == null)
            {
                continue;
            }

            if (tradeEventVtable != null && info.Handler->VirtualTable == tradeEventVtable)
            {
                agentTrade->HandleCallback(
                    (uint)slotInfo.Slot,
                    slotInfo.Container,
                    default(InventoryContextFlag),
                    info.CallbackParam);

                log.Info($"已通过 AgentTrade.HandleCallback 放入物品 ({slotInfo.Container}[{slotInfo.Slot}], param={info.CallbackParam})");
                return true;
            }
        }

        return false;
    }

    private static bool TryPlaceViaContextMenu(
        (InventoryType Container, int Slot) slotInfo,
        IGameGui gameGui,
        ActivityLog log)
    {
        var agentModule = AgentModule.Instance();
        if (agentModule == null)
        {
            return false;
        }

        var agentContext = (AgentInventoryContext*)agentModule->GetAgentByInternalId(AgentId.InventoryContext);
        var agentTrade = AgentTrade.Instance();
        if (agentContext == null || agentTrade == null)
        {
            return false;
        }

        var tradeAddonId = agentTrade->AddonId;
        if (tradeAddonId == 0)
        {
            var tradePtr = gameGui.GetAddonByName("Trade", 1);
            if (!tradePtr.IsNull)
            {
                tradeAddonId = ((AtkUnitBase*)tradePtr.Address)->Id;
            }
        }

        if (tradeAddonId == 0)
        {
            log.Warn("交易放入失败：无法获取 Trade addon Id。");
            return false;
        }

        agentContext->OpenForItemSlot(slotInfo.Container, slotInfo.Slot, 0, tradeAddonId);

        var tradeMenuIndex = FindTradeMenuIndex(agentContext);
        if (tradeMenuIndex < 0)
        {
            log.Warn("交易放入失败：上下文菜单中未找到「交易/Trade」选项。");
            return false;
        }

        var contextMenuPtr = gameGui.GetAddonByName("ContextMenu", 1);
        if (contextMenuPtr.IsNull)
        {
            log.Warn("交易放入失败：ContextMenu 未打开。");
            return false;
        }

        FireCallback((AtkUnitBase*)contextMenuPtr.Address, 0, tradeMenuIndex, 0U, 0, 0);
        log.Info($"已通过上下文菜单放入交易物品 ({slotInfo.Container}[{slotInfo.Slot}])");
        return true;
    }

    private static int FindTradeMenuIndex(AgentInventoryContext* agentContext)
    {
        var max = Math.Min(agentContext->ContextItemCount, 64);
        var start = agentContext->ContexItemStartIndex;

        for (var i = 0; i < max; i++)
        {
            var param = agentContext->EventParams[start + i];
            if (param.Type is not (AtkValueType.String or AtkValueType.ManagedString))
            {
                continue;
            }

            var text = ReadAtkValueString(param).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (IsTradeMenuText(text))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsTradeMenuText(string text)
    {
        // 英 / 中 / 日客户端
        if (text.Equals("Trade", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Contains("交易", StringComparison.Ordinal))
        {
            return true;
        }

        if (text.Contains("トレード", StringComparison.Ordinal))
        {
            return true;
        }

        return text.Contains("Trade", StringComparison.OrdinalIgnoreCase)
               && text.Contains("Item", StringComparison.OrdinalIgnoreCase);
    }

    private static (InventoryType Container, int Slot)? FindItemSlot(
        InventoryManager* inv,
        uint itemId,
        ItemQualityPreference quality)
    {
        foreach (var container in PlayerInventoryTypes)
        {
            var inventoryContainer = inv->GetInventoryContainer(container);
            if (inventoryContainer == null || !inventoryContainer->IsLoaded)
            {
                continue;
            }

            var size = Math.Min(inventoryContainer->Size, 140);
            for (var slot = 0; slot < size; slot++)
            {
                var item = inv->GetInventorySlot(container, slot);
                if (item == null || item->ItemId == 0 || item->Quantity <= 0)
                {
                    continue;
                }

                if (MatchesInventoryItem(item, itemId, quality))
                {
                    return (container, slot);
                }
            }
        }

        return null;
    }

    private static int GetLocalTradeQuantity(InventoryManager* inv, uint itemId, ItemQualityPreference quality)
    {
        var total = 0;
        var localItems = inv->TradeItemsLocal;
        var count = Math.Min(localItems.Length, MaxTradeItemSlots);
        for (var i = 0; i < count; i++)
        {
            ref var slot = ref localItems[i];
            if (slot.ItemId == 0 || slot.Quantity <= 0)
            {
                continue;
            }

            if (MatchesInventoryItem((InventoryItem*)Unsafe.AsPointer(ref slot), itemId, quality))
            {
                total += slot.Quantity;
            }
        }

        return total;
    }

    private static bool MatchesItemId(uint slotItemId, uint targetItemId, ItemQualityPreference quality)
    {
        var (baseId, _) = ItemUtil.GetBaseId(slotItemId);
        var isHq = ItemUtil.IsHighQuality(slotItemId);
        if (slotItemId != targetItemId && baseId != targetItemId)
        {
            return false;
        }

        return quality switch
        {
            ItemQualityPreference.NqOnly => !isHq,
            ItemQualityPreference.HqOnly => isHq,
            _ => true,
        };
    }

    private static bool MatchesInventoryItem(InventoryItem* item, uint targetItemId, ItemQualityPreference quality)
    {
        var slotItemId = item->ItemId;
        var baseId = item->GetBaseItemId();
        var isHq = item->IsHighQuality();
        if (slotItemId != targetItemId && baseId != targetItemId)
        {
            return false;
        }

        return quality switch
        {
            ItemQualityPreference.NqOnly => !isHq,
            ItemQualityPreference.HqOnly => isHq,
            _ => true,
        };
    }

    private static string ReadAtkValueString(AtkValue value)
    {
        try
        {
            if (value.Type is AtkValueType.String or AtkValueType.ManagedString)
            {
                return value.String.ToString()?.TrimEnd('\0') ?? string.Empty;
            }
        }
        catch
        {
            // ignore
        }

        return string.Empty;
    }

    private static void FireCallback(AtkUnitBase* unitBase, params object[] values)
    {
        var atkValues = CreateAtkValueArray(values);
        if (atkValues == null)
        {
            return;
        }

        try
        {
            unitBase->FireCallback((uint)values.Length, atkValues);
        }
        finally
        {
            Marshal.FreeHGlobal(new IntPtr(atkValues));
        }
    }

    private static AtkValue* CreateAtkValueArray(object[] values)
    {
        var atkValues = (AtkValue*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue));
        if (atkValues == null)
        {
            return null;
        }

        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var v = values[i];
                switch (v)
                {
                    case uint u:
                        atkValues[i].Type = AtkValueType.UInt;
                        atkValues[i].UInt = u;
                        break;
                    case int n:
                        atkValues[i].Type = AtkValueType.Int;
                        atkValues[i].Int = n;
                        break;
                    case float f:
                        atkValues[i].Type = AtkValueType.Float;
                        atkValues[i].Float = f;
                        break;
                    case bool b:
                        atkValues[i].Type = AtkValueType.Bool;
                        atkValues[i].Byte = (byte)(b ? 1 : 0);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported AtkValue type {v.GetType()}");
                }
            }
        }
        catch
        {
            Marshal.FreeHGlobal(new IntPtr(atkValues));
            return null;
        }

        return atkValues;
    }
}
