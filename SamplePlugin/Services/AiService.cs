using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class AiService : IDisposable
{
    private readonly Configuration configuration;
    private readonly EmoteRegistry emoteRegistry;
    private readonly AetheryteResolver aetheryteResolver;
    private readonly NavmeshIpc navmeshIpc;
    private readonly PresetTradeService presetTradeService;
    private readonly ActivityLog log;
    private readonly HttpClient httpClient = new();

    public AiService(
        Configuration configuration,
        EmoteRegistry emoteRegistry,
        AetheryteResolver aetheryteResolver,
        NavmeshIpc navmeshIpc,
        PresetTradeService presetTradeService,
        ActivityLog log)
    {
        this.configuration = configuration;
        this.emoteRegistry = emoteRegistry;
        this.aetheryteResolver = aetheryteResolver;
        this.navmeshIpc = navmeshIpc;
        this.presetTradeService = presetTradeService;
        this.log = log;
    }

    public async Task<AiDecision?> GetDecisionAsync(
        IReadOnlyList<ChatContextEntry> context,
        ChatContextEntry trigger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            log.Warn("未配置 API Key，跳过 AI 请求。");
            return null;
        }

        var messages = BuildMessages(context, trigger);
        var requestBody = new
        {
            model = configuration.Model,
            temperature = configuration.Temperature,
            response_format = new { type = "json_object" },
            messages,
        };

        var baseUrl = configuration.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                log.Error($"AI API 错误 ({(int)response.StatusCode}): {Truncate(body, 200)}");
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            var content = ExtractAssistantContent(doc.RootElement);
            if (string.IsNullOrWhiteSpace(content))
            {
                log.Warn($"AI 返回空内容或无法识别响应格式: {Truncate(body, 200)}");
                return null;
            }

            var decision = AiDecisionParser.TryParse(content, log);
            if (decision == null)
            {
                log.Warn($"AI 决策解析失败，原始内容: {Truncate(content, 200)}");
            }

            return decision;
        }
        catch (HttpRequestException ex)
        {
            log.Error($"AI 网络请求失败: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException)
        {
            log.Warn("AI 请求已取消或超时。");
            return null;
        }
        catch (Exception ex)
        {
            log.Error($"AI 请求失败: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            log.Warn("未配置 API Key。");
            return false;
        }

        var baseUrl = configuration.ApiBaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model = configuration.Model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 5,
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                log.Info("AI 连接测试成功。");
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            log.Error($"AI 连接测试失败 ({(int)response.StatusCode}): {Truncate(body, 200)}");
            return false;
        }
        catch (Exception ex)
        {
            log.Error($"AI 连接测试异常: {ex.Message}");
            return false;
        }
    }

    private List<object> BuildMessages(IReadOnlyList<ChatContextEntry> context, ChatContextEntry trigger)
    {
        var systemPrompt = AiPromptBuilder.BuildSystemPrompt(configuration, emoteRegistry, aetheryteResolver, navmeshIpc, presetTradeService);
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
        };

        foreach (var entry in context)
        {
            if (entry.IsAdminCommand)
            {
                continue;
            }

            var prefix = entry.IsEmote ? "[情感动作]" : $"[{entry.Channel}]";
            var role = entry.IsLocalPlayer ? "assistant" : "user";
            var who = string.IsNullOrEmpty(entry.SenderFullId) ? entry.Sender : entry.SenderFullId;
            var content = $"{prefix} {who}: {entry.Message}";
            if (!string.IsNullOrEmpty(entry.InventoryHint))
            {
                content += $" [背包: {entry.InventoryHint}]";
            }

            if (entry.HasMapLink)
            {
                content += $" [坐标: {entry.MapPlaceName} {entry.MapCoords}]";
            }

            messages.Add(new { role, content });
        }

        var triggerWho = string.IsNullOrEmpty(trigger.SenderFullId) ? trigger.Sender : trigger.SenderFullId;
        var triggerPrefix = trigger.IsEmote ? "[情感动作-触发]" : $"[{trigger.Channel}-触发]";
        var triggerContent = $"{triggerPrefix} {triggerWho}: {trigger.Message}";
        if (!string.IsNullOrEmpty(trigger.InventoryHint))
        {
            triggerContent += $" [背包: {trigger.InventoryHint}]";
        }

        if (trigger.HasMapLink)
        {
            triggerContent += $" [坐标: {trigger.MapPlaceName} {trigger.MapCoords}]";
        }

        messages.Add(new { role = "user", content = triggerContent });

        return messages;
    }

    private static string? ExtractAssistantContent(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Null => null,
            _ => content.GetRawText(),
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
