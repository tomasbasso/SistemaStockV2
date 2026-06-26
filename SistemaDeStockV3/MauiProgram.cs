using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using SistemaDeStockV3.Data;
using SistemaDeStockV3.Services;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Storage;
using Microsoft.Maui;
using System.IO;

namespace SistemaDeStockV3
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // ── Base de Datos Local (SQLite) ──────────────
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "stock.db");
            builder.Services.AddDbContext<StockDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"),
                ServiceLifetime.Transient);

            // ── Servicios de la aplicación ────────────────────────────────
            builder.Services.AddTransient<SistemaDeStockV3.Services.DataService>();
            builder.Services.AddSingleton<SistemaDeStockV3.Services.ReportService>();
            builder.Services.AddSingleton<SistemaDeStockV3.Services.NotificationService>();
            builder.Services.AddSingleton<SistemaDeStockV3.Services.PdfService>();
            builder.Services.AddSingleton<SistemaDeStockV3.Services.BackupService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            // ── Configuración de Ventana (Pantalla Completa / Maximizado) ──
            Microsoft.Maui.Handlers.WindowHandler.Mapper.AppendToMapping(nameof(IWindow), (handler, view) =>
            {
#if WINDOWS
                var nativeWindow = handler.PlatformView;
                nativeWindow.Activate();
                IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
                Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                
                // Configurar el ícono de la aplicación en Windows
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_appicon.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }

                // Usar Maximized (O usar FullScreen para modo kiosco/F11)
                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
#endif
            });

            // Backup de cierre silencioso
            builder.ConfigureLifecycleEvents(events =>
            {
#if ANDROID
                events.AddAndroid(android => android.OnStop(activity =>
                {
                    var folder = Preferences.Get("Backup.TargetFolder", string.Empty);
                    if (string.IsNullOrWhiteSpace(folder)) return;

                    var services = IPlatformApplication.Current?.Services;
                    var backup = services?.GetService<BackupService>();
                    if (backup == null) return;

                    _ = Task.Run(async () =>
                    {
                        try { await backup.ExecuteClosingBackupAsync(folder); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Backup cierre Android] {ex.Message}"); }
                    });
                }));
#endif

#if WINDOWS
                events.AddWindows(windows => windows.OnWindowCreated(window =>
                {
                    window.Closed += (_, __) =>
                    {
                        var folder = Preferences.Get("Backup.TargetFolder", string.Empty);
                        if (string.IsNullOrWhiteSpace(folder)) return;

                        var services = IPlatformApplication.Current?.Services;
                        var backup = services?.GetService<BackupService>();
                        if (backup == null) return;

                        _ = Task.Run(async () =>
                        {
                            try { await backup.ExecuteClosingBackupAsync(folder); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Backup cierre Windows] {ex.Message}"); }
                        });
                    };
                }));
#endif
            });

            return builder.Build();
        }
    }
}
