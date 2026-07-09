using System;
using System.Numerics;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using SamplePlugin.Models;
using SamplePlugin.Services;

namespace SamplePlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Configuration Configuration => plugin.Configuration;

    private string apiKeyBuffer = string.Empty;
    private string personaBuffer = string.Empty;
    private string adminIdBuffer = string.Empty;
    private string inventorySearchBuffer = string.Empty;
    private string presetItemBuffer = string.Empty;
    private string presetExpectedGilBuffer = "0";
    private int presetQuantity = 1;
    private ItemQualityPreference presetQuality = ItemQualityPreference.Any;
    private bool buffersInitialized;

    public ConfigWindow(Plugin plugin) : base("AI Companion · 设置###AICompanionConfig")
    {
        this.plugin = plugin;
        Size = new Vector2(560, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        if (Configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw()
    {
        EnsureBuffers();
        plugin.UiAssets.DrawPanelBackground(0.03f);

        plugin.UiAssets.DrawGameIcon(60443, 24f);
        ImGui.SameLine();
        ImGui.TextColored(UiTheme.AccentGold, "AI Companion 设置");
        ImGui.TextColored(UiTheme.Muted, "「核心」页含 API Key、Model、Persona 等人设配置");
        ImGui.Spacing();

        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("核心"))
            {
                using (var scroll = ImRaii.Child("ScrollCore", new Vector2(0, ImGui.GetContentRegionAvail().Y - 4), false))
                {
                    DrawCoreTab();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("社交 / 旅行"))
            {
                using (var scroll = ImRaii.Child("ScrollSocial", new Vector2(0, ImGui.GetContentRegionAvail().Y - 4), false))
                {
                    DrawSocialTravelTab();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("安全 / 频道"))
            {
                using (var scroll = ImRaii.Child("ScrollSafety", new Vector2(0, ImGui.GetContentRegionAvail().Y - 4), false))
                {
                    DrawSafetyChannelTab();
                }

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("资源"))
            {
                using (var scroll = ImRaii.Child("ScrollResources", new Vector2(0, ImGui.GetContentRegionAvail().Y - 4), false))
                {
                    DrawResourcesTab();
                }

                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), "警告: 自动聊天/动作/交易有封号风险，请谨慎使用。");
    }

    private void DrawCoreTab()
    {
        UiTheme.BeginSection();
        UiTheme.SectionHeader("核心开关");
        var enabled = Configuration.Enabled;
        if (ImGui.Checkbox("启用 AI 互动", ref enabled))
        {
            Configuration.Enabled = enabled;
            Configuration.Save();
            plugin.ActivityLog.Info(enabled ? "AI 互动已启用。" : "AI 互动已禁用。");
        }

        ImGui.TextUnformatted("交互语言（AI 回复与游戏内自动消息）");
        var langIndex = Configuration.InteractionLanguage == PluginLanguage.English ? 1 : 0;
        if (ImGui.Combo("##InteractionLanguage", ref langIndex, "简体中文\0English\0", 2))
        {
            var newLang = langIndex == 1 ? PluginLanguage.English : PluginLanguage.Chinese;
            if (newLang != Configuration.InteractionLanguage)
            {
                Configuration.InteractionLanguage = newLang;
                Configuration.Save();
                plugin.ActivityLog.Info($"交互语言已切换为: {PluginLocale.LanguageDisplayName(newLang)}");
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("加载该语言默认人设"))
        {
            Configuration.Persona = PluginLocale.GetDefaultPersona(Configuration.InteractionLanguage);
            personaBuffer = Configuration.Persona;
            Configuration.Save();
            plugin.ActivityLog.Info("已加载默认 Persona。");
        }

        UiTheme.EndSection();

        UiTheme.BeginSection();
        UiTheme.SectionHeader("API 设置");
        var baseUrl = Configuration.ApiBaseUrl;
        if (ImGui.InputText("Base URL", ref baseUrl, 256))
        {
            Configuration.ApiBaseUrl = baseUrl;
            Configuration.Save();
        }

        if (ImGui.InputText("API Key", ref apiKeyBuffer, 256, ImGuiInputTextFlags.Password))
        {
            Configuration.ApiKey = apiKeyBuffer;
            Configuration.Save();
        }

        var model = Configuration.Model;
        if (ImGui.InputText("Model", ref model, 128))
        {
            Configuration.Model = model;
            Configuration.Save();
        }

        var temperature = Configuration.Temperature;
        if (ImGui.SliderFloat("Temperature", ref temperature, 0f, 2f))
        {
            Configuration.Temperature = temperature;
            Configuration.Save();
        }

        var cooldown = Configuration.ResponseCooldownSeconds;
        if (ImGui.InputInt("回复冷却 (秒)", ref cooldown))
        {
            Configuration.ResponseCooldownSeconds = Math.Max(1, cooldown);
            Configuration.Save();
        }

        var maxContext = Configuration.MaxContextMessages;
        if (ImGui.InputInt("上下文条数", ref maxContext))
        {
            Configuration.MaxContextMessages = Math.Max(5, maxContext);
            Configuration.Save();
        }

        UiTheme.EndSection();

        UiTheme.BeginSection();
        UiTheme.SectionHeader("角色与管理");
        ImGui.TextUnformatted("AI Persona（角色人设）");
        if (ImGui.InputTextMultiline("##Persona", ref personaBuffer, 4096, new Vector2(-1, 120)))
        {
            Configuration.Persona = personaBuffer;
            Configuration.Save();
        }

        ImGui.TextUnformatted("管理员 ID（玩家名@服务器名）");
        if (ImGui.InputText("##AdminId", ref adminIdBuffer, 128))
        {
            Configuration.AdminId = adminIdBuffer;
            Configuration.Save();
        }

        ImGui.TextDisabled("管理员私聊: emote/chat/social on|off, status, reset, help");
        ImGui.TextDisabled("社交: friend/trade/invite/target 玩家@服, tp/teleport 水晶名, gs/gearset, nav stop");
        UiTheme.EndSection();
    }

    private void DrawSocialTravelTab()
    {
        UiTheme.BeginSection();
        UiTheme.SectionHeader("社交行为（高风险，默认关闭）");
        DrawChannelToggle("启用社交自动化", () => Configuration.AllowSocialAutomation, v => Configuration.AllowSocialAutomation = v);

        using (ImRaii.Disabled(!Configuration.AllowSocialAutomation))
        {
            DrawChannelToggle("  允许加好友", () => Configuration.AllowFriendRequest, v => Configuration.AllowFriendRequest = v);
            DrawChannelToggle("  允许交易", () => Configuration.AllowTrade, v => Configuration.AllowTrade = v);
            DrawChannelToggle("  交易金额监控", () => Configuration.AllowTradeMonitor, v => Configuration.AllowTradeMonitor = v);
            DrawChannelToggle("  交易自动确认（卖家侧）", () => Configuration.AllowTradeAutoConfirm, v => Configuration.AllowTradeAutoConfirm = v);
            DrawChannelToggle("  允许传送", () => Configuration.AllowTeleport, v => Configuration.AllowTeleport = v);
            DrawChannelToggle("  允许组队邀请", () => Configuration.AllowPartyInvite, v => Configuration.AllowPartyInvite = v);
            DrawChannelToggle("  允许 vnav 寻路", () => Configuration.AllowNavigation, v => Configuration.AllowNavigation = v);
            DrawChannelToggle("  允许切换装备组", () => Configuration.AllowGearsetSwitch, v => Configuration.AllowGearsetSwitch = v);
        }

        ImGui.TextDisabled("英文示例: \"come to Limsa\" → teleport; 发送 flag + \"come here\" → navigate");
        UiTheme.EndSection();

        DrawCharacterProfileSection();
        DrawVnavStatus();
        DrawAetheryteSection();
    }

    private void DrawSafetyChannelTab()
    {
        UiTheme.BeginSection();
        UiTheme.SectionHeader("安全设置");
        DrawChannelToggle("启用默语调试 (/echo aic ...)", () => Configuration.EnableEchoDebug, v => Configuration.EnableEchoDebug = v);
        ImGui.TextDisabled("自测: /echo aic hello");
        DrawChannelToggle("允许 AI 在公屏发言", () => Configuration.AllowSayChannelOutput, v => Configuration.AllowSayChannelOutput = v);
        DrawChannelToggle("仅限好友/小队成员互动", () => Configuration.InteractOnlyWithFriendsOrParty, v => Configuration.InteractOnlyWithFriendsOrParty = v);
        UiTheme.EndSection();

        UiTheme.BeginSection();
        UiTheme.SectionHeader("监听频道");
        DrawChannelToggle("公开说话 (Say)", () => Configuration.ListenSay, v => Configuration.ListenSay = v);
        DrawChannelToggle("喊话 (Shout)", () => Configuration.ListenShout, v => Configuration.ListenShout = v);
        DrawChannelToggle("私聊 (Tell)", () => Configuration.ListenTell, v => Configuration.ListenTell = v);
        DrawChannelToggle("部队 (FC)", () => Configuration.ListenFc, v => Configuration.ListenFc = v);
        DrawChannelToggle("通讯贝 (LS)", () => Configuration.ListenLs, v => Configuration.ListenLs = v);
        DrawChannelToggle("小队 (Party)", () => Configuration.ListenParty, v => Configuration.ListenParty = v);
        DrawChannelToggle("联盟 (Alliance)", () => Configuration.ListenAlliance, v => Configuration.ListenAlliance = v);
        DrawChannelToggle("情感动作", () => Configuration.ListenEmotes, v => Configuration.ListenEmotes = v);
        DrawChannelToggle("允许 AI 使用情感动作回应", () => Configuration.AllowEmoteResponse, v => Configuration.AllowEmoteResponse = v);
        UiTheme.EndSection();
    }

    private void DrawResourcesTab()
    {
        DrawPresetTradeSection();
        DrawInventorySection();

        UiTheme.BeginSection();
        UiTheme.SectionHeader("已解锁情感动作");
        if (ImGui.Button("刷新情感动作列表"))
        {
            Plugin.Framework.RunOnFrameworkThread(() => plugin.EmoteRegistry.Refresh());
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"共 {plugin.EmoteRegistry.GetUnlockedEmotes().Count} 个");

        using (var child = ImRaii.Child("EmoteList", new Vector2(-1, 160), true))
        {
            foreach (var emote in plugin.EmoteRegistry.GetUnlockedEmotes())
            {
                ImGui.TextUnformatted($"/{emote.Command} - {emote.DisplayName}");
            }
        }

        UiTheme.EndSection();
    }

    private void DrawCharacterProfileSection()
    {
        UiTheme.BeginSection();
        UiTheme.SectionHeader("角色信息 / 装备组");
        DrawChannelToggle("允许 AI 读取职业等级与装备组列表", () => Configuration.AllowCharacterProfileRead, v => Configuration.AllowCharacterProfileRead = v);
        ImGui.TextDisabled("开启后 AI 提示词会包含当前职业、已解锁等级与装备组清单。");

        if (ImGui.Button("刷新装备组列表"))
        {
            Plugin.Framework.RunOnFrameworkThread(() => plugin.CharacterProfileService.RefreshGearsets());
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"装备组: {plugin.CharacterProfileService.GetGearsets().Count} 个");

        var lang = Configuration.InteractionLanguage;
        ImGui.TextWrapped(plugin.CharacterProfileService.BuildJobSummary(lang));

        using (var gearsetChild = ImRaii.Child("GearsetList", new Vector2(-1, 100), true))
        {
            foreach (var gearset in plugin.CharacterProfileService.GetGearsets())
            {
                ImGui.TextUnformatted($"#{gearset.EnabledSlot} {gearset.Name} — {gearset.JobName} i{gearset.ItemLevel}");
            }
        }

        UiTheme.EndSection();
    }

    private void DrawVnavStatus()
    {
        ImGui.Spacing();
        var available = plugin.NavmeshIpc.IsAvailable;
        var ready = available && plugin.NavmeshIpc.IsReady();
        var color = ready ? new Vector4(0.2f, 0.9f, 0.3f, 1f)
            : available ? new Vector4(0.9f, 0.8f, 0.2f, 1f)
            : new Vector4(0.7f, 0.7f, 0.7f, 1f);
        var status = ready ? "vnavmesh: 就绪" : available ? "vnavmesh: 已安装，网格未就绪" : "vnavmesh: 未检测到";
        ImGui.TextColored(color, status);

        if (available && ImGui.Button("停止寻路"))
        {
            plugin.NavmeshIpc.Stop();
            plugin.ActivityLog.Info("已手动停止 vnav 寻路。");
        }
    }

    private void DrawAetheryteSection()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("传送水晶");
        ImGui.Separator();

        if (ImGui.Button("刷新水晶列表"))
        {
            Plugin.Framework.RunOnFrameworkThread(() => plugin.AetheryteResolver.Refresh());
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"已解锁: {plugin.AetheryteResolver.GetUnlockedAetherytes().Count} 个");

        using (var aetheryteChild = ImRaii.Child("AetheryteList", new Vector2(-1, 80), true))
        {
            foreach (var aetheryte in plugin.AetheryteResolver.GetUnlockedAetherytes())
            {
                ImGui.TextUnformatted(aetheryte.Name);
            }
        }
    }

    private void DrawInventorySection()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("背包物品索引");
        ImGui.Separator();

        if (ImGui.Button("重建物品索引"))
        {
            Plugin.Framework.RunOnFrameworkThread(() => plugin.InventoryService.Rebuild());
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"物品索引: {plugin.InventoryService.ItemCount} 个（可交易 {plugin.InventoryService.TradableItemCount}）");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##InvSearch", "输入物品名测试背包数量...", ref inventorySearchBuffer, 128);
        if (!string.IsNullOrWhiteSpace(inventorySearchBuffer) && ImGui.IsItemDeactivatedAfterEdit())
        {
            var query = inventorySearchBuffer;
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                var match = plugin.InventoryService.FindItemByName(query);
                if (match != null)
                {
                    var tradeTag = match.IsTradable ? string.Empty : "（不可交易）";
                    var countText = match.EverywhereCount >= 0 && match.EverywhereCount != match.Count
                        ? $"背包{match.Count}/全部{match.EverywhereCount}"
                        : $"x{match.Count}";
                    plugin.ActivityLog.Info($"背包查询: {match.Name} {countText}{tradeTag}");
                    plugin.InventoryService.SearchViaGameCommand(match.Name);
                }
                else
                {
                    plugin.ActivityLog.Warn($"背包查询: 未找到「{query}」，已尝试 /isearch");
                    plugin.InventoryService.SearchViaGameCommand(query);
                }
            });
        }
    }

    private void DrawPresetTradeSection()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("预设交易清单");
        ImGui.Separator();

        ImGui.TextDisabled("开启交易时，仅允许交易此清单中已启用的物品；数量、品质与价格以清单为准。");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##PresetItem", "输入物品名添加到交易清单...", ref presetItemBuffer, 128);

        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("数量##PresetQty", ref presetQuantity))
        {
            presetQuantity = Math.Max(1, presetQuantity);
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("总价 金币##PresetGil", ref presetExpectedGilBuffer, 32);

        ImGui.SameLine();
        DrawQualityCombo("品质##PresetQuality", ref presetQuality);

        if (ImGui.Button("添加预设物品"))
        {
            if (!long.TryParse(presetExpectedGilBuffer, out var gil))
            {
                plugin.ActivityLog.Warn("预设交易添加失败: 价格必须是数字。");
            }
            else if (plugin.PresetTradeService.TryAdd(presetItemBuffer, presetQuantity, gil, presetQuality, out var error))
            {
                plugin.ActivityLog.Info($"已添加预设交易物品: {presetItemBuffer}");
                presetItemBuffer = string.Empty;
                presetExpectedGilBuffer = "0";
                presetQuantity = 1;
                presetQuality = ItemQualityPreference.Any;
            }
            else
            {
                plugin.ActivityLog.Warn($"预设交易添加失败: {error}");
            }
        }

        var entries = Configuration.PresetTradeList;
        if (entries.Count == 0)
        {
            ImGui.TextDisabled("尚未添加预设交易物品。");
            return;
        }

        using var child = ImRaii.Child("PresetTradeList", new Vector2(-1, 220), true);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            ImGui.PushID($"PresetTrade{i}");
            DrawPresetTradeEntry(entry, i);
            ImGui.PopID();

            if (i < entries.Count - 1)
            {
                ImGui.Separator();
            }
        }
    }

    private void DrawPresetTradeEntry(PresetTradeEntry entry, int index)
    {
        DrawItemIcon(plugin.PresetTradeService.GetIconId(entry), entry.Quality);

        ImGui.SameLine();
        ImGui.BeginGroup();

        var enabled = entry.Enabled;
        if (ImGui.Checkbox("##Enabled", ref enabled))
        {
            entry.Enabled = enabled;
            Configuration.Save();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(entry.ItemName);

        var bagCount = plugin.PresetTradeService.GetBagCount(entry);
        var hasEnough = bagCount >= entry.Quantity;
        var bagColor = hasEnough ? new Vector4(0.2f, 0.9f, 0.3f, 1f) : new Vector4(1f, 0.55f, 0.25f, 1f);
        ImGui.SameLine();
        ImGui.TextColored(bagColor, $"背包: {bagCount}");

        var quantity = entry.Quantity;
        ImGui.SetNextItemWidth(90);
        if (ImGui.InputInt("数量", ref quantity))
        {
            entry.Quantity = Math.Max(1, quantity);
            Configuration.Save();
        }

        ImGui.SameLine();

        var gilText = entry.ExpectedGil.ToString();
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputText("总价 金币", ref gilText, 32) && long.TryParse(gilText, out var gil))
        {
            entry.ExpectedGil = Math.Max(0, gil);
            Configuration.Save();
        }

        ImGui.SameLine();

        var quality = entry.Quality;
        if (DrawQualityCombo("品质", ref quality))
        {
            entry.Quality = quality;
            Configuration.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("删除"))
        {
            plugin.PresetTradeService.RemoveAt(index);
        }

        ImGui.EndGroup();
    }

    private static bool DrawQualityCombo(string label, ref ItemQualityPreference quality)
    {
        var current = PresetTradeService.FormatQualityLabel(quality);
        var changed = false;
        if (ImGui.BeginCombo(label, current))
        {
            changed |= DrawQualityOption("皆可", ItemQualityPreference.Any, ref quality);
            changed |= DrawQualityOption("仅 NQ", ItemQualityPreference.NqOnly, ref quality);
            changed |= DrawQualityOption("仅 HQ", ItemQualityPreference.HqOnly, ref quality);
            ImGui.EndCombo();
        }

        return changed;
    }

    private static bool DrawQualityOption(string label, ItemQualityPreference value, ref ItemQualityPreference quality)
    {
        var selected = quality == value;
        if (!ImGui.Selectable(label, selected))
        {
            return false;
        }

        quality = value;
        return true;
    }

    private static void DrawItemIcon(uint iconId, ItemQualityPreference quality)
    {
        var size = new Vector2(32, 32);
        if (iconId == 0)
        {
            ImGui.Dummy(size);
            return;
        }

        var lookup = new GameIconLookup(iconId, quality == ItemQualityPreference.HqOnly, true);
        var texture = Plugin.TextureProvider.GetFromGameIcon(lookup);
        var wrap = texture.GetWrapOrEmpty();
        ImGui.Image(wrap.Handle, size);
    }

    private void EnsureBuffers()
    {
        if (buffersInitialized)
        {
            return;
        }

        apiKeyBuffer = Configuration.ApiKey;
        personaBuffer = Configuration.Persona;
        adminIdBuffer = Configuration.AdminId;
        buffersInitialized = true;
    }

    private void DrawChannelToggle(string label, Func<bool> getter, Action<bool> setter)
    {
        var value = getter();
        if (ImGui.Checkbox(label, ref value))
        {
            setter(value);
            Configuration.Save();
        }
    }
}
