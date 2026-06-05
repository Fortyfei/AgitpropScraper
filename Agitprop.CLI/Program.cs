﻿using System.CommandLine;
using Agitprop.CLI.Commands;

class Program
{
    public static async Task Main(string[] args)
    {
        var rootCommand = new RootCommand
        {
            Description = "Agitprop CLI Tool",
        };

        rootCommand.AddScrapeArticleCommand();
        rootCommand.AddScrapeArchiveCommand();

        await rootCommand.InvokeAsync(args);
    }
}