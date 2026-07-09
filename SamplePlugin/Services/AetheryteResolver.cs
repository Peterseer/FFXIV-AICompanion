using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace SamplePlugin.Services;

public sealed record AetheryteMatch(uint Id, string Name, byte SubIndex, uint TerritoryId);

public sealed unsafe class AetheryteResolver
{
    private readonly IDataManager dataManager;
    private readonly ActivityLog log;
    private List<AetheryteMatch> cached = [];
    private DateTime lastRefresh = DateTime.MinValue;

    public AetheryteResolver(IDataManager dataManager, ActivityLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public IReadOnlyList<AetheryteMatch> GetUnlockedAetherytes(bool forceRefresh = false)
    {
        if (forceRefresh || DateTime.UtcNow - lastRefresh > TimeSpan.FromMinutes(10))
        {
            Refresh();
        }

        return cached;
    }

    public AetheryteMatch? FindByName(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var list = GetUnlockedAetherytes();
        var q = query.Trim();

        // 语义别名（玩家常用简称 → 水晶全名关键词）
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["海都"] = "利姆萨罗敏萨上层甲板",
            ["limsa"] = "Limsa Lominsa Upper Decks",
            ["森都"] = "格里达尼亚新街",
            ["gridania"] = "New Gridania",
            ["沙都"] = "乌尔达哈来生回廊",
            ["uldah"] = "Ul'dah - Steps of Nald",
            ["黄金港"] = "黄金港",
            ["kugane"] = "Kugane",
            ["神拳痕"] = "神拳痕",
            ["rhalgr"] = "Rhalgr's Reach",
            ["水晶都"] = "水晶都",
            ["crystarium"] = "The Crystarium",
            ["旧都"] = "旧都",
            ["old sharlayan"] = "Old Sharlayan",
            ["拉札罕"] = "拉札罕",
            ["radz-at-han"] = "Radz-at-Han",
            ["九宝"] = "九宝",
            ["tuliyollal"] = "Tuliyollal",
        };

        if (aliases.TryGetValue(q, out var aliasTarget))
        {
            var aliasMatch = list.FirstOrDefault(a =>
                a.Name.Contains(aliasTarget, StringComparison.OrdinalIgnoreCase));
            if (aliasMatch != null)
            {
                return aliasMatch;
            }
        }

        return list.FirstOrDefault(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(a => q.Contains(a.Name, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryTeleport(string aetheryteName, out string error)
    {
        error = string.Empty;
        var match = FindByName(aetheryteName);
        if (match == null)
        {
            error = $"未找到已解锁的水晶: {aetheryteName}";
            return false;
        }

        var telepo = Telepo.Instance();
        if (telepo == null)
        {
            error = "Telepo 不可用。";
            return false;
        }

        if (!telepo->Teleport(match.Id, match.SubIndex))
        {
            error = $"传送失败: {match.Name} (id={match.Id})";
            return false;
        }

        log.Info($"已发起传送 -> {match.Name} (id={match.Id})");
        return true;
    }

    /// <summary>
    /// 在指定区域内查找已解锁的水晶（用于 flag 寻路时传送到目标地图）。
    /// </summary>
    public AetheryteMatch? FindNearestInTerritory(uint territoryId)
    {
        if (territoryId == 0)
        {
            return null;
        }

        var list = GetUnlockedAetherytes();
        return list.FirstOrDefault(a => a.TerritoryId == territoryId)
               ?? list.FirstOrDefault(a => a.TerritoryId != 0);
    }

    public string BuildPromptList(int maxItems = 40)
    {
        var list = GetUnlockedAetherytes();
        if (list.Count == 0)
        {
            return "（未检测到已解锁水晶）";
        }

        var names = list.Take(maxItems).Select(a => a.Name);
        var text = string.Join(", ", names);
        if (list.Count > maxItems)
        {
            text += $"... 等共 {list.Count} 个";
        }

        return text;
    }

    public void Refresh()
    {
        var result = new List<AetheryteMatch>();
        var uiState = UIState.Instance();
        if (uiState == null)
        {
            log.Warn("无法读取 UIState，水晶列表刷新失败。");
            cached = result;
            return;
        }

        var sheet = dataManager.GetExcelSheet<Aetheryte>();
        foreach (var row in sheet)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            if (!uiState->IsAetheryteUnlocked((uint)row.RowId))
            {
                continue;
            }

            var name = row.PlaceName.Value.Name.ExtractText();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var territoryId = row.Territory.Value.RowId;
            result.Add(new AetheryteMatch((uint)row.RowId, name, 0, territoryId));
        }

        cached = result.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        lastRefresh = DateTime.UtcNow;
        log.Info($"已刷新水晶列表: {cached.Count} 个已解锁。");
    }
}
