using System;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class AdminCommandHandler
{
    private readonly Configuration configuration;
    private readonly RuntimeControls runtimeControls;
    private readonly SocialActionService socialActionService;
    private readonly NavmeshIpc navmeshIpc;
    private readonly ActivityLog log;

    public AdminCommandHandler(
        Configuration configuration,
        RuntimeControls runtimeControls,
        SocialActionService socialActionService,
        NavmeshIpc navmeshIpc,
        ActivityLog log)
    {
        this.configuration = configuration;
        this.runtimeControls = runtimeControls;
        this.socialActionService = socialActionService;
        this.navmeshIpc = navmeshIpc;
        this.log = log;
    }

    public bool TryHandle(ChatContextEntry entry)
    {
        if (entry.Channel != "tell" || entry.IsLocalPlayer)
        {
            return false;
        }

        if (!IsAdmin(entry.SenderFullId, entry.Sender, entry.SenderWorld))
        {
            return false;
        }

        var raw = entry.Message.Trim();
        var cmd = raw.ToLowerInvariant();

        if (TryHandleSocialAdmin(cmd, raw, out var socialMsg))
        {
            log.Info($"[管理员] {socialMsg}");
            return true;
        }

        switch (cmd)
        {
            case "help":
                log.Info("[管理员] 指令: emote/chat/social on|off, status, reset, help");
                log.Info("[管理员] 社交: friend/trade/invite/target 玩家@服, tp/teleport 水晶名, gs/gearset 名称或序号, nav stop");
                return true;
            case "nav stop":
            case "navstop":
                navmeshIpc.Stop();
                log.Info("[管理员] 已停止 vnav 寻路。");
                return true;
            case "emote off":
                runtimeControls.EmoteEnabled = false;
                log.Info($"[管理员] 已禁用情感动作 ({entry.SenderFullId})");
                return true;
            case "emote on":
                runtimeControls.EmoteEnabled = true;
                log.Info($"[管理员] 已启用情感动作 ({entry.SenderFullId})");
                return true;
            case "chat off":
                runtimeControls.ChatEnabled = false;
                log.Info($"[管理员] 已禁用对话输出 ({entry.SenderFullId})");
                return true;
            case "chat on":
                runtimeControls.ChatEnabled = true;
                log.Info($"[管理员] 已启用对话输出 ({entry.SenderFullId})");
                return true;
            case "social off":
                runtimeControls.SocialEnabled = false;
                log.Info($"[管理员] 已禁用社交行为 ({entry.SenderFullId})");
                return true;
            case "social on":
                runtimeControls.SocialEnabled = true;
                log.Info($"[管理员] 已启用社交行为 ({entry.SenderFullId})");
                return true;
            case "status":
                log.Info($"[管理员] 当前状态: {runtimeControls.StatusSummary}");
                return true;
            case "reset":
                runtimeControls.Reset();
                log.Info($"[管理员] 已重置运行时控制 ({entry.SenderFullId})");
                return true;
            default:
                return false;
        }
    }

    private bool TryHandleSocialAdmin(string cmdLower, string raw, out string message)
    {
        message = string.Empty;
        string? action = null;
        string? args = null;

        if (cmdLower.StartsWith("friend "))
        {
            action = "friend";
            args = raw[7..].Trim();
        }
        else if (cmdLower.StartsWith("trade "))
        {
            action = "trade";
            args = raw[6..].Trim();
        }
        else if (cmdLower.StartsWith("invite "))
        {
            action = "invite";
            args = raw[7..].Trim();
        }
        else if (cmdLower.StartsWith("target "))
        {
            action = "target";
            args = raw[7..].Trim();
        }
        else if (cmdLower.StartsWith("tp "))
        {
            action = "teleport";
            args = raw[3..].Trim();
        }
        else if (cmdLower.StartsWith("teleport "))
        {
            action = "teleport";
            args = raw[9..].Trim();
        }
        else if (cmdLower.StartsWith("gearset ") || cmdLower.StartsWith("gs "))
        {
            action = "gearset";
            args = cmdLower.StartsWith("gearset ") ? raw[8..].Trim() : raw[3..].Trim();
        }

        else if (cmdLower.StartsWith("navigate ") || cmdLower.StartsWith("nav ") || cmdLower.StartsWith("goto "))
        {
            action = "navigate";
            if (cmdLower.StartsWith("navigate "))
            {
                args = raw[9..].Trim();
            }
            else if (cmdLower.StartsWith("goto "))
            {
                args = raw[5..].Trim();
            }
            else
            {
                args = raw[4..].Trim();
            }
        }

        if (action == null)
        {
            return false;
        }

        runtimeControls.SocialEnabled = true;
        var social = new AiSocialAction { Action = action };
        if (action is "teleport" or "tp")
        {
            social.AetheryteName = args ?? string.Empty;
        }
        else if (action == "gearset")
        {
            if (int.TryParse(args, out var slot) && slot > 0)
            {
                social.GearsetSlot = slot;
            }
            else
            {
                social.GearsetName = args;
            }
        }
        else if (PlayerIdentity.TryParseFullId(args ?? string.Empty, out var name, out var world))
        {
            social.TargetName = name;
            social.TargetWorld = world;
        }
        else
        {
            social.TargetName = args;
        }

        return socialActionService.TryExecuteAdmin(social, out message);
    }

    private bool IsAdmin(string fullId, string name, string world)
    {
        var admin = configuration.AdminId.Trim();
        if (string.IsNullOrEmpty(admin))
        {
            return false;
        }

        if (string.Equals(fullId, admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (PlayerIdentity.TryParseFullId(admin, out var adminName, out var adminWorld))
        {
            return string.Equals(name, adminName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(world, adminWorld, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(name, admin, StringComparison.OrdinalIgnoreCase);
    }
}
