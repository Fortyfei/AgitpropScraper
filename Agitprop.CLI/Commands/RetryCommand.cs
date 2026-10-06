using System.CommandLine;
using Agitprop.CLI.Services;
using Microsoft.Extensions.Logging;

namespace Agitprop.CLI.Commands;

public static class RetryCommand
{
    private const string CommandName = "retry";
    private const string DefaultFailedQueueName = "newsfeed-job_error";
    private const string DefaultTargetQueueName = "newsfeed-job";
    private static readonly IScrapeCommandOrchestrator _orchestrator = new ScrapeCommandOrchestrator();

    internal static Command AddRetryCommand(this RootCommand rootCommand, ILoggerFactory loggerFactory)
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

        var logger = loggerFactory.CreateLogger("Agitprop.CLI.retry");
        retryCommand.SetHandler(async (string connection, string failedQueue, string targetQueue, int max) =>
        {
            using var scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["FailedQueue"] = failedQueue,
                ["TargetQueue"] = targetQueue,
                ["MaxMessages"] = max
            });
            logger.LogInformation("CLI command started: {Command}", CommandName);

            if (max < 0)
            {
                logger.LogWarning("CLI command rejected invalid input: {Field}", "max");
                Console.WriteLine("Error: --max must be greater than or equal to 0.");
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(failedQueue))
            {
                logger.LogWarning("CLI command rejected invalid input: {Field}", "failed_queue");
                Console.WriteLine("Error: --failed-queue cannot be empty.");
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(targetQueue))
            {
                logger.LogWarning("CLI command rejected invalid input: {Field}", "target_queue");
                Console.WriteLine("Error: --target-queue cannot be empty.");
                Environment.ExitCode = 1;
                return;
            }

            var request = new RetryFailedFeedsRequest(connection, failedQueue, targetQueue, max);
            RetryFailedFeedsExecutionResult result;
            try
            {
                result = await _orchestrator.RetryFailedFeedsAsync(request);
            }
            catch (Exception ex)
            {
                logger.LogError("CLI command failed: {Command}; exception type: {ExceptionType}",
                    CommandName, ex.GetType().Name);
                throw;
            }

            if (!result.RetryEnabled)
            {
                logger.LogWarning("CLI retry was disabled");
                Console.WriteLine("Retry is disabled. Provide a valid RabbitMQ connection string.");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine($"Scanned failed messages: {result.ScannedCount}");
            Console.WriteLine($"Requeued messages: {result.RequeuedCount}");

            if (!result.Success)
            {
                logger.LogError("CLI command failed: {Command}; reason: {Reason}", CommandName, "retry_failed");
                Console.WriteLine($"Retry failed: {result.ErrorMessage}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine("Retry completed successfully.");
            logger.LogInformation("CLI command completed: {Command}; scanned: {ScannedCount}; requeued: {RequeuedCount}",
                CommandName, result.ScannedCount, result.RequeuedCount);
        }, connectionOption, failedQueueOption, targetQueueOption, maxOption);

        rootCommand.Add(retryCommand);
        return retryCommand;
    }
}
