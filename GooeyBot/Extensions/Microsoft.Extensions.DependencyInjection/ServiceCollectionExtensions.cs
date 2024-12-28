// Licensed under the Apache License, Version 2.0 (the "License");

using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGooeyBot(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptionsWithValidateOnStart<DiscordOptions>()
            .Bind(configuration.GetSection(DiscordOptions.SectionName))
            .ValidateDataAnnotations();

        services
            .AddOptionsWithValidateOnStart<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<DiscordSocketClient>(s => new(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent,
            UseInteractionSnowflakeDate = false
        }));

        services.AddSingleton<OpenAIClient>(s =>
        {
            var options = s.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            return new OpenAIClient(options.ApiKey);
        });
        services.AddSingleton<ChatClient>(s =>
        {
            var options = s.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            return s.GetRequiredService<OpenAIClient>().GetChatClient(options.Model);
        });

        services.AddSingleton<InteractionService>(s => new(
            s.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig()));

        services.AddSingleton<InteractionHandler>();

        services.AddSingleton<MessageCache>();
        services.AddFusionCache();

        services.AddHostedService<BotHostedService>();

        return services;
    }
}