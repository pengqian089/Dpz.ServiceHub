using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dpz.ServiceHub.Views;

public partial class SettingsWindow : Window
{
    public bool DialogResult { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
