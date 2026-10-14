using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Avalonia;
using Avalonia.Threading;
using Serilog;

namespace Dpz.ServiceHub
{
    internal sealed class Program
    {
        private const string SingleInstanceMutexName = "Global\\Dpz.ServiceHub.Singleton";
        private static Mutex? _singleInstanceMutex;

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            ConfigureLogging();
            RegisterGlobalExceptionHandlers();

            if (ShouldRestartAsAdministrator(args))
            {
                return;
            }

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                SingleInstanceMutexName,
                out var isNew
            );
            if (!isNew)
            {
                return;
            }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
                throw;
            }
            finally
            {
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
                Log.CloseAndFlush();
            }
        }

        private static bool ShouldRestartAsAdministrator(string[] args)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return false;
            }

            var settings = new Services.AppSettingsStore().Load();
            if (!settings.RunAsAdministrator || IsRunningAsAdministrator())
            {
                return false;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                Log.Warning("Unable to restart as administrator because the executable path is unavailable.");
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = true,
                    Verb = "runas",
                };

                foreach (var arg in args)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                Process.Start(startInfo);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Log.Warning(ex, "Administrator permission was not granted; continuing without elevation.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restart application as administrator.");
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void ConfigureLogging()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dpz.ServiceHub",
                "logs"
            );
            Directory.CreateDirectory(appDataPath);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Warning()
                .WriteTo.File(
                    path: Path.Combine(appDataPath, "servicehub-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 10 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();
        }

        private static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    Log.Fatal(ex, "Unhandled exception in AppDomain.");
                }
                else
                {
                    Log.Fatal(
                        "Unhandled non-exception object in AppDomain: {ExceptionObject}",
                        e.ExceptionObject
                    );
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error(e.Exception, "Unobserved task exception.");
                e.SetObserved();
            };

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                if (IsKnownWebView2FocusException(e.Exception))
                {
                    Log.Warning(
                        e.Exception,
                        "Ignored known WebView2 focus exception triggered during window activation/focus transition."
                    );
                    e.Handled = true;
                    return;
                }

                Log.Error(e.Exception, "Unhandled UI thread exception.");
            };
        }

        private static bool IsKnownWebView2FocusException(Exception ex)
        {
            if (ex is not ArgumentException)
            {
                return false;
            }

            if (!string.Equals(ex.Message, "Value does not fall within the expected range."))
            {
                return false;
            }

            var stackTrace = ex.StackTrace;
            if (string.IsNullOrWhiteSpace(stackTrace))
            {
                return false;
            }

            return stackTrace.Contains(
                    "ICoreWebView2Controller.MoveFocus",
                    StringComparison.Ordinal
                )
                && stackTrace.Contains(
                    "Avalonia.Controls.NativeWebView.OnGotFocus",
                    StringComparison.Ordinal
                );
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}
