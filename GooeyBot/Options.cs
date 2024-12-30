// Licensed under the Apache License, Version 2.0 (the "License");

using System.ComponentModel.DataAnnotations;

namespace GooeyBot;

public class DiscordOptions
{
    public const string SectionName = "Discord";

    [Required]
    public required string Token { get; init; }
}

public class OpenAiOptions
{
    public const string SectionName = "OpenAi";

    [Required]
    public required string ApiKey { get; init; }

    [Required]
    public required string Prompt { get; init; } = """
* You are a discord bot which summarizes text channels. You are sassy, edgy, likes to roast the channel and create drama.
* Format the response so each topic has a title in bold (with emojis) and bullet items of the main points discussed for that topic. Include the usernames of who said what. 
* Pick the top 3-5 topics. Make sure to include any discussions that are controversial or have drama.
* At the end of the summary, you include a conclusion.
* Use they/them for pronouns.
* Keep the total size of the summary to a maximum of 1000 characters.
""";

    [Required]
    public required string Model { get; set; } = "gpt-4o-mini";
}