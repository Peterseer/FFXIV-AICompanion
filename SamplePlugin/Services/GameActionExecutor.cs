using System;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public static unsafe class GameActionExecutor
{
    public static void SendChat(AiChatAction chat)
    {
        if (string.IsNullOrWhiteSpace(chat.Message))
        {
            return;
        }

        ExecuteSlashCommand(BuildChatCommand(chat));
    }

    public static void SendStandardEmote(string emoteName)
    {
        if (string.IsNullOrWhiteSpace(emoteName))
        {
            return;
        }

        var normalized = emoteName.Trim().TrimStart('/').Split(' ')[0];
        ExecuteSlashCommand($"/{normalized} motion");
    }

    public static void SendCustomEmote(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ExecuteSlashCommand($"/em {SanitiseText(text.Trim())}");
    }

    public static void ChangeGearset(int enabledSlot)
    {
        if (enabledSlot <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(enabledSlot));
        }

        ExecuteSlashCommand($"/gearset change {enabledSlot}");
    }

    public static bool TryChangeGearset(int enabledSlot, out string? error)
    {
        error = null;
        try
        {
            ChangeGearset(enabledSlot);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void SendFriendRequest(string playerName, string? world = null)
    {
        var target = FormatPlayerReference(playerName, world);
        ExecuteSlashCommand($"/friendlist add {target}");
    }

    public static void InitiateTrade(string playerName, string? world = null)
    {
        TargetPlayerInWorld(playerName);
        ExecuteSlashCommand("/trade");
    }

    public static void InviteToParty(string playerName, string? world = null)
    {
        var target = FormatPlayerReference(playerName, world);
        ExecuteSlashCommand($"/invite {target}");
    }

    /// <summary>
    /// 游戏内所持品检索（/isearch），搜索背包/兵装库/雇员/挎包等全部位置，结果输出到聊天栏。
    /// </summary>
    public static void SearchInventoryItem(string itemName)
    {
        var name = SanitiseText(itemName.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("检索物品名为空。", nameof(itemName));
        }

        ExecuteSlashCommand($"/isearch {name}");
    }

    /// <summary>
    /// 通过斜杠命令选中目标。仅支持游戏内玩家名，不可使用 玩家名@服务器（tell 专用格式）。
    /// 优先使用 <see cref="PlayerTargeting.TrySetTarget"/>。
    /// </summary>
    public static void TargetPlayer(string playerName, string? world = null)
    {
        TargetPlayerInWorld(playerName);
    }

    public static void TargetPlayerInWorld(string playerName)
    {
        var name = playerName.Trim();
        if (PlayerIdentity.TryParseFullId(name, out var parsedName, out _))
        {
            name = parsedName;
        }

        name = SanitiseText(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("目标玩家名为空。", nameof(playerName));
        }

        ExecuteSlashCommand($"/target {name}");
    }

    public static void ExecuteSlashCommand(string command)
    {
        if (!command.StartsWith('/'))
        {
            throw new InvalidOperationException($"指令必须以 / 开头: {command}");
        }

        SendMessage(command);
    }

    public static void ResolveTellTarget(AiChatAction chat, ChatContextEntry trigger)
    {
        if (!string.IsNullOrWhiteSpace(chat.TargetName) && !string.IsNullOrWhiteSpace(chat.TargetWorld))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(chat.Target) &&
            PlayerIdentity.TryParseFullId(chat.Target, out var name, out var world))
        {
            chat.TargetName = name;
            chat.TargetWorld = world;
            return;
        }

        if (!string.IsNullOrWhiteSpace(chat.Target))
        {
            chat.TargetName = chat.Target;
        }

        if (string.IsNullOrWhiteSpace(chat.TargetName))
        {
            chat.TargetName = trigger.Sender;
        }

        if (string.IsNullOrWhiteSpace(chat.TargetWorld))
        {
            chat.TargetWorld = trigger.SenderWorld;
        }
    }

    private static string BuildChatCommand(AiChatAction chat)
    {
        var message = SanitiseText(chat.Message.Trim());
        return chat.Channel.ToLowerInvariant() switch
        {
            "say" or "s" => $"/s {message}",
            "shout" or "y" => $"/y {message}",
            "whisper" or "tell" or "w" => BuildTellCommand(chat, message),
            "fc" => $"/fc {message}",
            "ls" or "l" => $"/l {message}",
            "party" or "p" => $"/p {message}",
            "alliance" or "a" => $"/a {message}",
            "echo" or "e" => $"/echo {message}",
            _ => $"/s {message}",
        };
    }

    private static string BuildTellCommand(AiChatAction chat, string message)
    {
        var name = SanitiseText(chat.TargetName ?? string.Empty);
        var world = SanitiseText(chat.TargetWorld ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("私聊缺少目标玩家名。");
        }

        if (string.IsNullOrWhiteSpace(world))
        {
            return $"/w {name} {message}";
        }

        return $"/tell {name}@{world} {message}";
    }

    private static string FormatPlayerReference(string playerName, string? world)
    {
        var name = SanitiseText(playerName.Trim());
        if (string.IsNullOrWhiteSpace(world))
        {
            return name;
        }

        return $"{name}@{SanitiseText(world.Trim())}";
    }

    private static void SendMessage(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("message is empty", nameof(message));
        }

        if (bytes.Length > 500)
        {
            throw new ArgumentException("message is longer than 500 bytes", nameof(message));
        }

        if (message.Length != SanitiseText(message).Length)
        {
            throw new ArgumentException("message contained invalid characters", nameof(message));
        }

        SendMessageUnsafe(bytes);
    }

    private static void SendMessageUnsafe(byte[] message)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
        {
            throw new InvalidOperationException("UIModule 不可用。");
        }

        var mes = Utf8String.FromSequence(message);
        uiModule->ProcessChatBoxEntry(mes);
        mes->Dtor(true);
    }

    private static string SanitiseText(string text)
    {
        var uText = Utf8String.FromString(text);
        uText->SanitizeString((AllowedEntities)0x27F);
        var sanitised = uText->ToString();
        uText->Dtor(true);
        return sanitised;
    }
}
