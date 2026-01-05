using System.Collections.Specialized;
using System.Reactive;
using FluentAvalonia.UI.Controls;
using ReactiveUI;
using Xabbo.Configuration;
using Xabbo.Services.Abstractions;

namespace Xabbo.ViewModels;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.Avalonia.Fluent.SymbolIconSource;

public sealed class SettingsPageViewModel : PageViewModel
{
    public override string Header => "Settings";
    public override IconSource? Icon { get; } = new SymbolIconSource { Symbol = Symbol.Settings };

    private readonly IConfigProvider<AppConfig> _config;
    public AppConfig Config => _config.Value;

    [Reactive] public string CustomProfanityWordsText { get; set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> ApplyCustomWordsCmd { get; }

    public SettingsPageViewModel(IConfigProvider<AppConfig> config)
    {
        _config = config;

        // Initialize text from current custom words
        RefreshCustomWordsText();

        // Subscribe to collection changes (for when words are added/removed via context menu)
        SubscribeToCustomWordsChanges();

        // Subscribe to config reload to refresh the text
        _config.Loaded += () =>
        {
            SubscribeToCustomWordsChanges();
            RefreshCustomWordsText();
        };

        ApplyCustomWordsCmd = ReactiveCommand.Create(ApplyCustomWords);
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
