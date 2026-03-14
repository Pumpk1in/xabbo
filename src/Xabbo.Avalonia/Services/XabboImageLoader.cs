using System;
using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, DateTime> _failureCache = [];
    private readonly ConcurrentDictionary<string, Bitmap> _syncBitmapCache = [];

    static XabboImageLoader()
    {
        HttpClient client = new();
        client.DefaultRequestHeaders.Add("User-Agent", "xabbo");
        Instance = new XabboImageLoader(client, false);
    }

    public bool TryGetBitmap(string url, out Bitmap? bitmap)
        => _syncBitmapCache.TryGetValue(url, out bitmap);

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
            // Fast path: already cached synchronously
            if (_syncBitmapCache.TryGetValue(url, out Bitmap? cached))
                return cached;

            if (_failureCache.TryGetValue(url, out DateTime failureTime) &&
                (DateTime.Now - failureTime).TotalMinutes < 5)
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
                _syncBitmapCache[url] = image;
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
