using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Sistema_de_Stock.Data;
using Sistema_de_Stock.Services;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Storage;
using Microsoft.Maui;

namespace Sistema_de_Stock
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // Habilita el soporte para DateTime locales en PostgreSQL (Npgsql)
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // ── TenantService (singleton, mantiene la sesión activa) ────────
            builder.Services.AddSingleton<TenantService>();
            builder.Services.AddSingleton<ConnectivityService>();
            builder.Services.AddSingleton<SupabaseAuthService>();

            // ── StockOnlineContext (Npgsql → Supabase PostgreSQL) ─────────────
            builder.Services.AddDbContext<StockOnlineContext>(options =>
                options.UseNpgsql(SupabaseConfig.PoolerConnectionString),
                ServiceLifetime.Transient);

            // ── StockCacheContext (SQLite → cache offline local) ──────────────
            var cacheDbPath = Path.Combine(FileSystem.AppDataDirectory, "stock_cache.db");
            builder.Services.AddDbContext<StockCacheContext>(options =>
                options.UseSqlite($"Data Source={cacheDbPath}"),
                ServiceLifetime.Transient);

            // ── Servicios de la aplicación ────────────────────────────────
            builder.Services.AddTransient<Sistema_de_Stock.Services.DataService>();
            builder.Services.AddTransient<Sistema_de_Stock.Services.CacheService>();
            builder.Services.AddSingleton<Sistema_de_Stock.Services.ReportService>();
            builder.Services.AddSingleton<Sistema_de_Stock.Services.NotificationService>();
            builder.Services.AddSingleton<Sistema_de_Stock.Services.PdfService>();
            builder.Services.AddSingleton<Sistema_de_Stock.Services.BackupService>();

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

            var app = builder.Build();

#if DEBUG
            // Asegurar que las DBs estén listas
            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    // La DB local caché siempre debe existir
                    var cacheDb = scope.ServiceProvider.GetRequiredService<StockCacheContext>();
                    cacheDb.Database.EnsureCreated();

                    // La DB online (Supabase) ya ha sido inicializada manualmente.
                    // Solo verificamos conexión o dejamos que EF Core maneje la creación perezosa.
                    // var onlineDb = scope.ServiceProvider.GetRequiredService<StockOnlineContext>();
                    // await onlineDb.Database.CanConnectAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error inicializando DB: {ex.Message}");
                }
            }
#endif

            return app;
        }
    }
}
