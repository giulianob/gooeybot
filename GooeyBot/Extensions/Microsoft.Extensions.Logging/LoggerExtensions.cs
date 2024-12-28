// Licensed under the Apache License, Version 2.0 (the "License");

namespace Microsoft.Extensions.Logging;

public static class LoggerExtensions
{
    public static void LogDiscordMessage(this ILogger logger, LogMessage msg)
    {
        logger.Log(msg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Debug => LogLevel.Debug,
                LogSeverity.Verbose => LogLevel.Trace,
                _ => throw new ArgumentOutOfRangeException()
            },
            msg.Exception,
            msg.Message);
    }
}