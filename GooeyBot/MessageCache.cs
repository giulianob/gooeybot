// Licensed under the Apache License, Version 2.0 (the "License");

using System.Diagnostics;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using ZiggyCreatures.Caching.Fusion;

namespace GooeyBot;

public partial class MessageCache(
    IFusionCache fusionCache,
    ILogger<MessageCache> logger)
{
    private record CacheItem
    {
        public required ulong Id { get; init; }
        public required string Name { get; init; }
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public LinkedList<Message> Messages { get; } = new();
        public int Length { get; set; } = 0;
    }

    public class Message
    {
        public required ulong Id { get; init; }
        public required string Author { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required string Content { get; init; }
    }

    public async Task Add(SocketMessage message)
    {
        if (message.Channel.ChannelType != ChannelType.Text)
        {
            return;
        }

        var cacheItem = await GetCacheItem(message.Channel);

        await cacheItem.Semaphore.WaitAsync();
        try
        {
            // If the factory was just called its possible this message is already in the cache
            // this isnt a perfect check but its good enough
            if (cacheItem.Messages.Last?.Value.Id == message.Id)
            {
                return;
            }

            if (ConvertMessage(message) is { } cacheMessage)
            {
                cacheItem.Messages.AddLast(cacheMessage);
                cacheItem.Length += cacheMessage.Content.Length;

                logger.LogInformation("New message for {Channel} (Length is {CacheItemLength}). {Author}: {Content}", cacheItem.Name, cacheItem.Length, cacheMessage.Author, cacheMessage.Content);

                TrimMessages(cacheItem, since: TimeSpan.FromHours(3));
            }
        }
        finally
        {
            cacheItem.Semaphore.Release();
        }
    }

    public async ValueTask ClearMessages(ISocketMessageChannel channel)
    {
        var cacheItem = await GetCacheItem(channel);

        await cacheItem.Semaphore.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cacheItem.Messages.Clear();
        }
        finally
        {
            cacheItem.Semaphore.Release();
        }
    }

    private void TrimMessages(CacheItem cacheItem, TimeSpan since)
    {
        Debug.Assert(cacheItem.Semaphore.CurrentCount == 0);

        var offset = DateTimeOffset.UtcNow.Subtract(since);

        var trimmed = 0;
        int initialSize = cacheItem.Length;
        while (
            cacheItem.Messages.First != null &&
            (cacheItem.Length > 7_500 ||
             cacheItem.Messages.First.Value.Timestamp < offset))
        {
            cacheItem.Length -= cacheItem.Messages.First.Value.Content.Length;
            cacheItem.Messages.RemoveFirst();
            trimmed++;
        }

        if (trimmed > 0)
        {
            logger.LogInformation("Trimmed {Count} messages from cache for channel {Channel}. Size reduced from {InitialSize} to {FinalSize}", trimmed, cacheItem.Name, initialSize, cacheItem.Length);
        }
    }

    public async ValueTask<List<Message>> PopMessages(ISocketMessageChannel channel)
    {
        var cacheItem = await GetCacheItem(channel);

        await cacheItem.Semaphore.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var list = new List<Message>(cacheItem.Messages.Count);
            for (LinkedListNode<Message>? message = cacheItem.Messages.First; message != null; message = message.Next)
            {
                list.Add(message.Value);
            }

            // Remove older messages when getting a summary
            TrimMessages(cacheItem, TimeSpan.FromMinutes(30));

            return list;
        }
        finally
        {
            cacheItem.Semaphore.Release();
        }
    }

    private ValueTask<CacheItem> GetCacheItem(ISocketMessageChannel channel)
    {
        if (channel.ChannelType != ChannelType.Text)
        {
            throw new InvalidOperationException("Channel must be a text channel");
        }

        return fusionCache.GetOrSetAsync<CacheItem>($"messagecache:{channel.Id}", LoadMessages);

        async Task<CacheItem> LoadMessages(FusionCacheFactoryExecutionContext<CacheItem> ctx, CancellationToken ct)
        {
            var cacheItem = new CacheItem
            {
                Id = channel.Id,
                Name = channel.Name
            };

            await cacheItem.Semaphore.WaitAsync(ct); // This is only necessary because TrimMessages checks the semaphore
            try
            {
                logger.LogInformation("Downloading messages for {Channel}", channel.Name);

                await foreach (IReadOnlyCollection<IMessage>? batch in channel.GetMessagesAsync(limit: 250).WithCancellation(ct))
                {
                    foreach (var message in batch)
                    {
                        if (ConvertMessage(message) is { } cacheMessage)
                        {
                            cacheItem.Messages.AddFirst(cacheMessage);
                            cacheItem.Length += cacheMessage.Content.Length;
                        }
                    }
                }

                TrimMessages(cacheItem, since: TimeSpan.FromHours(3));

                logger.LogInformation("Got {Count} messages for {Channel}. Cache item length is {CacheItemLength}", cacheItem.Messages.Count, cacheItem.Name, cacheItem.Length);

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var content = string.Join(Environment.NewLine, cacheItem.Messages.Select(m => $"{m.Author}: {m.Content}"));
                    logger.LogDebug("Got chat messages for {Channel}:\n{Messages}", channel.Name, content);
                }

                ctx.Options.SetDurationInfinite();
            }
            finally
            {
                cacheItem.Semaphore.Release();
            }

            return cacheItem;
        }
    }

    private Message? ConvertMessage(IMessage message)
    {
        if (message.Author.IsBot || string.IsNullOrWhiteSpace(message.CleanContent))
        {
            return null;
        }

        string content = message.CleanContent;

        content = StripUrls().Replace(content, ""); // Strip urls to shorten the messages

        return new Message
        {
            Id = message.Id,
            Author = message.Author switch
            {
                IGuildUser guildUser => guildUser.Nickname,
                _ => message.Author.GlobalName ?? message.Author.Username
            },
            Timestamp = message.Timestamp,
            Content = content,
        };
    }

    [GeneratedRegex(@"http[s]?://[^\s]+")]
    private static partial Regex StripUrls();
}