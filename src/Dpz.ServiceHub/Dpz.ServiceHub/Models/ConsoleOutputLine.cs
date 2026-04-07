using System.Collections.ObjectModel;

namespace Dpz.ServiceHub.Models;

/// <summary>
/// 控制台输出的一行
/// </summary>
public sealed class ConsoleOutputLine
{
    public ObservableCollection<ColoredTextSegment> Segments { get; } = [];
}
