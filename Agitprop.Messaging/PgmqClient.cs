using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NpgsqlTypes;

namespace Agitprop.Messaging;

public static class PgmqQueueNames
{
    public const string NewsfeedJobs = "newsfeed_job";
    public const string FailedNewsfeedJobs = "newsfeed_job_error";
}

public sealed record PgmqMessage<T>(
    long MessageId,
    int ReadCount,
    T Message,
    IReadOnlyDictionary<string, string?>? Headers = null);

public interface IPgmqClient
{
    Task EnsureQueuesAsync(
        CancellationToken cancellationToken = default,
        params string[] additionalQueueNames);

    Task<long> SendAsync<T>(
        string queueName,
        T message,
        int delaySeconds = 0,
        CancellationToken cancellationToken = default);

    Task<int> SendBatchAsync<T>(
        string queueName,
        IReadOnlyCollection<T> messages,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PgmqMessage<T>>> ReadAsync<T>(
        string queueName,
        int visibilityTimeoutSeconds,
        int quantity,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string queueName, long messageId, CancellationToken cancellationToken = default);

    Task SetVisibilityTimeoutAsync(
        string queueName,
        long messageId,
        int delaySeconds,
        CancellationToken cancellationToken = default);

    Task MoveAsync<T>(
        string sourceQueue,
        string destinationQueue,
        long messageId,
        T message,
        int delaySeconds = 0,
        IReadOnlyDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default);
}

public sealed class PgmqClient(NpgsqlDataSource dataSource) : IPgmqClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task EnsureQueuesAsync(
        CancellationToken cancellationToken = default,
        params string[] additionalQueueNames)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('agitprop.pgmq.initialize', 0))",
            connection,
            transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var extensionCommand = new NpgsqlCommand(
            "CREATE EXTENSION IF NOT EXISTS pgmq",
            connection,
            transaction))
        {
            await extensionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var queueNames = new HashSet<string>(
            [PgmqQueueNames.NewsfeedJobs, PgmqQueueNames.FailedNewsfeedJobs],
            StringComparer.Ordinal);
        foreach (var additionalQueueName in additionalQueueNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(additionalQueueName);
            queueNames.Add(additionalQueueName.ToLowerInvariant());
        }

        foreach (var queueName in queueNames)
        {
            await using var existsCommand = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pgmq.list_queues() WHERE queue_name = @queue_name)",
                connection,
                transaction);
            existsCommand.Parameters.AddWithValue("queue_name", queueName);
            var exists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Could not check whether pgmq queue '{queueName}' exists."));

            if (!exists)
            {
                await using var createCommand = new NpgsqlCommand(
                    "SELECT pgmq.create(@queue_name)",
                    connection,
                    transaction);
                createCommand.Parameters.AddWithValue("queue_name", queueName);
                await createCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<long> SendAsync<T>(
        string queueName,
        T message,
        int delaySeconds = 0,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateSendCommand(
            connection,
            null,
            queueName,
            message,
            delaySeconds,
            CreateTraceHeaders());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long messageId
            ? messageId
            : throw new InvalidOperationException($"pgmq.send did not return a message ID for '{queueName}'.");
    }

    public async Task<int> SendBatchAsync<T>(
        string queueName,
        IReadOnlyCollection<T> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var message in messages)
        {
            await using var command = CreateSendCommand(
                connection,
                transaction,
                queueName,
                message,
                0,
                CreateTraceHeaders());
            await command.ExecuteScalarAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }

    public async Task<IReadOnlyList<PgmqMessage<T>>> ReadAsync<T>(
        string queueName,
        int visibilityTimeoutSeconds,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        if (visibilityTimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeoutSeconds));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT msg_id, read_ct, message::text, headers::text FROM pgmq.read(@queue_name, @visibility_timeout, @quantity)",
            connection);
        command.Parameters.AddWithValue("queue_name", queueName);
        command.Parameters.AddWithValue("visibility_timeout", visibilityTimeoutSeconds);
        command.Parameters.AddWithValue("quantity", quantity);

        var messages = new List<PgmqMessage<T>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var message = JsonSerializer.Deserialize<T>(reader.GetString(2), SerializerOptions)
                ?? throw new JsonException($"pgmq returned an empty message from '{queueName}'.");
            var headers = reader.IsDBNull(3)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(reader.GetString(3), SerializerOptions);
            messages.Add(new PgmqMessage<T>(reader.GetInt64(0), reader.GetInt32(1), message, headers));
        }

        return messages;
    }

    public async Task DeleteAsync(
        string queueName,
        long messageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT pgmq.delete(@queue_name, @message_id)",
            connection);
        command.Parameters.AddWithValue("queue_name", queueName);
        command.Parameters.AddWithValue("message_id", messageId);

        var deleted = (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"pgmq.delete did not return a result for message {messageId}."));
        if (!deleted)
        {
            throw new InvalidOperationException($"pgmq message {messageId} was not found in queue '{queueName}'.");
        }
    }

    public async Task SetVisibilityTimeoutAsync(
        string queueName,
        long messageId,
        int delaySeconds,
        CancellationToken cancellationToken = default)
    {
        if (delaySeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pgmq.set_vt(
                    @queue_name,
                    @message_id,
                    clock_timestamp() + make_interval(secs => @delay_seconds))
            )
            """,
            connection);
        command.Parameters.AddWithValue("queue_name", queueName);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("delay_seconds", delaySeconds);

        var updated = (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"pgmq.set_vt did not return a result for message {messageId}."));
        if (!updated)
        {
            throw new InvalidOperationException($"pgmq message {messageId} was not found in queue '{queueName}'.");
        }
    }

    public async Task MoveAsync<T>(
        string sourceQueue,
        string destinationQueue,
        long messageId,
        T message,
        int delaySeconds = 0,
        IReadOnlyDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var sendCommand = CreateSendCommand(
            connection,
            transaction,
            destinationQueue,
            message,
            delaySeconds,
            headers ?? CreateTraceHeaders());
        await sendCommand.ExecuteScalarAsync(cancellationToken);

        await using var archiveCommand = new NpgsqlCommand(
            "SELECT pgmq.archive(@queue_name, @message_id)",
            connection,
            transaction);
        archiveCommand.Parameters.AddWithValue("queue_name", sourceQueue);
        archiveCommand.Parameters.AddWithValue("message_id", messageId);
        var archived = (bool)(await archiveCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException($"pgmq.archive did not return a result for message {messageId}."));
        if (!archived)
        {
            throw new InvalidOperationException($"pgmq message {messageId} was not found in queue '{sourceQueue}'.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static NpgsqlCommand CreateSendCommand<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string queueName,
        T message,
        int delaySeconds,
        IReadOnlyDictionary<string, string?>? headers = null)
    {
        var command = new NpgsqlCommand(
            "SELECT pgmq.send(@queue_name, @message, @headers, @delay_seconds)",
            connection,
            transaction);
        command.Parameters.AddWithValue("queue_name", queueName);
        command.Parameters.Add(new NpgsqlParameter("message", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(message, SerializerOptions)
        });
        command.Parameters.Add(new NpgsqlParameter("headers", NpgsqlDbType.Jsonb)
        {
            Value = headers is null
                ? DBNull.Value
                : JsonSerializer.Serialize(headers, SerializerOptions)
        });
        command.Parameters.AddWithValue("delay_seconds", delaySeconds);
        return command;
    }

    private static IReadOnlyDictionary<string, string?>? CreateTraceHeaders()
    {
        var activity = Activity.Current;
        if (activity?.Id is not { } traceParent)
        {
            return null;
        }

        var headers = new Dictionary<string, string?> { ["traceparent"] = traceParent };
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers["tracestate"] = activity.TraceStateString;
        }

        return headers;
    }
}

public static class PgmqServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddPgmqClient(
        this IHostApplicationBuilder builder,
        string connectionStringName)
    {
        var connectionString = builder.Configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing required configuration key 'ConnectionStrings:{connectionStringName}'.");
        }

        builder.Services.AddSingleton<NpgsqlDataSource>(_ => NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton<IPgmqClient, PgmqClient>();
        return builder;
    }
}
