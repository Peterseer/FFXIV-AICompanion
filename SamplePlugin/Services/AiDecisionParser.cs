using System;
using System.Text;
using System.Text.Json;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

/// <summary>
/// 将 AI 返回文本解析为 <see cref="AiDecision"/>，兼容 markdown 包裹与非标准 JSON。
/// </summary>
internal static class AiDecisionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
    };

    public static AiDecision? TryParse(string rawContent, ActivityLog log)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        var json = ExtractJsonObject(rawContent);
        if (string.IsNullOrWhiteSpace(json))
        {
            log.Warn($"AI 返回内容中未找到 JSON 对象: {Truncate(rawContent, 200)}");
            return null;
        }

        try
        {
            var decision = JsonSerializer.Deserialize<AiDecision>(json, JsonOptions);
            if (decision != null)
            {
                return decision;
            }
        }
        catch (JsonException)
        {
            // 回退到宽松解析
        }

        try
        {
            return ParseLenient(json);
        }
        catch (Exception ex)
        {
            log.Error($"AI JSON 解析失败: {ex.Message} | 片段: {Truncate(json, 300)}");
            return null;
        }
    }

    private static string ExtractJsonObject(string content)
    {
        content = content.Trim().TrimStart('\uFEFF');

        if (content.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = content.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var endFence = content.IndexOf("```", firstNewline + 1, StringComparison.Ordinal);
                if (endFence > firstNewline)
                {
                    content = content[(firstNewline + 1)..endFence].Trim();
                }
            }
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return content[start..(end + 1)];
    }

    private static AiDecision ParseLenient(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("根节点不是 JSON 对象。");
        }

        if (root.TryGetProperty("decision", out var wrapped) && wrapped.ValueKind == JsonValueKind.Object)
        {
            root = wrapped;
        }
        else if (root.TryGetProperty("result", out wrapped) && wrapped.ValueKind == JsonValueKind.Object)
        {
            root = wrapped;
        }

        var decision = new AiDecision
        {
            ShouldRespond = ReadBool(root, "shouldRespond"),
            Reason = ReadString(root, "reason") ?? string.Empty,
            Emote = ReadString(root, "emote"),
            CustomEmote = ReadString(root, "customEmote"),
        };

        if (root.TryGetProperty("chat", out var chatEl) && chatEl.ValueKind == JsonValueKind.Object)
        {
            decision.Chat = new AiChatAction
            {
                Channel = ReadString(chatEl, "channel") ?? "say",
                TargetName = ReadString(chatEl, "targetName"),
                TargetWorld = ReadString(chatEl, "targetWorld"),
                Target = ReadString(chatEl, "target"),
                Message = ReadString(chatEl, "message") ?? string.Empty,
            };
        }

        if (root.TryGetProperty("social", out var socialEl) && socialEl.ValueKind == JsonValueKind.Object)
        {
            decision.Social = new AiSocialAction
            {
                Action = ReadString(socialEl, "action") ?? string.Empty,
                TargetName = ReadString(socialEl, "targetName"),
                TargetWorld = ReadString(socialEl, "targetWorld"),
                AetheryteName = ReadString(socialEl, "aetheryteName"),
                ItemName = ReadString(socialEl, "itemName"),
                ItemQuantity = ReadInt(socialEl, "itemQuantity"),
                ExpectedGil = ReadLong(socialEl, "expectedGil"),
                GearsetName = ReadString(socialEl, "gearsetName"),
                GearsetSlot = ReadInt(socialEl, "gearsetSlot"),
            };
        }

        return decision;
    }

    private static string? ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static bool ReadBool(JsonElement parent, string name, bool defaultValue = false)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : defaultValue,
            JsonValueKind.Number => value.TryGetInt32(out var n) && n != 0,
            _ => defaultValue,
        };
    }

    private static int ReadInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
        {
            return n;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out n))
        {
            return n;
        }

        return 0;
    }

    private static long ReadLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
        {
            return n;
        }

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out n))
        {
            return n;
        }

        return 0;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}
