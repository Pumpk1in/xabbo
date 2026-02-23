using System.Collections.Generic;
using System.Globalization;
using System;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xabbo.Models;

namespace Xabbo.Avalonia.Converters;

public class BubbleImageConverter : IValueConverter
{
    private static readonly Dictionary<int, Bitmap> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ChatBubbleOption opt)
            return null;

        if (_cache.TryGetValue(opt.Id, out var cached))
            return cached;

        string folder = opt.IsOther ? "chat-bubbles/other" : "chat-bubbles";
        string file = $"bubble_{opt.Id}.png";
        var uri = new Uri($"avares://Xabbo.Avalonia/Assets/Images/{folder}/{file}");
        var bmp = new Bitmap(AssetLoader.Open(uri));
        _cache[opt.Id] = bmp;
        return bmp;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
