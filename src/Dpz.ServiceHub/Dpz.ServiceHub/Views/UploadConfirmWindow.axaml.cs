using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dpz.ServiceHub.Views;

public partial class UploadConfirmWindow : Window
{
    public UploadConfirmWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            PrefixBox.Text = RemotePrefix;
        };
    }

    public string RemotePrefix { get; set; } = string.Empty;

    public bool DialogResult { get; private set; }

    private void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        RemotePrefix = PrefixBox.Text ?? string.Empty;
        DialogResult = true;
        Close();
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
