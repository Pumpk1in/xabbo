using System;
using System.Reactive.Disposables;

namespace Xabbo.ViewModels;

public abstract class ChatLogEntryViewModel : ViewModelBase, IDisposable
{
    public long EntryId { get; set; }
    public DateTime Timestamp { get; } = DateTime.Now;

    public virtual bool IsSelectable => true;

    protected CompositeDisposable Disposables { get; } = new();
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disposables.Dispose();
        GC.SuppressFinalize(this);
    }
}
