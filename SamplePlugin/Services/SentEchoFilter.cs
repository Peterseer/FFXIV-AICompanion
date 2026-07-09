using System;
using System.Collections.Generic;

namespace SamplePlugin.Services;

/// <summary>
/// 防止插件自己发出的聊天回显再次触发 AI。
/// </summary>
public sealed class SentEchoFilter
{
    private readonly object sync = new();
    private readonly List<SentRecord> recent = [];
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(8);

    public void Record(string channel, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (sync)
        {
            Prune();
            recent.Add(new SentRecord
            {
                Channel = channel,
                Message = message.Trim(),
                SentAt = DateTime.UtcNow,
            });
        }
    }

    public bool IsEcho(string channel, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        lock (sync)
        {
            Prune();
            var normalized = message.Trim();
            foreach (var record in recent)
            {
                if (record.Channel == channel &&
                    string.Equals(record.Message, normalized, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Window;
        recent.RemoveAll(r => r.SentAt < cutoff);
    }

    private sealed class SentRecord
    {
        public string Channel { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;

        public DateTime SentAt { get; init; }
    }
}
