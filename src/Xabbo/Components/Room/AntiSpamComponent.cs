using Splat;
using Xabbo.Configuration;
using Xabbo.Controllers;
using Xabbo.Core;
using Xabbo.Core.Events;
using Xabbo.Core.Game;
using Xabbo.Extension;
using Xabbo.Services.Abstractions;
using Xabbo.ViewModels;

namespace Xabbo.Components;

public class AntiSpamComponent : Component
{
    private readonly IConfigProvider<AppConfig> _config;
    private readonly RoomManager _roomManager;
    private readonly RoomModerationController _moderation;
    private readonly XabbotComponent _xabbot;

    private readonly Dictionary<string, List<(string UserName, DateTimeOffset Time)>> _spamTracker = [];
    private readonly object _lock = new();

    private ChatPageViewModel? _chatPage;

    private AppConfig Settings => _config.Value;

    public AntiSpamComponent(
        IExtension extension,
        IConfigProvider<AppConfig> config,
        RoomManager roomManager,
        RoomModerationController moderation,
        XabbotComponent xabbot)
        : base(extension)
    {
        _config = config;
        _roomManager = roomManager;
        _moderation = moderation;
        _xabbot = xabbot;

        _roomManager.AvatarChat += OnAvatarChat;
        _roomManager.Left += OnLeftRoom;
    }

    protected override void OnDisconnected()
    {
        lock (_lock)
            _spamTracker.Clear();
    }

    private void OnLeftRoom()
    {
        lock (_lock)
            _spamTracker.Clear();
    }

    private void OnAvatarChat(AvatarChatEventArgs e)
    {
        if (!Settings.Chat.AntiSpam) return;
        if (e.Avatar.Type != AvatarType.User) return;
        if (e.ChatType == ChatType.Whisper) return;

        var key = e.Message.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key)) return;

        var now = DateTimeOffset.UtcNow;
        var windowSeconds = Settings.Chat.AntiSpamWindowSeconds;
        var threshold = Settings.Chat.AntiSpamThreshold;

        List<string>? spammers = null;

        lock (_lock)
        {
            if (!_spamTracker.TryGetValue(key, out var entries))
            {
                entries = [];
                _spamTracker[key] = entries;
            }

            // Purger les entrées trop vieilles
            entries.RemoveAll(x => (now - x.Time).TotalSeconds > windowSeconds);

            // N'ajouter qu'une entrée par utilisateur (évite double-comptage)
            if (!entries.Any(x => x.UserName.Equals(e.Avatar.Name, StringComparison.OrdinalIgnoreCase)))
                entries.Add((e.Avatar.Name, now));

            if (entries.Count >= threshold && _moderation.CanBan)
            {
                spammers = entries.Select(x => x.UserName).ToList();
                _spamTracker.Remove(key);
            }
        }

        if (spammers is { Count: > 0 })
            _ = TriggerSpamBanAsync(spammers, e.Message);
    }

    private async Task TriggerSpamBanAsync(List<string> spammerNames, string message)
    {
        var room = _roomManager.Room;
        if (room is null) return;

        var duration = Settings.Chat.AntiSpamBanDuration;

        var usersToban = spammerNames
            .Select(name => room.TryGetUserByName(name, out var user) ? user : null)
            .Where(u => u is not null && _moderation.CanModerate(RoomModerationController.ModerationType.Ban, u))
            .ToList();

        if (usersToban.Count == 0) return;

        await _moderation.BanUsersAsync(usersToban!, duration);

        var durationText = duration switch
        {
            BanDuration.Hour => "1h",
            BanDuration.Day => "24h",
            BanDuration.Permanent => "permanent",
            _ => duration.ToString()
        };

        _xabbot.ShowMessage($"Spam detected! {usersToban.Count} user(s) banned {durationText}.");

        _chatPage ??= Locator.Current.GetService<ChatPageViewModel>();
        foreach (var user in usersToban)
            _chatPage?.AppendModerationNotification(user!.Name, $"banned {durationText} (anti-spam)");
    }
}
