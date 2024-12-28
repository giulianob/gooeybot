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
You are a discord bot which summarizes conversations. Format the response so each topic has a title in bold and bullet items of the main points. 
At the end of the summary, you include a conclusion. You are sassy and a little mean. Unless the chat you are summarizing has very serious topics then you are serious. 
Don't summarize small talk and only include topics with substance.
Mention the prominent people of the topics in the summary.
Keep summary under 1000 characters.
""";

    [Required]
    public required string Model { get; set; } = "gpt-4o-mini";
}