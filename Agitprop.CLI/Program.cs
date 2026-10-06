﻿using System.CommandLine;
using Agitprop.CLI.Commands;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.DependencyInjection;

class Program
{
    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "O";
                options.UseUtcTimestamp = true;
            });
            logging.Services.Configure<ConsoleLoggerOptions>(options =>
                options.LogToStandardErrorThreshold = LogLevel.Trace);
        });

        var rootCommand = new RootCommand
        {
            Description = "Agitprop CLI Tool",
        };

        rootCommand.AddScrapeArticleCommand(loggerFactory);
        rootCommand.AddScrapeArchiveCommand(loggerFactory);
        rootCommand.AddRetryCommand(loggerFactory);

        await rootCommand.InvokeAsync(args);
    }
}