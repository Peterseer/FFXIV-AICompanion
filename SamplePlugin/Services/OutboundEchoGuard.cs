using System;
using System.Collections.Generic;
using System.Linq;

namespace SamplePlugin.Services;

/// <summary>
/// 记录近期由插件发出的消息，用于过滤聊天回显，避免自我触发循环。
/// </summary>
public sealed class OutboundEchoGuard
{
    private readonly object sync = new();
    private readonly List<SentEntry> recent = [];

    private const int MaxRecords = 48;

    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(20);

    public void Record(string channel, string message, string? targetFullId = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (sync)
        {
            Prune();
            recent.Add(new SentEntry
            {
                Channel = NormalizeChannel(channel),
                Message = message.Trim(),
                TargetFullId = targetFullId ?? string.Empty,
                Timestamp = DateTime.UtcNow,
            });

            if (recent.Count > MaxRecords)
            {
                recent.RemoveRange(0, recent.Count - MaxRecords);
            }
        }
    }

    public bool IsLikelyEcho(string channel, string message, string senderFullId)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        lock (sync)
        {
            Prune();
            var normalizedChannel = NormalizeChannel(channel);
            var normalizedMessage = message.Trim();

            return recent.Any(entry =>
            {
                if (!entry.Message.Equals(normalizedMessage, StringComparison.Ordinal))
                {
                    return false;
                }

                if (!entry.Channel.Equals(normalizedChannel, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 私聊回显的发送者字段不稳定，只要内容匹配即视为回显
                if (normalizedChannel == "tell")
                {
                    return true;
                }

                return string.IsNullOrEmpty(entry.TargetFullId) ||
                       string.IsNullOrEmpty(senderFullId) ||
                       entry.TargetFullId.Equals(senderFullId, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    private static string NormalizeChannel(string channel) => channel.ToLowerInvariant() switch
    {
        "whisper" or "w" => "tell",
        _ => channel.ToLowerInvariant(),
    };

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - EchoWindow;
        recent.RemoveAll(r => r.Timestamp < cutoff);
    }

    private sealed class SentEntry
    {
        public string Channel { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public string TargetFullId { get; init; } = string.Empty;

        public DateTime Timestamp { get; init; }
    }
}
