using System.Text.RegularExpressions;
using Avalonia.Media;

namespace Dpz.ServiceHub.Models;

/// <summary>
/// ANSI 颜色代码解析器
/// </summary>
public static partial class AnsiColorParser
{
    [GeneratedRegex(@"\x1B\[([0-9;]+)m")]
    private static partial Regex AnsiCodeRegex();

    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
    private static readonly SolidColorBrush BlackBrush = new(Colors.Black);
    private static readonly SolidColorBrush RedBrush = new(Colors.Red);
    private static readonly SolidColorBrush GreenBrush = new(Colors.Green);
    private static readonly SolidColorBrush YellowBrush = new(Colors.Yellow);
    private static readonly SolidColorBrush BlueBrush = new(Colors.Blue);
    private static readonly SolidColorBrush MagentaBrush = new(Colors.Magenta);
    private static readonly SolidColorBrush CyanBrush = new(Colors.Cyan);
    private static readonly SolidColorBrush DarkGrayBrush = new(Colors.DarkGray);
    private static readonly SolidColorBrush LightCoralBrush = new(Colors.LightCoral);
    private static readonly SolidColorBrush LightGreenBrush = new(Colors.LightGreen);
    private static readonly SolidColorBrush LightYellowBrush = new(Colors.LightYellow);
    private static readonly SolidColorBrush LightBlueBrush = new(Colors.LightBlue);
    private static readonly SolidColorBrush PlumBrush = new(Colors.Plum);
    private static readonly SolidColorBrush LightCyanBrush = new(Colors.LightCyan);
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(230, 230, 230));

    [GeneratedRegex(@"\x1B\[[0-9;]+m")]
    private static partial Regex HasAnsiRegex();

    /// <summary>
    /// 解析 ANSI 颜色代码
    /// </summary>
    public static List<ColoredTextSegment> Parse(string text)
    {
        if (!HasAnsiRegex().IsMatch(text))
        {
            return ParsePlainText(text);
        }

        var segments = new List<ColoredTextSegment>();
        var currentColor = NeutralBrush;
        var lastIndex = 0;

        var matches = AnsiCodeRegex().Matches(text);

        foreach (Match match in matches)
        {
            // 添加匹配前的文本
            if (match.Index > lastIndex)
            {
                var textSegment = text.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrEmpty(textSegment))
                {
                    segments.Add(new ColoredTextSegment(textSegment, currentColor));
                }
            }

            // 解析颜色代码
            var codes = match.Groups[1].Value.Split(';');
            currentColor = ParseColorCode(codes, currentColor);

            lastIndex = match.Index + match.Length;
        }

        // 添加剩余文本
        if (lastIndex < text.Length)
        {
            var remainingText = text.Substring(lastIndex);
            if (!string.IsNullOrEmpty(remainingText))
            {
                segments.Add(new ColoredTextSegment(remainingText, currentColor));
            }
        }

        return segments;
    }

    /// <summary>
    /// 解析 ANSI 文本并按行拆分。
    /// </summary>
    public static List<List<ColoredTextSegment>> ParseToLines(string text)
    {
        var lines = new List<List<ColoredTextSegment>> { new List<ColoredTextSegment>() };
        var segments = Parse(text);

        foreach (var segment in segments)
        {
            var parts = segment.Text.Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (!string.IsNullOrEmpty(part))
                {
                    lines[^1].Add(new ColoredTextSegment(part, segment.Color));
                }

                if (i < parts.Length - 1)
                {
                    lines.Add(new List<ColoredTextSegment>());
                }
            }
        }

        return lines;
    }

    private static SolidColorBrush ParseColorCode(string[] codes, SolidColorBrush currentColor)
    {
        foreach (var code in codes)
        {
            if (!int.TryParse(code, out var colorCode))
            {
                continue;
            }

            return colorCode switch
            {
                0 => NeutralBrush,            // 重置
                30 => BlackBrush,             // 黑色
                31 => RedBrush,               // 红色
                32 => GreenBrush,             // 绿色
                33 => YellowBrush,            // 黄色
                34 => BlueBrush,              // 蓝色
                35 => MagentaBrush,           // 品红
                36 => CyanBrush,              // 青色
                37 => WhiteBrush,             // 白色
                90 => DarkGrayBrush,          // 亮黑色
                91 => LightCoralBrush,        // 亮红色
                92 => LightGreenBrush,        // 亮绿色
                93 => LightYellowBrush,       // 亮黄色
                94 => LightBlueBrush,         // 亮蓝色
                95 => PlumBrush,              // 亮品红
                96 => LightCyanBrush,         // 亮青色
                97 => WhiteBrush,             // 亮白色
                _ => currentColor
            };
        }

        return currentColor;
    }

    /// <summary>
    /// 移除 ANSI 颜色代码
    /// </summary>
    public static string RemoveAnsiCodes(string text)
    {
        return AnsiCodeRegex().Replace(text, string.Empty);
    }

    private static List<ColoredTextSegment> ParsePlainText(string text)
    {
        if (text.Contains("[ERR]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase))
        {
            return [new ColoredTextSegment(text, LightCoralBrush)];
        }

        if (text.Contains("[WRN]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[WARN]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase))
        {
            return [new ColoredTextSegment(text, LightYellowBrush)];
        }

        if (text.Contains("[INF]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[INFO]", StringComparison.OrdinalIgnoreCase))
        {
            return [new ColoredTextSegment(text, LightCyanBrush)];
        }

        if (text.Contains("[DBG]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("[DEBUG]", StringComparison.OrdinalIgnoreCase)
            || text.Contains("trace", StringComparison.OrdinalIgnoreCase))
        {
            return [new ColoredTextSegment(text, DarkGrayBrush)];
        }

        return [new ColoredTextSegment(text, NeutralBrush)];
    }
}

/// <summary>
/// 带颜色的文本片段
/// </summary>
public sealed record ColoredTextSegment(string Text, IBrush Color);
