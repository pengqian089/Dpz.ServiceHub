using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dpz.ServiceHub.Views;

public enum ShutdownAction
{
    Cancel,
    StopServicesAndExit,
}

public partial class ShutdownConfirmWindow : Window
{
    public ShutdownConfirmWindow()
    {
        InitializeComponent();
    }

    public void SetServiceNames(IEnumerable<string> names)
    {
        var textBlock = this.FindControl<TextBlock>("ServiceListTextBlock");
        if (textBlock == null)
        {
            return;
        }

        var lines = names.Select((name, index) => $"{index + 1}. {name}");
        textBlock.Text = string.Join(Environment.NewLine, lines);
    }

    private void OnStopAndExitClick(object? sender, RoutedEventArgs e)
    {
        Close(ShutdownAction.StopServicesAndExit);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(ShutdownAction.Cancel);
    }
}
