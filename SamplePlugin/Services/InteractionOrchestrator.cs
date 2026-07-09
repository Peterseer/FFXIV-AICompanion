using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using SamplePlugin.Models;

namespace SamplePlugin.Services;

public sealed class InteractionOrchestrator : IDisposable
{
    private readonly Configuration configuration;
    private readonly AiService aiService;
    private readonly EmoteRegistry emoteRegistry;
    private readonly RuntimeControls runtimeControls;
    private readonly OutboundEchoGuard echoGuard;
    private readonly SocialActionService socialActionService;
    private readonly TradeIntentConfirmationService tradeIntentConfirmationService;
    private readonly InteractionSafetyService safetyService;
    private readonly IFramework framework;
    private readonly ActivityLog log;
    private readonly ConcurrentQueue<ChatContextEntry> eventQueue = new();
    private readonly List<ChatContextEntry> contextHistory = [];
    private readonly object historyLock = new();
    private readonly CancellationTokenSource cts = new();

    private DateTime lastActionTime = DateTime.MinValue;
    private bool isProcessing;
    private string lastDecisionSummary = "尚无决策";

    public InteractionOrchestrator(
        Configuration configuration,
        AiService aiService,
        EmoteRegistry emoteRegistry,
        RuntimeControls runtimeControls,
        OutboundEchoGuard echoGuard,
        SocialActionService socialActionService,
        TradeIntentConfirmationService tradeIntentConfirmationService,
        InteractionSafetyService safetyService,
        IFramework framework,
        ActivityLog log)
    {
        this.configuration = configuration;
        this.aiService = aiService;
        this.emoteRegistry = emoteRegistry;
        this.runtimeControls = runtimeControls;
        this.echoGuard = echoGuard;
        this.socialActionService = socialActionService;
        this.tradeIntentConfirmationService = tradeIntentConfirmationService;
        this.safetyService = safetyService;
        this.framework = framework;
        this.log = log;

        _ = ProcessLoopAsync(cts.Token);
    }

    public string LastDecisionSummary => lastDecisionSummary;

    public RuntimeControls RuntimeControls => runtimeControls;

    public IReadOnlyList<ChatContextEntry> GetContextSnapshot()
    {
        lock (historyLock)
        {
            return contextHistory.ToArray();
        }
    }

    public void Enqueue(ChatContextEntry entry)
    {
        if (!configuration.Enabled || entry.IsAdminCommand)
        {
            return;
        }

        lock (historyLock)
        {
            contextHistory.Add(entry);
            TrimHistory();
        }

        eventQueue.Enqueue(entry);
    }

    public async Task<bool> TestAiConnectionAsync()
    {
        return await aiService.TestConnectionAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (eventQueue.TryDequeue(out var trigger))
                {
                    await ProcessEventAsync(trigger, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.Error($"处理循环异常: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessEventAsync(ChatContextEntry trigger, CancellationToken cancellationToken)
    {
        if (isProcessing)
        {
            eventQueue.Enqueue(trigger);
            return;
        }

        var cooldown = TimeSpan.FromSeconds(Math.Max(1, configuration.ResponseCooldownSeconds));
        var elapsed = DateTime.UtcNow - lastActionTime;
        if (elapsed < cooldown)
        {
            var waitMs = (int)(cooldown - elapsed).TotalMilliseconds;
            log.Info($"冷却中，等待 {waitMs}ms...");
            await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
        }

        isProcessing = true;
        try
        {
            AiSocialAction? confirmedTrade = null;
            var handledTradeIntent = false;
            if (RuntimeControls.CanOfferTradeInteraction(configuration, runtimeControls))
            {
                await framework.RunOnFrameworkThread(() =>
                {
                    handledTradeIntent = tradeIntentConfirmationService.TryHandleReply(trigger, out confirmedTrade)
                                         || tradeIntentConfirmationService.TryRequestFromMention(trigger);
                }).ConfigureAwait(false);
            }

            if (handledTradeIntent)
            {
                if (confirmedTrade != null)
                {
                    await framework.RunOnFrameworkThread(() => ExecuteSocialAction(confirmedTrade, trigger)).ConfigureAwait(false);
                    lastActionTime = DateTime.UtcNow;
                }

                lastDecisionSummary = "trade intent confirmation handled";
                return;
            }

            IReadOnlyList<ChatContextEntry> snapshot;
            lock (historyLock)
            {
                snapshot = contextHistory.ToArray();
            }

            log.Info($"AI 思考中: [{trigger.Channel}] {trigger.SenderFullId}...");
            var decision = await aiService.GetDecisionAsync(snapshot, trigger, cancellationToken).ConfigureAwait(false);
            if (decision == null)
            {
                lastDecisionSummary = "AI 未返回有效决策";
                return;
            }

            decision = SanitizeDecision(decision, trigger);

            lastDecisionSummary = decision.Social != null
                ? $"shouldRespond={decision.ShouldRespond}, social={decision.Social.Action}, reason={decision.Reason}"
                : $"shouldRespond={decision.ShouldRespond}, reason={decision.Reason}";
            log.Info($"AI 决策: {lastDecisionSummary}");

            if (!decision.ShouldRespond)
            {
                return;
            }

            await framework.RunOnFrameworkThread(() => ExecuteDecision(decision, trigger)).ConfigureAwait(false);
            lastActionTime = DateTime.UtcNow;
        }
        finally
        {
            isProcessing = false;
        }
    }

    private AiDecision SanitizeDecision(AiDecision decision, ChatContextEntry trigger)
    {
        if (decision.Chat?.Channel is "whisper" or "tell" or "w")
        {
            GameActionExecutor.ResolveTellTarget(decision.Chat, trigger);
        }

        if (!string.IsNullOrWhiteSpace(decision.Emote) && !emoteRegistry.IsOwned(decision.Emote))
        {
            log.Warn($"AI 选择了未拥有的情感动作: {decision.Emote}，已忽略。");
            decision.Emote = null;
        }

        // emote 与 customEmote 同时存在时优先标准动作
        if (!string.IsNullOrWhiteSpace(decision.Emote) && !string.IsNullOrWhiteSpace(decision.CustomEmote))
        {
            decision.CustomEmote = null;
        }

        if (decision.Social != null && !configuration.AllowSocialAutomation)
        {
            log.Warn("社交自动化未在设置中启用，已忽略 AI 社交行为。");
            decision.Social = null;
        }

        return decision;
    }

    private void ExecuteSocialAction(AiSocialAction action, ChatContextEntry trigger)
    {
        if (socialActionService.TryExecute(action, trigger, out var socialMsg))
        {
            log.Info($"绀句氦琛屼负: {socialMsg}");
        }
        else if (!string.IsNullOrEmpty(socialMsg))
        {
            log.Warn($"绀句氦琛屼负鏈墽琛? {socialMsg}");
        }
    }

    private void ExecuteDecision(AiDecision decision, ChatContextEntry trigger)
    {
        try
        {
            var canChat = runtimeControls.ChatEnabled && configuration.Enabled;
            var canEmote = runtimeControls.EmoteEnabled && configuration.AllowEmoteResponse;

            if (canChat && decision.Chat != null && !string.IsNullOrWhiteSpace(decision.Chat.Message))
            {
                var chat = decision.Chat;
                if (!safetyService.CanSendChat(chat))
                {
                    return;
                }

                if (chat.Channel is "whisper" or "tell" or "w")
                {
                    GameActionExecutor.ResolveTellTarget(chat, trigger);
                }

                var targetId = string.IsNullOrEmpty(chat.TargetWorld)
                    ? chat.TargetName ?? string.Empty
                    : $"{chat.TargetName}@{chat.TargetWorld}";

                echoGuard.Record(chat.Channel, chat.Message, targetId);
                GameActionExecutor.SendChat(chat);
                log.Info($"已发送聊天 [{chat.Channel}] -> {chat.TargetName}@{chat.TargetWorld}: {chat.Message}");

                lock (historyLock)
                {
                    contextHistory.Add(new ChatContextEntry
                    {
                        Channel = chat.Channel,
                        Sender = "我",
                        Message = chat.Message,
                        IsLocalPlayer = true,
                    });
                    TrimHistory();
                }
            }
            else if (decision.Chat != null && !string.IsNullOrWhiteSpace(decision.Chat.Message))
            {
                log.Info("对话输出已被管理员禁用，跳过聊天。");
            }

            if (canEmote && !string.IsNullOrWhiteSpace(decision.Emote))
            {
                GameActionExecutor.SendStandardEmote(decision.Emote);
                log.Info($"已执行情感动作: /{decision.Emote.TrimStart('/')} motion");
            }
            else if (!string.IsNullOrWhiteSpace(decision.Emote))
            {
                log.Info("情感动作已被管理员禁用，跳过。");
            }

            if (canEmote && !string.IsNullOrWhiteSpace(decision.CustomEmote))
            {
                echoGuard.Record("emote", decision.CustomEmote);
                GameActionExecutor.SendCustomEmote(decision.CustomEmote);
                log.Info($"已执行自定义情感: /em {decision.CustomEmote}");
            }

            if (decision.Social != null && !string.IsNullOrWhiteSpace(decision.Social.Action))
            {
                if (!runtimeControls.SocialEnabled)
                {
                    log.Info("社交行为已被管理员禁用，跳过。");
                }
                else if (!configuration.AllowTrade)
                {
                    log.Info("交易功能未在设置中启用，跳过。");
                }
                else if (!runtimeControls.ChatEnabled)
                {
                    log.Info("对话输出已被管理员禁用，跳过交易确认询问。");
                }
                else if (tradeIntentConfirmationService.TryRequestForAction(decision.Social, trigger))
                {
                    log.Info("Preset trade confirmation requested.");
                }
                else if (socialActionService.TryExecute(decision.Social, trigger, out var socialMsg))
                {
                    log.Info($"社交行为: {socialMsg}");
                }
                else if (!string.IsNullOrEmpty(socialMsg))
                {
                    log.Warn($"社交行为未执行: {socialMsg}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"执行决策失败: {ex.Message}");
        }
    }

    private void TrimHistory()
    {
        var max = Math.Max(5, configuration.MaxContextMessages);
        if (contextHistory.Count > max)
        {
            contextHistory.RemoveRange(0, contextHistory.Count - max);
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
