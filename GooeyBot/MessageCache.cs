// Licensed under the Apache License, Version 2.0 (the "License");

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Discord.WebSocket;
using ZiggyCreatures.Caching.Fusion;

namespace GooeyBot;

public partial class MessageCache
{
    private const int MessageCacheLength = 7_500;

    private readonly Channel<SocketMessage> _messageChannel =
        Channel.CreateUnbounded<SocketMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly IFusionCache _fusionCache;
    private readonly ILogger<MessageCache> _logger;

    private sealed class CacheItem
    {
        public required ulong Id { get; init; }
        public required string Name { get; init; }
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public LinkedList<Message> Messages { get; } = new();
        public int Length { get; set; } = 0;
    }

    public sealed class Message
    {
        public required ulong Id { get; init; }
        public required ulong AuthorId { get; init; }
        public required string AuthorDisplayName { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required string Content { get; init; }
        public ulong? ReplyToMessageId { get; set; }

        public override string ToString() => $"{Id.ToBase64()} {(ReplyToMessageId is not null ? $"(reply to {ReplyToMessageId.Value.ToBase64()})" : "")} {AuthorDisplayName} : {Content}";
    }

    public MessageCache(IFusionCache fusionCache, ILogger<MessageCache> logger)
    {
        _fusionCache = fusionCache;
        _logger = logger;

        _ = Task.Run(() => BackgroundChannelProcessor());
    }

    private async Task BackgroundChannelProcessor()
    {
        _logger.LogInformation("Starting message cache channel reader");

        await foreach (var message in _messageChannel.Reader.ReadAllAsync())
        {
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

                if (await ConvertMessage(message) is { } cacheMessage)
                {
                    cacheItem.Messages.AddLast(cacheMessage);
                    cacheItem.Length += cacheMessage.Content.Length;

                    TrimMessagesUnsafe(cacheItem, since: TimeSpan.FromHours(3));

                    _logger.LogInformation("New message for {Channel} (MessageCount={MessageCount}, Length={CacheItemLength}). {Message}", cacheItem.Name, cacheItem.Messages.Count, cacheItem.Length, cacheMessage.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding message to cache for {Channel}", cacheItem.Name);
            }
            finally
            {
                cacheItem.Semaphore.Release();
            }
        }
    }

    public async Task Add(SocketMessage message)
    {
        if (message.Channel.ChannelType != ChannelType.Text)
        {
            return;
        }

        await _messageChannel.Writer.WriteAsync(message);
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

    private void TrimMessagesUnsafe(CacheItem cacheItem, TimeSpan since)
    {
        Debug.Assert(cacheItem.Semaphore.CurrentCount == 0);

        var offset = DateTimeOffset.UtcNow.Subtract(since);

        var trimmed = 0;
        int initialSize = cacheItem.Length;
        while (
            cacheItem.Messages.First != null &&
            (cacheItem.Length > MessageCacheLength ||
             cacheItem.Messages.First.Value.Timestamp < offset))
        {
            cacheItem.Length -= cacheItem.Messages.First.Value.Content.Length;
            cacheItem.Messages.RemoveFirst();
            trimmed++;
        }

        if (trimmed > 0)
        {
            _logger.LogInformation("Trimmed {Count} messages from cache for channel {Channel}. Size reduced from {InitialSize} to {FinalSize}", trimmed, cacheItem.Name, initialSize, cacheItem.Length);
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
            TrimMessagesUnsafe(cacheItem, TimeSpan.FromMinutes(30));

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

        return _fusionCache.GetOrSetAsync<CacheItem>($"messagecache:{channel.Id}", LoadMessages);

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
                _logger.LogInformation("Downloading messages for {Channel}", channel.Name);

                await foreach (IReadOnlyCollection<IMessage>? batch in channel.GetMessagesAsync(limit: 250).WithCancellation(ct))
                {
                    foreach (var message in batch)
                    {
                        if (await ConvertMessage(message) is { } cacheMessage)
                        {
                            cacheItem.Messages.AddFirst(cacheMessage);
                            cacheItem.Length += cacheMessage.Content.Length;
                        }
                    }
                }

                TrimMessagesUnsafe(cacheItem, since: TimeSpan.FromHours(3));

                _logger.LogInformation("Got {Count} messages for {Channel}. Cache item length is {CacheItemLength}", cacheItem.Messages.Count, cacheItem.Name, cacheItem.Length);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var content = string.Join(Environment.NewLine, cacheItem.Messages.Select(m => m.ToString()));
                    _logger.LogDebug("Got chat messages for {Channel}:\n{Messages}", channel.Name, content);
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

    private static async Task<Message?> ConvertMessage(IMessage message)
    {
        if (message.Author.IsBot || string.IsNullOrWhiteSpace(message.Content))
        {
            return null;
        }

        string content = await FormatContent();

        content = UrlRegex().Replace(content, ""); // Strip urls to shorten the messages

        return new Message
        {
            Id = message.Id,
            ReplyToMessageId = message.Reference?.MessageId is { IsSpecified: true } reference ? reference.Value : null,
            AuthorId = message.Author.Id,
            AuthorDisplayName = await GetUserDisplayName(message.Author),
            Timestamp = message.Timestamp,
            Content = content,
        };

        async Task<string> FormatContent()
        {
            var text = new StringBuilder(message.Content.ReplaceLineEndings(" "));
            var tags = message.Tags;
            int indexOffset = -0;

            foreach (var tag in tags)
            {
                string? newText = tag.Type switch
                {
                    TagType.UserMention when tag.Value is IUser user => $"@{await GetUserDisplayName(user)}",
                    TagType.ChannelMention when tag.Value is IChannel channel => $"#{channel.Name}",
                    TagType.RoleMention when tag.Value is IRole role => $"@{role.Name}",
                    TagType.EveryoneMention => "@everyone",
                    TagType.HereMention => "@here",
                    TagType.Emoji when tag.Value is IEmote emote => $":{emote.Name}:",
                    _ => null
                };

                if (newText is null)
                {
                    continue;
                }

                text.Remove(tag.Index + indexOffset, tag.Length);
                text.Insert(tag.Index + indexOffset, newText);
                indexOffset += newText.Length - tag.Length;
            }

            return text.ToString();
        }

        async Task<string> GetUserDisplayName(IUser user)
        {
            var guildUser = user as IGuildUser;

            if (guildUser is null && message.Channel is IGuildChannel guildChannel)
            {
                guildUser = await guildChannel.GetUserAsync(user.Id);
            }

            return guildUser is not null
                ? $"<{guildUser.Nickname ?? guildUser.GlobalName ?? guildUser.Username}>"
                : $"<{user.GlobalName ?? user.Username}>";
        }
    }

    [GeneratedRegex(@"http[s]?://[^\s]+")] // Good enuff
    private static partial Regex UrlRegex();
}