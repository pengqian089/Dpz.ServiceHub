using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Dpz.ServiceHub.ViewModels;

namespace Dpz.ServiceHub.Views;

public partial class ServiceEditWindow : Window
{
    public ServiceEditWindow()
    {
        InitializeComponent();
    }

    public bool DialogResult { get; private set; }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ServiceEditViewModel vm)
        {
            // 验证必填字段
            if (string.IsNullOrWhiteSpace(vm.Name))
            {
                // TODO: 显示错误消息
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.WorkingDirectory))
            {
                // TODO: 显示错误消息
                return;
            }

            if (string.IsNullOrWhiteSpace(vm.Executable))
            {
                // TODO: 显示错误消息
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = "选择工作目录",
            AllowMultiple = false
        };

        var result = await StorageProvider.OpenFolderPickerAsync(options);

        if (result.Count > 0 && DataContext is ServiceEditViewModel vm)
        {
            vm.WorkingDirectory = result[0].Path.LocalPath;
        }
    }
}
