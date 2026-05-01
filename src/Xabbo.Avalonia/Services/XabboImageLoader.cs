using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Xabbo.Avalonia.Services;

public sealed class XabboImageLoader(HttpClient httpClient, bool disposeHttpClient)
    : RamCachedWebImageLoader(httpClient, disposeHttpClient), IAdvancedAsyncImageLoader
{
    public static XabboImageLoader Instance { get; }

    // Limit concurrent HTTP requests to avoid CDN throttling
    private static readonly SemaphoreSlim _httpSemaphore = new(4, 4);

    private const int MaxCacheEntries = 150;

    private readonly ConcurrentDictionary<string, DateTime> _failureCache = [];

    private readonly object _lruLock = new();
    private readonly LinkedList<KeyValuePair<string, Bitmap>> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, Bitmap>>> _lruIndex = [];

    static XabboImageLoader()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.Add("User-Agent", "xabbo");
        Instance = new XabboImageLoader(client, false);
    }

    public string WebHost { get; set; } = "www.habbo.com";

    public bool TryGetBitmap(string url, out Bitmap? bitmap)
    {
        lock (_lruLock)
        {
            if (_lruIndex.TryGetValue(url, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                bitmap = node.Value.Value;
                return true;
            }
        }
        bitmap = null;
        return false;
    }

    private void Cache(string url, Bitmap bitmap)
    {
        lock (_lruLock)
        {
            if (_lruIndex.TryGetValue(url, out var existing))
            {
                _lruList.Remove(existing);
                _lruList.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<KeyValuePair<string, Bitmap>>(new(url, bitmap));
            _lruList.AddFirst(node);
            _lruIndex[url] = node;

            // Evict oldest entries beyond capacity. We do not Dispose the evicted bitmap:
            // it may still be referenced by an Image control. The GC will reclaim it once
            // no live reference remains.
            while (_lruList.Count > MaxCacheEntries)
            {
                var oldest = _lruList.Last!;
                _lruList.RemoveLast();
                _lruIndex.Remove(oldest.Value.Key);
            }
        }
    }

    public void ClearFailureCache() => _failureCache.Clear();

    protected override async Task<Bitmap?> LoadAsync(string url)
    {
        await _httpSemaphore.WaitAsync();
        try
        {
            return await base.LoadAsync(url);
        }
        finally
        {
            _httpSemaphore.Release();
        }
    }

    // AdvancedImage calls this 2-param overload (IAdvancedAsyncImageLoader) in 3.5.0+.
    // BaseWebImageLoader's implementation bypasses the RAM cache, so we redirect here.
    Task<Bitmap?> IAdvancedAsyncImageLoader.ProvideImageAsync(string url, IStorageProvider? storageProvider)
        => ProvideImageAsync(url);

    public override async Task<Bitmap?> ProvideImageAsync(string url)
    {
        try
        {
            if (TryGetBitmap(url, out Bitmap? cached) && cached is not null)
                return cached;

            if (_failureCache.TryGetValue(url, out DateTime failureTime) &&
                (DateTime.Now - failureTime).TotalSeconds < 30)
            {
                return null;
            }

            Bitmap? image = await base.ProvideImageAsync(url);
            if (image is null)
            {
                _failureCache.AddOrUpdate(url, DateTime.Now, (_, _) => DateTime.Now);
            }
            else
            {
                Cache(url, image);
            }

            return image;
        }
        catch
        {
            _failureCache.AddOrUpdate(url, DateTime.Now, (_, _) => DateTime.Now);
            return null;
        }
    }
}
