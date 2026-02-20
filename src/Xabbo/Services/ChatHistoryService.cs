using System.Text.Json;
using Microsoft.Data.Sqlite;
using Xabbo.Models;
using Xabbo.Models.Enums;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

public sealed class ChatHistoryService : IChatHistoryService, IDisposable
{
    private readonly IAppPathProvider _pathProvider;
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private int _entryCount;

    public ChatHistoryService(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider;

        var dbPath = _pathProvider.GetPath(AppPathKind.ChatHistoryDb);
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        using (var pragmaCmd = _connection.CreateCommand())
        {
            pragmaCmd.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA cache_size=-8000;
                PRAGMA temp_store=MEMORY;
                """;
            pragmaCmd.ExecuteNonQuery();
        }

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS chat_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                type TEXT NOT NULL,
                name TEXT,
                message TEXT,
                chat_type TEXT,
                is_whisper INTEGER DEFAULT 0,
                has_profanity INTEGER DEFAULT 0,
                matched_words TEXT,
                user_name TEXT,
                action TEXT,
                room_name TEXT,
                room_owner TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_timestamp ON chat_history(timestamp);
            CREATE INDEX IF NOT EXISTS idx_name ON chat_history(name);
            CREATE INDEX IF NOT EXISTS idx_user_name ON chat_history(user_name);
            CREATE INDEX IF NOT EXISTS idx_has_profanity ON chat_history(has_profanity);
            CREATE INDEX IF NOT EXISTS idx_name_timestamp ON chat_history(name, timestamp);
            CREATE INDEX IF NOT EXISTS idx_user_name_timestamp ON chat_history(user_name, timestamp);
            """;
        cmd.ExecuteNonQuery();

        // Add whisper_recipient column if it doesn't exist (migration)
        try
        {
            using var alterCmd = _connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE chat_history ADD COLUMN whisper_recipient TEXT";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* Column already exists */ }

        // Get initial count
        using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM chat_history";
        _entryCount = Convert.ToInt32(countCmd.ExecuteScalar());
    }

    public void AddEntry(ChatHistoryEntry entry)
    {
        lock (_lock)
        {
            // Check for duplicates before inserting
            if (!EntryExists(entry))
            {
                InsertEntry(entry);
                _entryCount++;
            }
        }
    }

    /// <summary>
    /// Checks if an equivalent message entry already exists in the database.
    /// Uses name + message + timestamp (±2 seconds) to identify duplicates.
    /// </summary>
    private bool EntryExists(ChatHistoryEntry entry)
    {
        using var cmd = _connection.CreateCommand();

        cmd.CommandText = @"
            SELECT COUNT(*) FROM chat_history 
            WHERE type = 'message' 
            AND name = @name 
            AND message = @message 
            AND timestamp BETWEEN @startTime AND @endTime";

        var startTime = new DateTimeOffset(entry.Timestamp.AddSeconds(-2)).ToUnixTimeSeconds();
        var endTime = new DateTimeOffset(entry.Timestamp.AddSeconds(2)).ToUnixTimeSeconds();
        cmd.Parameters.AddWithValue("@startTime", startTime);
        cmd.Parameters.AddWithValue("@endTime", endTime);
        cmd.Parameters.AddWithValue("@name", (object?)entry.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@message", (object?)entry.Message ?? DBNull.Value);

        int count = Convert.ToInt32(cmd.ExecuteScalar());
        return count > 0;
    }

    private void InsertEntry(ChatHistoryEntry entry)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_history (timestamp, type, name, message, chat_type, is_whisper, whisper_recipient, has_profanity, matched_words, user_name, action, room_name, room_owner)
            VALUES (@timestamp, @type, @name, @message, @chatType, @isWhisper, @whisperRecipient, @hasProfanity, @matchedWords, @userName, @action, @roomName, @roomOwner)
            """;

        cmd.Parameters.AddWithValue("@timestamp", new DateTimeOffset(entry.Timestamp).ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("@type", entry.Type);
        cmd.Parameters.AddWithValue("@name", (object?)entry.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@message", (object?)entry.Message ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@chatType", (object?)entry.ChatType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isWhisper", entry.IsWhisper ? 1 : 0);
        cmd.Parameters.AddWithValue("@whisperRecipient", (object?)entry.WhisperRecipient ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hasProfanity", entry.HasProfanity ? 1 : 0);
        cmd.Parameters.AddWithValue("@matchedWords", entry.MatchedWords is { Count: > 0 } ? JsonSerializer.Serialize(entry.MatchedWords, ChatHistoryJsonContext.Default.ListString) : DBNull.Value);
        cmd.Parameters.AddWithValue("@userName", (object?)entry.UserName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", (object?)entry.Action ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@roomName", (object?)entry.RoomName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@roomOwner", (object?)entry.RoomOwner ?? DBNull.Value);

        cmd.ExecuteNonQuery();
    }

    public IEnumerable<ChatHistoryEntry> Search(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        bool? whispersOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null)
    {
        return SearchWithCount(userName, keyword, profanityOnly, whispersOnly, fromDate, toDate, limit).Results;
    }

    public (IEnumerable<ChatHistoryEntry> Results, int TotalCount) SearchWithCount(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        bool? whispersOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null)
    {
        lock (_lock)
        {
            var conditions = new List<string>();
            var parameters = new List<SqliteParameter>();

            if (fromDate.HasValue)
            {
                conditions.Add("timestamp >= @fromDate");
                parameters.Add(new SqliteParameter("@fromDate", new DateTimeOffset(fromDate.Value).ToUnixTimeSeconds()));
            }

            if (toDate.HasValue)
            {
                conditions.Add("timestamp <= @toDate");
                parameters.Add(new SqliteParameter("@toDate", new DateTimeOffset(toDate.Value).ToUnixTimeSeconds()));
            }

            if (!string.IsNullOrWhiteSpace(userName))
            {
                conditions.Add("(name LIKE @userName OR user_name LIKE @userName)");
                parameters.Add(new SqliteParameter("@userName", $"%{userName}%"));
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                conditions.Add("(message LIKE @keyword OR action LIKE @keyword)");
                parameters.Add(new SqliteParameter("@keyword", $"%{keyword}%"));
            }

            if (profanityOnly == true)
            {
                conditions.Add("has_profanity = 1");
            }

            if (whispersOnly == true)
            {
                conditions.Add("is_whisper = 1");
            }

            var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

            // Single query: get total count via window function, with optional limit on rows returned
            using var cmd = _connection.CreateCommand();
            var limitClause = limit.HasValue ? $"LIMIT {limit.Value}" : "";
            cmd.CommandText = $"SELECT *, COUNT(*) OVER() AS total_count FROM chat_history {whereClause} ORDER BY timestamp DESC {limitClause}";
            cmd.Parameters.AddRange(parameters.ToArray());

            var results = new List<ChatHistoryEntry>();
            int totalCount = 0;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (totalCount == 0)
                    totalCount = Convert.ToInt32(reader["total_count"]);
                results.Add(ReadEntry(reader));
            }

            return (results, totalCount);
        }
    }

    private static ChatHistoryEntry ReadEntry(SqliteDataReader reader)
    {
        List<string>? matchedWords = null;
        var matchedWordsJson = reader["matched_words"] as string;
        if (!string.IsNullOrEmpty(matchedWordsJson))
        {
            matchedWords = JsonSerializer.Deserialize(matchedWordsJson, ChatHistoryJsonContext.Default.ListString);
        }

        return new ChatHistoryEntry
        {
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(reader["timestamp"])).LocalDateTime,
            Type = reader["type"].ToString()!,
            Name = reader["name"] as string,
            Message = reader["message"] as string,
            ChatType = reader["chat_type"] as string,
            IsWhisper = Convert.ToInt32(reader["is_whisper"]) == 1,
            WhisperRecipient = reader["whisper_recipient"] as string,
            HasProfanity = Convert.ToInt32(reader["has_profanity"]) == 1,
            MatchedWords = matchedWords,
            UserName = reader["user_name"] as string,
            Action = reader["action"] as string,
            RoomName = reader["room_name"] as string,
            RoomOwner = reader["room_owner"] as string,
        };
    }

    public Task UpdateProfanityFlagsAsync(IProfanityFilterService profanityFilter)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                // Read all message IDs and their current profanity state
                var updates = new List<(long Id, bool NewFlag)>();

                using (var cmd = _connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT id, message, has_profanity FROM chat_history WHERE type = 'message' AND message IS NOT NULL";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt64(0);
                        var message = reader.GetString(1);
                        var currentFlag = reader.GetInt32(2) == 1;
                        var newFlag = profanityFilter.ContainsProfanity(message);

                        if (newFlag != currentFlag)
                            updates.Add((id, newFlag));
                    }
                }

                if (updates.Count == 0)
                    return;

                // Batch update changed flags in a single transaction
                using var transaction = _connection.BeginTransaction();
                using var updateCmd = _connection.CreateCommand();
                updateCmd.CommandText = "UPDATE chat_history SET has_profanity = @flag WHERE id = @id";
                var flagParam = updateCmd.Parameters.Add("@flag", SqliteType.Integer);
                var idParam = updateCmd.Parameters.Add("@id", SqliteType.Integer);

                foreach (var (id, newFlag) in updates)
                {
                    flagParam.Value = newFlag ? 1 : 0;
                    idParam.Value = id;
                    updateCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        });
    }

    public int GetEntryCount()
    {
        lock (_lock)
        {
            return _entryCount;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM chat_history";
            cmd.ExecuteNonQuery();
            _entryCount = 0;
        }
    }

    public Task SaveAsync()
    {
        // SQLite auto-saves, nothing to do
        return Task.CompletedTask;
    }

    public Task LoadAsync()
    {
        // Already loaded via SQLite, nothing to do
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}
