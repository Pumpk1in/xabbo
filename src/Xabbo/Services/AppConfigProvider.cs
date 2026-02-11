using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReactiveUI;

using Xabbo.Serialization;
using Xabbo.Services.Abstractions;
using Xabbo.Configuration;
using Xabbo.Models.Enums;

namespace Xabbo.Services;

public sealed partial class AppConfigProvider(
    IAppPathProvider appPathService,
    IHostApplicationLifetime lifetime,
    ILoggerFactory? loggerFactory = null
)
    : ConfigProvider<AppConfig>(JsonSourceGenerationContext.Default.AppConfig, lifetime, loggerFactory)
{
    private System.Collections.ObjectModel.ObservableCollection<string>? _subscribedCustomWords;
    private IDisposable? _autoSaveSubscription;

    protected override string FilePath => appPathService.GetPath(AppPathKind.Settings);

    protected override void OnValueChanged()
    {
        base.OnValueChanged();
        
        // Subscribe to profanity CustomWords changes for auto-save
        if (Value?.Profanity?.CustomWords != _subscribedCustomWords)
        {
            if (_subscribedCustomWords != null)
            {
                _subscribedCustomWords.CollectionChanged -= OnCustomWordsChanged;
            }

            _subscribedCustomWords = Value?.Profanity?.CustomWords;
            if (_subscribedCustomWords != null)
            {
                _subscribedCustomWords.CollectionChanged += OnCustomWordsChanged;
            }
        }
    }

    private void OnCustomWordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Debounce the auto-save to avoid excessive disk writes
        _autoSaveSubscription?.Dispose();
        _autoSaveSubscription = Observable
            .Return(Unit.Default)
            .Delay(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => Save());
    }
}
