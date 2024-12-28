// Licensed under the Apache License, Version 2.0 (the "License");

using System.Text;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using ZiggyCreatures.Caching.Fusion;

namespace GooeyBot.Modules;

public class SummaryModule(
    IFusionCache cache,
    MessageCache messageCache,
    ChatClient chatClient,
    ILogger<SummaryModule> logger,
    IOptions<OpenAiOptions> openAiOptions)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("tldr", "Summarizes recent chat")]
    [RequireUserPermission(GuildPermission.SendMessages)]
    public async Task Tldr()
    {
        if (Context.Channel.ChannelType != ChannelType.Text)
        {
            await FollowupAsync("This command only works in text channels", ephemeral: true);
            return;
        }

        MaybeValue<TldrCacheItem> maybeCacheItem = cache.TryGet<TldrCacheItem>($"tldr:{Context.Channel.Id}");

        if (maybeCacheItem.HasValue)
        {
            await FollowupAsync("I've already summarized too recently", ephemeral: true);
            return;
        }

        await DeferAsync();

        var cacheItem = await cache.GetOrSetAsync<TldrCacheItem>(
            $"tldr:{Context.Channel.Id}",
            (ctx, ct) =>
            {
                ctx.Options.SetDurationMin(15);
                return Summarize(ctx, Context.Interaction.Id, Context.Channel);
            });

        if (cacheItem.InteractionId != Context.Interaction.Id)
        {
            await FollowupAsync("I've already summarized too recently", ephemeral: true);
        }
        else
        {
            await FollowupAsync(cacheItem.Summary);
        }
    }

    private async Task<TldrCacheItem> Summarize(
        FusionCacheFactoryExecutionContext<TldrCacheItem> ctx,
        ulong interactionId,
        ISocketMessageChannel channel)
    {
        var sb = new StringBuilder();
        var messages = await messageCache.PopMessages(channel);

        if (messages.Count == 0)
        {
            return new TldrCacheItem
            {
                Timestamp = DateTimeOffset.Now,
                InteractionId = interactionId,
                Summary = "Nothing to summarize :sob:"
            };
        }

        foreach (var message in messages)
        {
            sb.AppendLine($"{message.Author}: {message.Content}");
        }

        var chat = sb.ToString();

        logger.LogInformation("Requesting summary for {Channel} with {Length} length", channel.Name, chat.Length);

        logger.LogDebug("Sending chat to OpenAI: {Chat}", chat);

        try
        {
            ChatCompletion completion = await chatClient.CompleteChatAsync(
            [
                new SystemChatMessage(openAiOptions.Value.Prompt),
                new UserChatMessage(chat),
            ], new ChatCompletionOptions
            {
                Temperature = 1,
                TopP = 1,
                FrequencyPenalty = 0,
                PresencePenalty = 0,
                MaxOutputTokenCount = 3000,
            });

            string? summary = completion.FinishReason switch
            {
                ChatFinishReason.Stop => completion.Content[0].Text[..Math.Min(completion.Content[0].Text.Length, 1_950)],
                ChatFinishReason.Length => "Incomplete output. Fix this.",
                ChatFinishReason.ContentFilter => "I couldn't summarize the chat because of a content filter :sob:",
                _ => $"Hit a {completion.FinishReason} issue"
            };

            logger.LogInformation("Got summary for {Channel}: {Summary}", channel.Name, summary);

            return new TldrCacheItem
            {
                Timestamp = DateTimeOffset.Now,
                InteractionId = interactionId,
                Summary = summary
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error summarizing {Channel}", channel.Name);

            await messageCache.ClearMessages(channel);

            return new TldrCacheItem
            {
                Timestamp = DateTimeOffset.Now,
                InteractionId = interactionId,
                Summary = "I couldn't summarize the chat. Probably hit a content filter :shrug:"
            };
        }
    }

    private record TldrCacheItem
    {
        public DateTimeOffset Timestamp { get; init; }
        public ulong InteractionId { get; init; }
        public string Summary { get; init; } = "";
    }
}