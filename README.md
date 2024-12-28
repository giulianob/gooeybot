Summarizes Discord chats with /tldr command using OpenAI models.

### Installation
1. Create a [Discord bot and get the token](https://docs.discordnet.dev/guides/getting_started/first-bot.html).
1. Build the `gooeybot` container:
   ```bash
   dotnet publish --os linux --arch x64 /t:PublishContainer`
   docker push YOURREPO/gooeybot:latest
   ```
1. Run on your favorite platform. Env vars needed for credentials:
   * `DISCORD__TOKEN` - (Required) Discord bot token
   * `OPENAI__APIKEY` - (Required) OpenAI API key
   * `OPENAI__MODEL` - (Optional) OpenAI model to use (default: `gpt-4o-mini`)
    * `OPENAI__PROMPT` - (Optional) The default system prompt to use. See `Options.cs` for the default

   > These can also be configured via `appsettings.json` or `appsettings.Production.json` files.

### Development

1. Set user secrets for development:
   ```bash
   dotnet user-secrets set "Discord:Token" "..."
   dotnet user-secrets set "OpenAi:ApiKey" "..."
   ```
1. Run the `GooeyBot` project in your IDE or via `dotnet run`.