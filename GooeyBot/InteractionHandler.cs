// Licensed under the Apache License, Version 2.0 (the "License");

using Discord.Interactions;
using Discord.WebSocket;
using GooeyBot.Modules;
using IResult = Discord.Interactions.IResult;

namespace GooeyBot;

public class InteractionHandler(
    DiscordSocketClient client,
    InteractionService handler,
    IServiceProvider services,
    ILogger<InteractionHandler> logger)
{
    public async Task InitializeAsync()
    {
        client.Ready += () => handler.RegisterCommandsGloballyAsync();
        handler.Log += message =>
        {
            logger.LogDiscordMessage(message);
            return Task.CompletedTask;
        };

        await handler.AddModulesAsync(typeof(SummaryModule).Assembly, services);

        // Process the InteractionCreated payloads to execute Interactions commands
        client.InteractionCreated += HandleInteraction;
        // Also process the result of the command execution.
        handler.InteractionExecuted += HandleInteractionExecute;
    }

    private async Task HandleInteraction(SocketInteraction interaction)
    {
        try
        {
            // Create an execution context that matches the generic type parameter of your InteractionModuleBase<T> modules.
            var context = new SocketInteractionContext(client, interaction);

            // Execute the incoming command.
            var result = await handler.ExecuteCommandAsync(context, services);

            // Due to async nature of InteractionFramework, the result here may always be success.
            // That's why we also need to handle the InteractionExecuted event.
            if (!result.IsSuccess)
            {
                logger.LogWarning("Interaction failed with error {Error} error {ErrorReason}", result.Error, result.ErrorReason);
            }
        }
        catch
        {
            // If Slash Command execution fails it is most likely that the original interaction acknowledgement will persist. It is a good idea to delete the original
            // response, or at least let the user know that something went wrong during the command execution.
            if (interaction.Type is InteractionType.ApplicationCommand)
            {
                var msg = await interaction.GetOriginalResponseAsync();
                await msg.DeleteAsync();
            }
        }
    }

    private Task HandleInteractionExecute(ICommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            LogResultError(result);
        }

        return Task.CompletedTask;
    }

    private void LogResultError(IResult result)
    {
        logger.LogWarning("Interaction failed with error {Error} error {ErrorReason}", result.Error, result.ErrorReason);
    }
}