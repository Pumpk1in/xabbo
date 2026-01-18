using System.Text.Json;
using Xabbo.Models;
using Xabbo.Models.Enums;
using Xabbo.Services.Abstractions;

namespace Xabbo.Services;

public sealed class ChatHistoryService : IChatHistoryService
{
    private readonly IAppPathProvider _pathProvider;
    private readonly List<ChatHistoryEntry> _entries = [];
    private readonly object _lock = new();
    private readonly Timer _saveTimer;
    private bool _isDirty;
    private bool _isLoaded;

    public ChatHistoryService(IAppPathProvider pathProvider)
    {
        _pathProvider = pathProvider;

        // Auto-save every 30 seconds if there are changes
        _saveTimer = new Timer(_ => SaveIfDirty(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Load existing history in background - won't block startup
        _ = LoadAsync();
    }

    public void AddEntry(ChatHistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            _isDirty = true;
        }
    }

    public IEnumerable<ChatHistoryEntry> Search(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null)
    {
        return SearchWithCount(userName, keyword, profanityOnly, fromDate, toDate, limit).Results;
    }

    public (IEnumerable<ChatHistoryEntry> Results, int TotalCount) SearchWithCount(
        string? userName = null,
        string? keyword = null,
        bool? profanityOnly = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? limit = null)
    {
        lock (_lock)
        {
            IEnumerable<ChatHistoryEntry> results = _entries;

            // Filter by date range
            if (fromDate.HasValue)
                results = results.Where(e => e.Timestamp >= fromDate.Value);
            if (toDate.HasValue)
                results = results.Where(e => e.Timestamp <= toDate.Value);

            // Filter by user name (for messages and actions)
            if (!string.IsNullOrWhiteSpace(userName))
            {
                results = results.Where(e =>
                    (e.Name?.Contains(userName, StringComparison.OrdinalIgnoreCase) == true) ||
                    (e.UserName?.Contains(userName, StringComparison.OrdinalIgnoreCase) == true));
            }

            // Filter by keyword in message
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                results = results.Where(e =>
                    e.Message?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
            }

            // Filter by profanity
            if (profanityOnly == true)
            {
                results = results.Where(e => e.HasProfanity);
            }

            // Order by timestamp descending (most recent first)
            var orderedResults = results.OrderByDescending(e => e.Timestamp).ToList();
            var totalCount = orderedResults.Count;

            // Apply limit
            if (limit.HasValue)
                orderedResults = orderedResults.Take(limit.Value).ToList();

            return (orderedResults, totalCount);
        }
    }

    public int GetEntryCount()
    {
        lock (_lock)
        {
            return _entries.Count;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _isDirty = true;
        }
    }

    public async Task SaveAsync()
    {
        List<ChatHistoryEntry> snapshot;
        lock (_lock)
        {
            snapshot = [.. _entries];
            _isDirty = false;
        }

        var path = _pathProvider.GetPath(AppPathKind.ChatHistory);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, snapshot, ChatHistoryJsonContext.Default.ListChatHistoryEntry);
    }

    public async Task LoadAsync()
    {
        // Only load once
        if (_isLoaded)
            return;

        var path = _pathProvider.GetPath(AppPathKind.ChatHistory);
        if (!File.Exists(path))
        {
            _isLoaded = true;
            return;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var entries = await JsonSerializer.DeserializeAsync(stream, ChatHistoryJsonContext.Default.ListChatHistoryEntry);

            if (entries is not null)
            {
                lock (_lock)
                {
                    // Insert loaded entries at the beginning, keeping any new entries that were added
                    _entries.InsertRange(0, entries);
                }
            }
        }
        catch
        {
            // Ignore corrupted history file
        }
        finally
        {
            _isLoaded = true;
        }
    }

    private void SaveIfDirty()
    {
        if (_isDirty)
        {
            _ = SaveAsync();
        }
    }
}
