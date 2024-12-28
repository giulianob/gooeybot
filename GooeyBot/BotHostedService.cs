// Licensed under the Apache License, Version 2.0 (the "License");

using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace GooeyBot;

public class BotHostedService(
    DiscordSocketClient client,
    InteractionHandler handler,
    MessageCache messageCache,
    IOptions<DiscordOptions> options,
    ILogger<BotHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += message =>
        {
            logger.LogDiscordMessage(message);
            return Task.CompletedTask;
        };
        client.MessageReceived += async message =>
        {
            await messageCache.Add(message);
        };

        await handler.InitializeAsync();
        await client.LoginAsync(TokenType.Bot, options.Value.Token.Trim());
        await client.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
    }
}