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
    private static readonly Dictionary<string, string[]> AliasTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["海都"] = ["利姆萨罗敏萨", "利姆萨·罗敏萨", "Limsa Lominsa"],
        ["limsa"] = ["Limsa Lominsa", "利姆萨罗敏萨", "利姆萨·罗敏萨"],
        ["limsa lominsa"] = ["Limsa Lominsa", "利姆萨罗敏萨", "利姆萨·罗敏萨"],
        ["森都"] = ["格里达尼亚", "Gridania"],
        ["gridania"] = ["Gridania", "格里达尼亚"],
        ["grid"] = ["Gridania", "格里达尼亚"],
        ["沙都"] = ["乌尔达哈", "Ul'dah", "Uldah"],
        ["uldah"] = ["Ul'dah", "Uldah", "乌尔达哈"],
        ["ul'dah"] = ["Ul'dah", "Uldah", "乌尔达哈"],
        ["黄金港"] = ["黄金港", "Kugane"],
        ["kugane"] = ["Kugane", "黄金港"],
        ["神拳痕"] = ["神拳痕", "Rhalgr's Reach", "Rhalgrs Reach"],
        ["rhalgr"] = ["Rhalgr's Reach", "Rhalgrs Reach", "神拳痕"],
        ["水晶都"] = ["水晶都", "Crystarium"],
        ["crystarium"] = ["Crystarium", "The Crystarium", "水晶都"],
        ["旧都"] = ["旧都", "Sharlayan", "Old Sharlayan"],
        ["sharlayan"] = ["Sharlayan", "Old Sharlayan", "旧都"],
        ["old sharlayan"] = ["Old Sharlayan", "Sharlayan", "旧都"],
        ["拉札罕"] = ["拉札罕", "Radz-at-Han", "RadzatHan"],
        ["radz-at-han"] = ["Radz-at-Han", "RadzatHan", "拉札罕"],
        ["radzat-han"] = ["Radz-at-Han", "RadzatHan", "拉札罕"],
        ["九宝"] = ["九宝", "Tuliyollal"],
        ["tuliyollal"] = ["Tuliyollal", "九宝"],
        ["伊修加德"] = ["伊修加德", "Ishgard"],
        ["ishgard"] = ["Ishgard", "伊修加德"],
        ["摩杜纳"] = ["摩杜纳", "Mor Dhona"],
        ["mor dhona"] = ["Mor Dhona", "摩杜纳"],
        ["萨雷安"] = ["萨雷安", "Solution Nine"],
        ["solution nine"] = ["Solution Nine", "萨雷安"],
    };

    private readonly IDataManager dataManager;
    private readonly ActivityLog log;
    private List<AetheryteMatch> cached = [];
    private DateTime lastRefresh = DateTime.MinValue;

    public AetheryteResolver(IDataManager dataManager, ActivityLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    public static IReadOnlyCollection<string> GetAliasKeys() => AliasTargets.Keys;

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

        foreach (var (alias, targets) in AliasTargets)
        {
            if (!ContainsToken(q, alias))
            {
                continue;
            }

            var aliasMatch = FindByTargets(list, targets);
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

    private static bool ContainsToken(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static AetheryteMatch? FindByTargets(IReadOnlyList<AetheryteMatch> list, IEnumerable<string> targets)
    {
        foreach (var target in targets)
        {
            var match = list.FirstOrDefault(a => a.Name.Contains(target, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }
}
