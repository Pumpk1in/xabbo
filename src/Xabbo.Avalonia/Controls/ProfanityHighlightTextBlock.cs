using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using Xabbo.ViewModels;

namespace Xabbo.Avalonia.Controls;

/// <summary>
/// A TextBlock that highlights profanity segments in red with underline.
/// </summary>
public class ProfanityHighlightTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<MessageSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, IReadOnlyList<MessageSegment>?>(nameof(Segments));

    public static readonly StyledProperty<string?> FallbackTextProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, string?>(nameof(FallbackText));

    public static readonly StyledProperty<bool> IsWhisperProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, bool>(nameof(IsWhisper));

    public static readonly StyledProperty<string?> UsernameProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, string?>(nameof(Username));

    public static readonly StyledProperty<string?> WhisperRecipientProperty =
        AvaloniaProperty.Register<ProfanityHighlightTextBlock, string?>(nameof(WhisperRecipient));

    /// <summary>
    /// The message segments to display with profanity highlighting.
    /// </summary>
    public IReadOnlyList<MessageSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>
    /// The fallback text to display when Segments is null or empty.
    /// </summary>
    public string? FallbackText
    {
        get => GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    /// <summary>
    /// Whether this is a whisper message (applies whisper styling).
    /// </summary>
    public bool IsWhisper
    {
        get => GetValue(IsWhisperProperty);
        set => SetValue(IsWhisperProperty, value);
    }

    /// <summary>
    /// The username to display before the message.
    /// </summary>
    public string? Username
    {
        get => GetValue(UsernameProperty);
        set => SetValue(UsernameProperty, value);
    }

    /// <summary>
    /// The whisper recipient name for outgoing whispers (displays "Name -> Recipient: msg").
    /// </summary>
    public string? WhisperRecipient
    {
        get => GetValue(WhisperRecipientProperty);
        set => SetValue(WhisperRecipientProperty, value);
    }

    private static readonly IBrush ProfanityBrush = new SolidColorBrush(Color.Parse("#FF4444"));
    private static readonly IBrush WhisperBrush = new SolidColorBrush(Color.Parse("#a495ff"));
    private static readonly IBrush UsernameBrush = new SolidColorBrush(Color.Parse("#9995ff"));

    // DEBUG: only log when layout activity becomes dangerous (>50 measures/s)
    private static readonly string _logPath = @"C:\Users\odele\OneDrive\Bureau\xabbo_layout_debug.log";
    private static int _globalMeasureCount;
    private static DateTime _lastResetTime = DateTime.UtcNow;
    private static bool _alarmLogged;
    private static DateTime _lastBurstEndTime = DateTime.UtcNow;
    private static bool _pruneScheduled;
    private static int _consecutiveBurstCount;
    private static int _purgeCount;

    /// <summary>
    /// Assigned by ChatPage to trigger a KeepLastMessages() purge when a layout loop is detected.
    /// </summary>
    public static Action? PruneAction { get; set; }
    public static Action? PurgeTotalAction { get; set; }

    private static void Log(string msg)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch { }
    }

    static ProfanityHighlightTextBlock()
    {
        try { File.WriteAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Session started{Environment.NewLine}"); } catch { }
        SegmentsProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.ScheduleUpdateInlines());
        FallbackTextProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.ScheduleUpdateInlines());
        IsWhisperProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.ScheduleUpdateInlines());
        UsernameProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.ScheduleUpdateInlines());
        WhisperRecipientProperty.Changed.AddClassHandler<ProfanityHighlightTextBlock>((x, _) => x.ScheduleUpdateInlines());
    }

    private bool _updatePending;
    private (IReadOnlyList<MessageSegment>? segments, string? fallback, bool whisper, string? username, string? recipient) _lastState;

    private void ScheduleUpdateInlines()
    {
        if (_updatePending) return;
        _updatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _updatePending = false;
            UpdateInlines();
        }, DispatcherPriority.Loaded);
    }

    // Inserts zero-width spaces every N chars in runs with no natural break points,
    // preventing Avalonia's TextWrapping from entering an infinite layout loop.
    private static string InjectWordBreaks(string text, int every = 10)
    {
        if (text.Length <= every || text.Contains(' ')) return text;
        var sb = new System.Text.StringBuilder(text.Length + text.Length / every);
        for (int i = 0; i < text.Length; i++)
        {
            sb.Append(text[i]);
            if ((i + 1) % every == 0 && i + 1 < text.Length)
                sb.Append('\u200B');
        }
        return sb.ToString();
    }

    private void UpdateInlines()
    {
        var segments = Segments;
        var fallback = FallbackText;
        var username = Username;

        // Skip update when control has no content (e.g. virtualizer recycling with null bindings).
        // This prevents dozens of InvalidateMeasure() calls from piling up in a single render cycle.
        if (segments is null or { Count: 0 } && string.IsNullOrEmpty(fallback) && string.IsNullOrEmpty(username))
            return;

        var state = (segments, fallback, IsWhisper, username, WhisperRecipient);
        if (state == _lastState) return;
        _lastState = state;

        var runs = new List<Inline>();

        // Add username prefix if provided
        if (!string.IsNullOrEmpty(username))
        {
            var boldFont = new FontFamily("fonts:#IBM Plex Sans");
            var usernameRun = new Run(InjectWordBreaks(username))
            {
                FontFamily = boldFont,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                Foreground = IsWhisper ? WhisperBrush : UsernameBrush
            };
            runs.Add(usernameRun);

            // For outgoing whispers, show "Name -> Recipient: msg" (all italic/purple, no bold)
            if (!string.IsNullOrEmpty(WhisperRecipient))
            {
                runs.Add(new Run($" -> {InjectWordBreaks(WhisperRecipient)}")
                {
                    FontStyle = FontStyle.Italic,
                    Foreground = WhisperBrush,
                    FontSize = 13
                });
            }

            var separatorRun = new Run(":  ")
            {
                FontSize = 13,
                Foreground = IsWhisper ? WhisperBrush : UsernameBrush
            };
            if (IsWhisper)
            {
                separatorRun.FontStyle = FontStyle.Italic;
            }
            runs.Add(separatorRun);
        }

        if (segments is null or { Count: 0 })
        {
            var fallbackRun = new Run(InjectWordBreaks(fallback ?? string.Empty));
            if (IsWhisper)
            {
                fallbackRun.FontStyle = FontStyle.Italic;
                fallbackRun.Foreground = WhisperBrush;
            }
            runs.Add(fallbackRun);
        }
        else
        {
            foreach (var segment in segments)
            {
                var run = new Run(InjectWordBreaks(segment.Text));

                if (segment.IsProfanity)
                {
                    run.Foreground = ProfanityBrush;
                    run.TextDecorations = global::Avalonia.Media.TextDecorations.Underline;
                    run.FontWeight = FontWeight.SemiBold;
                }
                else if (IsWhisper)
                {
                    run.FontStyle = FontStyle.Italic;
                    run.Foreground = WhisperBrush;
                }

                runs.Add(run);
            }
        }

        _measureDirty = true;
        Inlines?.Clear();
        Inlines?.AddRange(runs);
    }

    // Cache measured size to prevent redundant text wrapping calculations
    // across consecutive layout passes with the same available width.
    private double _lastMeasureWidth = -1;
    private Size _lastMeasureResult;
    private bool _measureDirty = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastResetTime).TotalSeconds > 1.0)
        {
            if (_globalMeasureCount > 250)
            {
                _consecutiveBurstCount++;
                var msSinceLastBurst = (_lastResetTime - _lastBurstEndTime).TotalMilliseconds;
                Log($"BURST ended: {_globalMeasureCount} measures in last second (consecutive: {_consecutiveBurstCount}, gap: {msSinceLastBurst:F0}ms)");

                // 3+ consecutive bursts = layout loop not converging → trigger purge
                if (_consecutiveBurstCount >= 3 && !_pruneScheduled)
                {
                    _pruneScheduled = true;
                    _purgeCount++;
                    try
                    {
                        if (_purgeCount >= 2 && PurgeTotalAction != null)
                        {
                            Log($"LOOP DETECTED ({_consecutiveBurstCount} consecutive bursts, purge #{_purgeCount}): clearing ALL messages");
                            PurgeTotalAction.Invoke();
                            Log($"TOTAL PURGE executed successfully");
                        }
                        else if (PruneAction != null)
                        {
                            Log($"LOOP DETECTED ({_consecutiveBurstCount} consecutive bursts, purge #{_purgeCount}): keeping last 150 messages");
                            PruneAction.Invoke();
                            Log($"PURGE executed successfully");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"PURGE failed: {ex.Message}");
                    }
                    _pruneScheduled = false;
                    _consecutiveBurstCount = 0;
                }

                _lastBurstEndTime = _lastResetTime;
            }
            else
            {
                // Burst didn't reach threshold — reset counters
                _consecutiveBurstCount = 0;
                _purgeCount = 0;
            }
            _globalMeasureCount = 0;
            _lastResetTime = now;
            _alarmLogged = false;
        }
        _globalMeasureCount++;

        // Log the first time we cross the danger threshold in a given second
        if (_globalMeasureCount == 250 && !_alarmLogged)
        {
            _alarmLogged = true;
            var textPreview = FallbackText?.Substring(0, Math.Min(FallbackText?.Length ?? 0, 40)) ?? "(null)";
            Log($"ALARM: 250+ measures/s reached! w={availableSize.Width:F1} '{textPreview}'");
        }

        // Log every measure above 50/s with details
        if (_globalMeasureCount > 250)
        {
            var textPreview = FallbackText?.Substring(0, Math.Min(FallbackText?.Length ?? 0, 30)) ?? "(null)";
            var cached = !_measureDirty && Math.Abs(availableSize.Width - _lastMeasureWidth) < 3.0;
            Log($"  [{_globalMeasureCount}] w={availableSize.Width:F1} (was {_lastMeasureWidth:F1}) cached={cached} dirty={_measureDirty} '{textPreview}'");
        }

        if (!_measureDirty && Math.Abs(availableSize.Width - _lastMeasureWidth) < 3.0)
            return _lastMeasureResult;

        _measureDirty = false;
        _lastMeasureWidth = availableSize.Width;
        _lastMeasureResult = base.MeasureOverride(availableSize);
        return _lastMeasureResult;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UpdateInlines();
    }
}
