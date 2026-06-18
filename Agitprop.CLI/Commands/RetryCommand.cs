using System.CommandLine;
using Agitprop.CLI.Services;

namespace Agitprop.CLI.Commands;

public static class RetryCommand
{
    private const string CommandName = "retry";
    private const string DefaultFailedQueueName = "newsfeed-job_error";
    private const string DefaultTargetQueueName = "newsfeed-job";
    private static readonly IScrapeCommandOrchestrator _orchestrator = new ScrapeCommandOrchestrator();

    internal static Command AddRetryCommand(this RootCommand rootCommand)
    {
        var connectionOption = new Option<string>(
            ["--connection", "-c"],
            "RabbitMQ connection string");
        connectionOption.IsRequired = true;

        var failedQueueOption = new Option<string>(
            ["--failed-queue"],
            () => DefaultFailedQueueName,
            "Source queue containing failed feed messages");

        var targetQueueOption = new Option<string>(
            ["--target-queue"],
            () => DefaultTargetQueueName,
            "Destination queue where messages should be requeued");

        var maxOption = new Option<int>(
            ["--max", "-m"],
            () => 0,
            "Maximum number of failed messages to requeue (0 = all available)");

        var retryCommand = new Command(CommandName, "Requeue failed feed messages from RabbitMQ error queue")
        {
            connectionOption,
            failedQueueOption,
            targetQueueOption,
            maxOption
        };

        retryCommand.SetHandler(async (string connection, string failedQueue, string targetQueue, int max) =>
        {
            if (max < 0)
            {
                Console.WriteLine("Error: --max must be greater than or equal to 0.");
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(failedQueue))
            {
                Console.WriteLine("Error: --failed-queue cannot be empty.");
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(targetQueue))
            {
                Console.WriteLine("Error: --target-queue cannot be empty.");
                Environment.ExitCode = 1;
                return;
            }

            var request = new RetryFailedFeedsRequest(connection, failedQueue, targetQueue, max);
            var result = await _orchestrator.RetryFailedFeedsAsync(request);

            if (!result.RetryEnabled)
            {
                Console.WriteLine("Retry is disabled. Provide a valid RabbitMQ connection string.");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Scanned failed messages: {result.ScannedCount}");
            Console.WriteLine($"Requeued messages: {result.RequeuedCount}");

            if (!result.Success)
            {
                Console.WriteLine($"Retry failed: {result.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine("Retry completed successfully.");
        }, connectionOption, failedQueueOption, targetQueueOption, maxOption);

        rootCommand.Add(retryCommand);
        return retryCommand;
    }
}
