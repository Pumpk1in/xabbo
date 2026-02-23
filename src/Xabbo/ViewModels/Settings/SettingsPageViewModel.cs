using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using FluentAvalonia.UI.Controls;
using ReactiveUI;
using Xabbo.Configuration;
using Xabbo.Models;
using Xabbo.Services.Abstractions;

namespace Xabbo.ViewModels;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

public sealed class SettingsPageViewModel : PageViewModel
{
    public override string Header => "Settings";
    public override IconSource? Icon { get; } = new SymbolIconSource { Symbol = Symbol.Settings };

    private readonly IConfigProvider<AppConfig> _config;
    private readonly IChatHistoryService _chatHistory;
    public AppConfig Config => _config.Value;

    public static IReadOnlyList<ChatBubbleOption> NormalBubbles { get; } = ChatBubbleOption.NormalBubbles;
    public static IReadOnlyList<ChatBubbleOption> OtherBubbles  { get; } = ChatBubbleOption.OtherBubbles;

    [Reactive] public ChatBubbleOption? SelectedNormalBubble { get; set; }
    [Reactive] public ChatBubbleOption? SelectedOtherBubble  { get; set; }
    [Reactive] public string CustomProfanityWordsText { get; set; } = string.Empty;
    [Reactive] public int HistoryEntryCount { get; set; }

    public ReactiveCommand<Unit, Unit> ApplyCustomWordsCmd { get; }
    public ReactiveCommand<Unit, Unit> ClearHistoryCmd { get; }

    public SettingsPageViewModel(IConfigProvider<AppConfig> config, IChatHistoryService chatHistory)
    {
        _config = config;
        _chatHistory = chatHistory;

        // Initialize text from current custom words
        RefreshCustomWordsText();

        // Subscribe to collection changes (for when words are added/removed via context menu)
        SubscribeToCustomWordsChanges();

        // Subscribe to config reload to refresh the text
        _config.Loaded += () =>
        {
            SubscribeToCustomWordsChanges();
            RefreshCustomWordsText();
            InitBubbleSelection();
        };

        // Initialize bubble selection from config
        InitBubbleSelection();

        this.WhenAnyValue(x => x.SelectedNormalBubble)
            .WhereNotNull()
            .Subscribe(b => { SelectedOtherBubble = null; Config.Chat.BubbleStyle = b.Id; });

        this.WhenAnyValue(x => x.SelectedOtherBubble)
            .WhereNotNull()
            .Subscribe(b => { SelectedNormalBubble = null; Config.Chat.BubbleStyle = b.Id; });

        // Initialize history count
        HistoryEntryCount = _chatHistory.GetEntryCount();

        ApplyCustomWordsCmd = ReactiveCommand.Create(ApplyCustomWords);
        ClearHistoryCmd = ReactiveCommand.Create(ClearHistory);
    }

    private void InitBubbleSelection()
    {
        var savedId = Config.Chat.BubbleStyle;
        SelectedNormalBubble = ChatBubbleOption.NormalBubbles.FirstOrDefault(b => b.Id == savedId);
        SelectedOtherBubble  = ChatBubbleOption.OtherBubbles.FirstOrDefault(b => b.Id == savedId);
        if (SelectedNormalBubble is null && SelectedOtherBubble is null)
            SelectedNormalBubble = ChatBubbleOption.NormalBubbles[0];
    }

    private void ClearHistory()
    {
        _chatHistory.Clear();
        HistoryEntryCount = 0;
    }

    public void RefreshHistoryCount()
    {
        HistoryEntryCount = _chatHistory.GetEntryCount();
    }

    private System.Collections.ObjectModel.ObservableCollection<string>? _subscribedCollection;

    private void SubscribeToCustomWordsChanges()
    {
        var currentCollection = Config.Profanity.CustomWords;
        if (_subscribedCollection != currentCollection)
        {
            if (_subscribedCollection != null)
            {
                _subscribedCollection.CollectionChanged -= OnCustomWordsChanged;
            }
            _subscribedCollection = currentCollection;
            _subscribedCollection.CollectionChanged += OnCustomWordsChanged;
        }
    }

    private void OnCustomWordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshCustomWordsText();
    }

    private void RefreshCustomWordsText()
    {
        CustomProfanityWordsText = string.Join("\n", Config.Profanity.CustomWords);
    }

    private void ApplyCustomWords()
    {
        var words = CustomProfanityWordsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToList();

        Config.Profanity.CustomWords.Clear();
        foreach (var word in words)
        {
            Config.Profanity.CustomWords.Add(word);
        }
    }
}
