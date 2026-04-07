using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Dpz.ServiceHub.Controls;

public sealed partial class ServiceLogColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush TimestampBrush = new SolidColorBrush(Color.FromRgb(145, 145, 145));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 108, 108));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(255, 205, 64));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.FromRgb(92, 219, 255));
    private static readonly IBrush DebugBrush = new SolidColorBrush(Color.FromRgb(148, 148, 148));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.FromRgb(100, 220, 130));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromRgb(221, 112, 255));
    private static readonly IBrush RouteBrush = new SolidColorBrush(Color.FromRgb(103, 201, 255));
    private static readonly IBrush AddressBrush = new SolidColorBrush(Color.FromRgb(255, 170, 85));

    [GeneratedRegex(@"\[(ERR|ERROR)\]", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorRegex();

    [GeneratedRegex(@"\[(WRN|WARN|WARNING)\]", RegexOptions.IgnoreCase)]
    private static partial Regex WarnRegex();

    [GeneratedRegex(@"\[(INF|INFO)\]", RegexOptions.IgnoreCase)]
    private static partial Regex InfoRegex();

    [GeneratedRegex(@"\[(DBG|DEBUG|TRC|TRACE)\]", RegexOptions.IgnoreCase)]
    private static partial Regex DebugRegex();

    [GeneratedRegex(@"\[(\d{2}:\d{2}:\d{2})\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\b(GET|POST|PUT|DELETE|PATCH|CONNECT|OPTIONS|HEAD)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HttpVerbRegex();

    [GeneratedRegex(@"(/[-a-zA-Z0-9_./?=&%]+)")]
    private static partial Regex RouteRegex();

    [GeneratedRegex(@"\b\d{3}\b")]
    private static partial Regex StatusCodeRegex();

    [GeneratedRegex(@"\b\d+(?:\.\d+)?\s*ms\b", RegexOptions.IgnoreCase)]
    private static partial Regex ElapsedRegex();

    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{1,3}){3}:\d{2,5}\b")]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"\b(started|starting|stopped|stopping|shutdown|successfully|listening)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LifecycleRegex();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (CurrentContext?.Document == null)
        {
            return;
        }

        var text = CurrentContext.Document.GetText(line.Offset, line.Length);
        var lowerText = text.ToLowerInvariant();

        foreach (Match match in TimestampRegex().Matches(text))
        {
            ColorRange(line, match.Index, match.Length, TimestampBrush);
        }

        foreach (Match match in HttpVerbRegex().Matches(text))
        {
            ColorRange(line, match.Index, match.Length, AccentBrush);
        }

        foreach (Match match in RouteRegex().Matches(text))
        {
            ColorRange(line, match.Index, match.Length, RouteBrush);
        }

        foreach (Match match in AddressRegex().Matches(text))
        {
            ColorRange(line, match.Index, match.Length, AddressBrush);
        }

        foreach (Match match in LifecycleRegex().Matches(text))
        {
            ColorRange(line, match.Index, match.Length, SuccessBrush);
        }

        if (ErrorRegex().IsMatch(text))
        {
            ColorRange(line, 0, text.Length, ErrorBrush);
        }
        else if (WarnRegex().IsMatch(text))
        {
            ColorRange(line, 0, text.Length, WarnBrush);
        }
        else if (InfoRegex().IsMatch(text))
        {
            ColorRange(line, 0, text.Length, InfoBrush);
        }
        else if (DebugRegex().IsMatch(text) || lowerText.Contains("trace", StringComparison.Ordinal))
        {
            ColorRange(line, 0, text.Length, DebugBrush);
        }

        foreach (Match match in StatusCodeRegex().Matches(text))
        {
            var code = int.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture);
            if (code >= 500)
            {
                ColorRange(line, match.Index, match.Length, ErrorBrush);
            }
            else if (code >= 400)
            {
                ColorRange(line, match.Index, match.Length, WarnBrush);
            }
            else if (code >= 200 && code < 300)
            {
                ColorRange(line, match.Index, match.Length, SuccessBrush);
            }
            else
            {
                ColorRange(line, match.Index, match.Length, AccentBrush);
            }
        }

        foreach (Match match in ElapsedRegex().Matches(text))
        {
            ColorRange(line, match.Index, match.Length, AccentBrush);
        }
    }

    private void ColorRange(DocumentLine line, int start, int length, IBrush brush)
    {
        if (length <= 0)
        {
            return;
        }

        ChangeLinePart(line.Offset + start, line.Offset + start + length, visualLine =>
        {
            visualLine.TextRunProperties.SetForegroundBrush(brush);
        });
    }
}
