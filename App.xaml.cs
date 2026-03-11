using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using TaskCore.Services;
using System.Diagnostics;

namespace TaskCore
{
    public partial class App : Application
    {
        private static Mutex? _instanceMutex;
        private static bool _ownsMutex;
        private const string MutexName = "TaskCore_SingleInstance_Mutex";

        private static void Log(string msg)
        {
            TaskCoreLogger.Info("startup", msg);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Cleanup old logs on startup
            TaskCoreLogger.CleanupOldLogs();
            TaskCoreLogger.SessionTracking.Startup();

            // Setup global exception handlers FIRST - before anything else
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                TaskCoreLogger.Lifecycle.UnhandledException(args.ExceptionObject?.ToString() ?? "Unknown");
                MessageBox.Show($"Fatal Error: {args.ExceptionObject}", "TaskCore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            DispatcherUnhandledException += (s, args) =>
            {
                TaskCoreLogger.Error("ui", $"DispatcherUnhandledException: {args.Exception}");
                args.Handled = true;
                MessageBox.Show($"UI Error: {args.Exception.Message}", "TaskCore Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                TaskCoreLogger.Error("task", $"UnobservedTaskException: {args.Exception}");
                args.SetObserved();
            };

            Log($"OnStartup BEGIN - Elevated: {e.Args.Contains("--elevated")}");

            // Setup native DLL resolution for SkiaSharp - critical for MSI-installed apps
            SetupNativeDllResolver();

            // Check if watchdog mode is requested via command line or settings
            var settings = AppSettingsService.Load();
            var watchdogMode = settings.WatchdogMode || e.Args.Contains("--watchdog");
            var isElevatedRelaunch = e.Args.Contains("--elevated");
            
            // Use different mutex for watchdog mode to allow it running alongside regular mode
            var mutexName = watchdogMode ? "TaskCore_Watchdog_Instance" : MutexName;
            
            _instanceMutex = new Mutex(true, mutexName, out bool createdNew);
            
            // Skip mutex check for elevated relaunch or watchdog mode
            if (!createdNew && !watchdogMode && !isElevatedRelaunch)
            {
                // Only block regular mode duplicates. Watchdog mode can have multiple instances
                try { _instanceMutex.Close(); } catch { }
                _instanceMutex = null;
                _ownsMutex = false;
                
                // Bring existing window to foreground
                try
                {
                    var existingProcess = System.Diagnostics.Process.GetProcessesByName("TaskCore")
                        .FirstOrDefault(p => p.Id != System.Diagnostics.Process.GetCurrentProcess().Id);
                    
                    if (existingProcess != null)
                    {
                        NativeMethods.SetForegroundWindow(existingProcess.MainWindowHandle);
                    }
                }
                catch { }
                
                Shutdown();
                return;
            }

            // We own the mutex
            _ownsMutex = true;

            base.OnStartup(e);

            // Initialize theme with saved accent color
            ThemeService.ApplyAccentColor(settings.AccentColor ?? "#FF9A4DFF");

            // LiveCharts/SkiaSharp initialization - skip in watchdog mode (no graphs shown)
            // This saves ~30-50MB memory in minimal mode
            if (!watchdogMode)
            {
                LiveCharts.Configure(config =>
                    config.AddSkiaSharp()
                );
            }

            // UsageTrackingService runs in ALL modes - records session data for dashboard
            // even when in watchdog mode, so data is available when viewing dashboard later
            _ = UsageTrackingService.Instance;

            if (settings.AutomationWatchdogEnabled)
            {
                try
                {
                    _ = AutomationWatchdogService.Instance;
                }
                catch
                {
                }
            }

            // RulesService initializes itself via lazy singleton when needed
            // No need to force init - it only runs when rules exist
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // Initialize tray icon once when window is activated
            if (MainWindow is not null && TrayIconService.Instance is { IsInitialized: false })
            {
                TrayIconService.Instance.Initialize(MainWindow);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TaskCoreLogger.Lifecycle.OnExitCalled();
            
            // Save running sessions before exit
            try
            {
                UsageTrackingService.Instance.Dispose();
            }
            catch { }

            // Only release mutex if we actually acquired it
            if (_ownsMutex && _instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch { }
                
                try
                {
                    _instanceMutex.Close();
                }
                catch { }
                
                _instanceMutex = null;
                _ownsMutex = false;
            }
            
            base.OnExit(e);
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);
        }

        private static void SetupNativeDllResolver()
        {
            try
            {
                var appDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(appDir))
                {
                    Log("WARNING: Could not determine app directory for DLL resolver");
                    return;
                }

                Log($"Setting up native DLL resolver for: {appDir}");

                // Set DLL directory for Windows LoadLibrary calls
                SetDllDirectory(appDir);

                // Subscribe to assembly resolution for native libs
                NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKImageInfo).Assembly, (libraryName, assembly, searchPath) =>
                {
                    Log($"Resolving native library: {libraryName}");

                    // Handle SkiaSharp native library
                    if (libraryName.Contains("SkiaSharp") || libraryName == "libSkiaSharp")
                    {
                        var skiaPath = Path.Combine(appDir, "libSkiaSharp.dll");
                        if (File.Exists(skiaPath))
                        {
                            Log($"Found libSkiaSharp.dll at: {skiaPath}");
                            try
                            {
                                return NativeLibrary.Load(skiaPath);
                            }
                            catch (Exception ex)
                            {
                                Log($"Error loading libSkiaSharp.dll: {ex.Message}");
                            }
                        }
                        else
                        {
                            Log($"libSkiaSharp.dll NOT FOUND at: {skiaPath}");
                        }
                    }

                    // Handle HarfBuzz native library
                    if (libraryName.Contains("HarfBuzzSharp") || libraryName == "libHarfBuzzSharp")
                    {
                        var harfPath = Path.Combine(appDir, "libHarfBuzzSharp.dll");
                        if (File.Exists(harfPath))
                        {
                            Log($"Found libHarfBuzzSharp.dll at: {harfPath}");
                            try
                            {
                                return NativeLibrary.Load(harfPath);
                            }
                            catch (Exception ex)
                            {
                                Log($"Error loading libHarfBuzzSharp.dll: {ex.Message}");
                            }
                        }
                    }

                    // Let default resolution handle it
                    return IntPtr.Zero;
                });

                Log("Native DLL resolver setup complete");
            }
            catch (Exception ex)
            {
                Log($"ERROR setting up native DLL resolver: {ex}");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);
    }
}
