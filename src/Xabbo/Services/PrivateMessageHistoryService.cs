using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Xabbo.Core;
using Xabbo.Models.Enums;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

public sealed class PrivateMessageHistoryService : IPrivateMessageHistoryService, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    private readonly Channel<PrivateMessageRecord> _writeQueue = Channel.CreateUnbounded<PrivateMessageRecord>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Task _processTask;

    public PrivateMessageHistoryService(IAppPathProvider pathProvider)
    {
        var dbPath = pathProvider.GetPath(AppPathKind.PrivateMessagesDb);
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA cache_size=-4000;
                PRAGMA temp_store=MEMORY;
                """;
            cmd.ExecuteNonQuery();
        }

        InitializeDatabase();

        _processTask = Task.Run(ProcessWriteQueueAsync);
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS private_messages (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp   INTEGER NOT NULL,
                friend_id   INTEGER NOT NULL,
                friend_name TEXT    NOT NULL,
                sender_name TEXT    NOT NULL,
                content     TEXT    NOT NULL,
                is_from_me  INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_pm_friend_id   ON private_messages(friend_id);
            CREATE INDEX IF NOT EXISTS idx_pm_timestamp   ON private_messages(timestamp);
            """;
        cmd.ExecuteNonQuery();
    }

    private async Task ProcessWriteQueueAsync()
    {
        await foreach (var record in _writeQueue.Reader.ReadAllAsync())
        {
            lock (_lock)
                InsertRecord(record);
        }
    }

    private void InsertRecord(PrivateMessageRecord r)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO private_messages (timestamp, friend_id, friend_name, sender_name, content, is_from_me)
            VALUES (@ts, @friendId, @friendName, @senderName, @content, @isFromMe)
            """;
        cmd.Parameters.AddWithValue("@ts", r.Timestamp.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@friendId", (long)r.FriendId);
        cmd.Parameters.AddWithValue("@friendName", r.FriendName);
        cmd.Parameters.AddWithValue("@senderName", r.SenderName);
        cmd.Parameters.AddWithValue("@content", r.Content);
        cmd.Parameters.AddWithValue("@isFromMe", r.IsFromMe ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void Enqueue(Id friendId, string friendName, string senderName, string content, DateTimeOffset timestamp, bool isFromMe)
    {
        _writeQueue.Writer.TryWrite(new PrivateMessageRecord(friendId, friendName, senderName, content, timestamp, isFromMe));
    }

    public Task<IReadOnlyList<PrivateMessageRecord>> LoadAllAsync()
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = """
                    SELECT timestamp, friend_id, friend_name, sender_name, content, is_from_me
                    FROM private_messages
                    ORDER BY timestamp ASC
                    """;

                var results = new List<PrivateMessageRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new PrivateMessageRecord(
                        FriendId: (Id)reader.GetInt64(1),
                        FriendName: reader.GetString(2),
                        SenderName: reader.GetString(3),
                        Content: reader.GetString(4),
                        Timestamp: DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)),
                        IsFromMe: reader.GetInt32(5) == 1
                    ));
                }
                return (IReadOnlyList<PrivateMessageRecord>)results;
            }
        });
    }

    public Task<IReadOnlyList<PrivateMessageRecord>> LoadConversationAsync(Id friendId)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = """
                    SELECT timestamp, friend_id, friend_name, sender_name, content, is_from_me
                    FROM private_messages
                    WHERE friend_id = @friendId
                    ORDER BY timestamp ASC
                    """;
                cmd.Parameters.AddWithValue("@friendId", (long)friendId);

                var results = new List<PrivateMessageRecord>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    results.Add(new PrivateMessageRecord(
                        FriendId: (Id)reader.GetInt64(1),
                        FriendName: reader.GetString(2),
                        SenderName: reader.GetString(3),
                        Content: reader.GetString(4),
                        Timestamp: DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)),
                        IsFromMe: reader.GetInt32(5) == 1
                    ));
                }
                return (IReadOnlyList<PrivateMessageRecord>)results;
            }
        });
    }

    public Task DeleteConversationAsync(Id friendId)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM private_messages WHERE friend_id = @friendId";
                cmd.Parameters.AddWithValue("@friendId", (long)friendId);
                cmd.ExecuteNonQuery();
            }
        });
    }

    public void Dispose()
    {
        _writeQueue.Writer.Complete();
        _processTask.Wait(TimeSpan.FromSeconds(5));
        _connection.Close();
        _connection.Dispose();
    }
}
