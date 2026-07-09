using System.Text;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public static class AiPromptBuilder
{
    public static string BuildSystemPrompt(
        Configuration configuration,
        EmoteRegistry emoteRegistry,
        AetheryteResolver aetheryteResolver,
        NavmeshIpc navmeshIpc,
        PresetTradeService presetTradeService,
        CharacterProfileService characterProfileService)
    {
        return PluginLocale.IsEnglish(configuration)
            ? BuildEnglishSystemPrompt(configuration, emoteRegistry, aetheryteResolver, navmeshIpc, presetTradeService, characterProfileService)
            : BuildChineseSystemPrompt(configuration, emoteRegistry, aetheryteResolver, navmeshIpc, presetTradeService, characterProfileService);
    }

    private static string BuildChineseSystemPrompt(
        Configuration configuration,
        EmoteRegistry emoteRegistry,
        AetheryteResolver aetheryteResolver,
        NavmeshIpc navmeshIpc,
        PresetTradeService presetTradeService,
        CharacterProfileService characterProfileService)
    {
        var emoteList = emoteRegistry.BuildPromptList();
        var aetheryteList = aetheryteResolver.BuildPromptList();
        var admin = string.IsNullOrWhiteSpace(configuration.AdminId)
            ? PluginLocale.AdminNotSet(PluginLanguage.Chinese)
            : configuration.AdminId;
        var socialEnabled = configuration.AllowSocialAutomation;
        var vnavAvailable = navmeshIpc.IsAvailable;

        var sb = new StringBuilder();
        sb.AppendLine(configuration.Persona);
        sb.AppendLine();
        sb.AppendLine("你是 FINAL FANTASY XIV 游戏内的玩家角色。不要透露你是 AI 或插件。");
        sb.AppendLine("请始终使用简体中文撰写 chat.message。");
        sb.AppendLine();
        AppendChineseOutputFormat(sb);
        AppendChineseTradeRules(sb, presetTradeService, socialEnabled);
        AppendChineseTravelRules(sb, vnavAvailable);
        AppendChineseSocialRules(sb, socialEnabled, vnavAvailable, configuration.AllowGearsetSwitch);
        if (configuration.AllowCharacterProfileRead)
        {
            AppendChineseCharacterProfile(sb, characterProfileService, PluginLanguage.Chinese);
        }

        if (socialEnabled)
        {
            AppendChineseAetheryteList(sb, aetheryteList);
        }

        sb.AppendLine("## 已拥有情感动作（仅可使用以下命令名）");
        sb.AppendLine(emoteList);
        sb.AppendLine();
        sb.AppendLine("## 管理员");
        sb.AppendLine("管理员 ID 配置为: " + admin);
        return sb.ToString();
    }

    private static string BuildEnglishSystemPrompt(
        Configuration configuration,
        EmoteRegistry emoteRegistry,
        AetheryteResolver aetheryteResolver,
        NavmeshIpc navmeshIpc,
        PresetTradeService presetTradeService,
        CharacterProfileService characterProfileService)
    {
        var emoteList = emoteRegistry.BuildPromptList();
        var aetheryteList = aetheryteResolver.BuildPromptList();
        var admin = string.IsNullOrWhiteSpace(configuration.AdminId)
            ? PluginLocale.AdminNotSet(PluginLanguage.English)
            : configuration.AdminId;
        var socialEnabled = configuration.AllowSocialAutomation;
        var vnavAvailable = navmeshIpc.IsAvailable;

        var sb = new StringBuilder();
        sb.AppendLine(configuration.Persona);
        sb.AppendLine();
        sb.AppendLine("You are a player character in FINAL FANTASY XIV. Never reveal that you are an AI or a plugin.");
        sb.AppendLine("Always write chat.message in natural English.");
        sb.AppendLine();
        AppendEnglishOutputFormat(sb);
        AppendEnglishTradeRules(sb, presetTradeService, socialEnabled);
        AppendEnglishTravelRules(sb, vnavAvailable);
        AppendEnglishSocialRules(sb, socialEnabled, vnavAvailable, configuration.AllowGearsetSwitch);
        if (configuration.AllowCharacterProfileRead)
        {
            AppendEnglishCharacterProfile(sb, characterProfileService, PluginLanguage.English);
        }

        if (socialEnabled)
        {
            AppendEnglishAetheryteList(sb, aetheryteList);
        }

        sb.AppendLine("## Owned emotes (only use command names below)");
        sb.AppendLine(emoteList);
        sb.AppendLine();
        sb.AppendLine("## Admin");
        sb.AppendLine("Configured admin ID: " + admin);
        return sb.ToString();
    }

    private static void AppendChineseOutputFormat(StringBuilder sb)
    {
        sb.AppendLine("## 输出格式");
        sb.AppendLine("你必须仅返回 JSON，格式如下：");
        sb.AppendLine("""
            {
              "shouldRespond": true,
              "reason": "简要思考过程",
              "chat": {
                "channel": "tell",
                "targetName": "玩家名",
                "targetWorld": "服务器名",
                "message": "回复内容"
              },
              "emote": null,
              "customEmote": null,
              "social": null
            }
            """);
        sb.AppendLine("## 频道 channel");
        sb.AppendLine("say | shout | tell | fc | ls | party | alliance");
        sb.AppendLine();
        sb.AppendLine("## 私聊 tell 规则（重要）");
        sb.AppendLine("私聊回复必须填写 targetName 和 targetWorld（从对话上下文中的 玩家名@服务器名 获取）。");
        sb.AppendLine("游戏内实际发送格式为: /tell 玩家名@服务器名 消息内容");
        sb.AppendLine();
        sb.AppendLine("## 情感动作规则");
        sb.AppendLine("1. 不是所有对话都需要情感动作，只有必要时才使用 emote 或 customEmote。");
        sb.AppendLine("2. emote: 从「已拥有情感动作」列表选择命令名（不含/），如 wave、deny、no。");
        sb.AppendLine("3. customEmote: 自定义情感文本，通过 /em 发送。与 emote 通常二选一。");
        sb.AppendLine("4. 对话出格、无礼时优先 deny/no，可配合简短拒绝文字。");
        sb.AppendLine();
    }

    private static void AppendEnglishOutputFormat(StringBuilder sb)
    {
        sb.AppendLine("## Output format");
        sb.AppendLine("Return JSON only:");
        sb.AppendLine("""
            {
              "shouldRespond": true,
              "reason": "brief reasoning",
              "chat": {
                "channel": "tell",
                "targetName": "PlayerName",
                "targetWorld": "WorldName",
                "message": "reply text"
              },
              "emote": null,
              "customEmote": null,
              "social": null
            }
            """);
        sb.AppendLine("## Channels");
        sb.AppendLine("say | shout | tell | fc | ls | party | alliance");
        sb.AppendLine();
        sb.AppendLine("## Tell / whisper rules (important)");
        sb.AppendLine("For tell replies, always set targetName and targetWorld from context (PlayerName@World).");
        sb.AppendLine("In-game format: /tell PlayerName@World message");
        sb.AppendLine();
        sb.AppendLine("## Emote rules");
        sb.AppendLine("1. Do not use emotes in every reply — only when appropriate.");
        sb.AppendLine("2. emote: pick a command name from the owned emote list (no slash), e.g. wave, deny, no.");
        sb.AppendLine("3. customEmote: custom emote text sent via /em. Usually pick either emote or customEmote, not both.");
        sb.AppendLine("4. For rude messages, prefer deny/no with a short refusal.");
        sb.AppendLine();
    }

    private static void AppendChineseTradeRules(StringBuilder sb, PresetTradeService presetTradeService, bool socialEnabled)
    {
        sb.AppendLine("## 背包与交易（重要）");
        sb.AppendLine(presetTradeService.BuildPromptSection(PluginLanguage.Chinese));
        sb.AppendLine("当玩家询问是否有某物品、或提出收购/出售请求时：");
        sb.AppendLine("1. 必须先确认物品在预设交易清单中，未列入清单则不允许发起交易。");
        sb.AppendLine("2. 查看消息附带的 [背包: 物品名: 数量] 信息判断是否有货。");
        sb.AppendLine("3. 若背包数量不足或没有该物品，如实告知没有。");
        sb.AppendLine("4. 若清单允许、有货且玩家提出收购，使用 social.trade 发起交易：");
        sb.AppendLine("""
              "social": {
                "action": "trade",
                "targetName": "玩家名",
                "targetWorld": "服务器名",
                "itemName": "物品名（与背包提示一致）",
                "itemQuantity": 数量,
                "expectedGil": 期望对方支付的金币金额
              }
            """);
        sb.AppendLine("5. 插件会按预设清单覆盖 itemQuantity 与 expectedGil；你仍应按清单价格回复。");
        sb.AppendLine("6. 交易流程：放入物品 → 提出条件 → 对方放入金币并提出条件 → 双方确认完成。");
        sb.AppendLine("7. 插件会自动放入己方物品、点击提出条件；金额正确后自动确认（卖家侧）。");
        sb.AppendLine("8. 对方必须在同一地图且距离较近才能发起交易（游戏限制）。");
        sb.AppendLine();
        if (!socialEnabled)
        {
            sb.AppendLine("当前社交自动化未启用，social 必须始终为 null。");
            sb.AppendLine();
        }
    }

    private static void AppendEnglishTradeRules(StringBuilder sb, PresetTradeService presetTradeService, bool socialEnabled)
    {
        sb.AppendLine("## Inventory & trade (important)");
        sb.AppendLine(presetTradeService.BuildPromptSection(PluginLanguage.English));
        sb.AppendLine("When a player asks about an item or wants to buy/sell:");
        sb.AppendLine("1. The item must be on the preset trade list; refuse anything not listed.");
        sb.AppendLine("2. Use [Inventory: item: qty] hints in messages to judge stock.");
        sb.AppendLine("3. If bags do not have enough, say so honestly.");
        sb.AppendLine("4. If allowed and in stock, start trade via social.trade:");
        sb.AppendLine("""
              "social": {
                "action": "trade",
                "targetName": "PlayerName",
                "targetWorld": "WorldName",
                "itemName": "item name (match inventory hint)",
                "itemQuantity": quantity,
                "expectedGil": gil amount buyer should pay
              }
            """);
        sb.AppendLine("5. The plugin enforces quantity/expectedGil from the preset list; still quote list prices in chat.");
        sb.AppendLine("6. Trade flow: place items → offer → buyer adds gil and offers → both confirm.");
        sb.AppendLine("7. The plugin auto-places your items and offers; auto-confirms when gil matches (seller side).");
        sb.AppendLine("8. Buyer must be nearby on the same map (game limitation).");
        sb.AppendLine();
        if (!socialEnabled)
        {
            sb.AppendLine("Social automation is disabled — social must always be null.");
            sb.AppendLine();
        }
    }

    private static void AppendChineseTravelRules(StringBuilder sb, bool vnavAvailable)
    {
        sb.AppendLine("## 传送与寻路");
        sb.AppendLine("当玩家邀请你去某地（如「来海都找我」「来森都」）：");
        sb.AppendLine("- 使用 social.teleport，aetheryteName 填水晶名或简称（海都=利姆萨罗敏萨上层甲板，森都=格里达尼亚新街，沙都=乌尔达哈来生回廊）");
        sb.AppendLine("- 示例：玩家说「你来海都找我吧」→ social.teleport(aetheryteName=\"海都\")");
        sb.AppendLine();
        if (vnavAvailable)
        {
            sb.AppendLine("当玩家发送地图坐标（flag / <flag>，消息带有 [坐标: ...] 标记）：");
            sb.AppendLine("- 使用 social.navigate 自动传送到该区域并寻路至坐标");
            sb.AppendLine("- 示例：玩家发 flag → social.navigate（无需额外参数，坐标从上下文读取）");
        }
        else
        {
            sb.AppendLine("当玩家发送地图坐标（flag）：vnavmesh 未安装，只能传送到该区域最近水晶，无法自动寻路。");
        }

        sb.AppendLine();
    }

    private static void AppendEnglishTravelRules(StringBuilder sb, bool vnavAvailable)
    {
        sb.AppendLine("## Teleport & navigation (IMPORTANT — use social actions, not chat-only replies)");
        sb.AppendLine("When a player invites you to a location, set shouldRespond=true AND include the correct social action.");
        sb.AppendLine();
        sb.AppendLine("### Teleport to city/aetheryte");
        sb.AppendLine("Triggers: come to Limsa, meet me in Gridania, head to Ul'dah, tp to Kugane, visit Crystarium, etc.");
        sb.AppendLine("Use: social.teleport with aetheryteName (Limsa, Gridania, Ul'dah, Kugane, Crystarium, Sharlayan, Ishgard...).");
        sb.AppendLine("""
            Example — player: "come to Limsa":
            "social": { "action": "teleport", "aetheryteName": "Limsa" }
            """);
        sb.AppendLine();
        if (vnavAvailable)
        {
            sb.AppendLine("### Navigate to map flag");
            sb.AppendLine("When message has [Coords: ...] OR phrases: come here, meet at this flag, my location, this flag:");
            sb.AppendLine("You MUST use social.navigate — action=\"navigate\", no extra fields. Coords come from context.");
            sb.AppendLine("Do NOT reply with chat only when a flag is present and the player wants you to come.");
            sb.AppendLine("""
                Example — player sends flag + "come here":
                "social": { "action": "navigate" }
                """);
        }
        else
        {
            sb.AppendLine("### Map flag without vnavmesh");
            sb.AppendLine("With a flag but no vnavmesh: teleport to nearest aetheryte in that zone if you can.");
        }

        sb.AppendLine("Requires AllowSocialAutomation + AllowTeleport enabled in plugin settings.");
        sb.AppendLine();
    }

    private static void AppendChineseSocialRules(StringBuilder sb, bool socialEnabled, bool vnavAvailable, bool gearsetEnabled)
    {
        sb.AppendLine("## 社交行为 social");
        if (!socialEnabled)
        {
            sb.AppendLine("当前社交自动化未启用，social 必须始终为 null。");
        }
        else
        {
            sb.AppendLine("social 格式:");
            sb.AppendLine("- friend: 好友申请，需 targetName + targetWorld");
            sb.AppendLine("- trade: 交易，需 targetName + targetWorld；可选 itemName, itemQuantity, expectedGil");
            sb.AppendLine("- invite: 组队邀请，需 targetName + targetWorld（从聊天 玩家名@服务器名 获取）");
            sb.AppendLine("- target: 选中玩家");
            sb.AppendLine("- teleport: 传送，需 aetheryteName（水晶名或简称：海都/森都/沙都）");
            if (vnavAvailable)
            {
                sb.AppendLine("- navigate: 根据 flag 坐标传送并寻路（消息须含 [坐标: ...]）");
            }

            if (gearsetEnabled)
            {
                sb.AppendLine("- gearset: 切换装备组，需 gearsetName（名称部分匹配）或 gearsetSlot（UI 序号 1-based）");
                sb.AppendLine("  示例: 玩家说「切白魔」→ social.gearset(gearsetName=\"白魔\")");
                sb.AppendLine("  示例: 玩家说「换第 3 套外观」→ social.gearset(gearsetSlot=3)");
            }

            sb.AppendLine("仅在玩家明确请求或角色人设需要时才执行，不要频繁自动交易/传送/切套装。");
        }

        sb.AppendLine();
    }

    private static void AppendEnglishSocialRules(StringBuilder sb, bool socialEnabled, bool vnavAvailable, bool gearsetEnabled)
    {
        sb.AppendLine("## Social actions");
        if (!socialEnabled)
        {
            sb.AppendLine("Social automation is disabled — social must always be null.");
        }
        else
        {
            sb.AppendLine("social format:");
            sb.AppendLine("- friend: friend request — targetName + targetWorld");
            sb.AppendLine("- trade: trade — targetName + targetWorld; optional itemName, itemQuantity, expectedGil");
            sb.AppendLine("- invite: party invite — targetName + targetWorld from chat");
            sb.AppendLine("- target: select player");
            sb.AppendLine("- teleport: teleport — aetheryteName (name or shorthand)");
            if (vnavAvailable)
            {
                sb.AppendLine("- navigate: path to flag coords (message must include [Coords: ...])");
            }

            if (gearsetEnabled)
            {
                sb.AppendLine("- gearset: switch gearset — gearsetName (partial match) or gearsetSlot (1-based UI slot)");
                sb.AppendLine("  Example: \"switch to WHM\" → social.gearset(gearsetName=\"WHM\")");
                sb.AppendLine("  Example: \"use gearset 3\" → social.gearset(gearsetSlot=3)");
            }

            sb.AppendLine("Only use when the player clearly asks or persona requires it — do not spam trades/teleports/gearsets.");
        }

        sb.AppendLine();
    }

    private static void AppendChineseCharacterProfile(StringBuilder sb, CharacterProfileService profileService, PluginLanguage language)
    {
        sb.AppendLine("## 自身角色信息（回答职业/等级/套装问题时参考）");
        sb.AppendLine(profileService.BuildJobSummary(language));
        sb.AppendLine("装备组: " + profileService.BuildGearsetPromptList(language));
        sb.AppendLine();
    }

    private static void AppendEnglishCharacterProfile(StringBuilder sb, CharacterProfileService profileService, PluginLanguage language)
    {
        sb.AppendLine("## Your character (use when asked about jobs/levels/gearsets)");
        sb.AppendLine(profileService.BuildJobSummary(language));
        sb.AppendLine("Gearsets: " + profileService.BuildGearsetPromptList(language));
        sb.AppendLine();
    }

    private static void AppendChineseAetheryteList(StringBuilder sb, string aetheryteList)
    {
        sb.AppendLine("## 已解锁传送水晶（aetheryteName 部分匹配，支持简称）");
        sb.AppendLine("简称: 海都=利姆萨罗敏萨上层甲板, 森都=格里达尼亚新街, 沙都=乌尔达哈来生回廊");
        sb.AppendLine(aetheryteList);
        sb.AppendLine();
    }

    private static void AppendEnglishAetheryteList(StringBuilder sb, string aetheryteList)
    {
        sb.AppendLine("## Unlocked aetherytes (partial match on aetheryteName)");
        sb.AppendLine("Shorthand: Limsa, Gridania, Ul'dah map to their main city aetherytes.");
        sb.AppendLine(aetheryteList);
        sb.AppendLine();
    }
}
