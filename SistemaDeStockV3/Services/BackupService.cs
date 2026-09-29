using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Storage;
using SistemaDeStockV3.Models;
using System.Globalization;

namespace SistemaDeStockV3.Services
{
    public class BackupService
    {
        private const string TargetFolderKey = "Backup.TargetFolder";
        private const string LastRunUtcKey = "Backup.LastRunUtc";
        private const string LastCloseUtcKey = "Backup.LastCloseUtc";
        private const int RetentionCount = 15;

        // Una base vacía (ej. el programa nuevo recién copiado, antes de restaurar) no se respalda:
        // el cierre pisaría Backup_Stock_UltimoCierre.db y la rotación terminaría borrando respaldos reales.
        private const string MensajeBaseVacia = "La base de datos está vacía: no se generó el respaldo para no pisar respaldos anteriores.";

        private readonly string _dbPath;

        /// <summary>
        /// Carpeta que se usa mientras el usuario no elija otra, para que haya respaldos
        /// automáticos desde el primer día aunque nunca entre a Configuración.
        /// </summary>
        public string CarpetaPredeterminada { get; }

        public BackupService()
        {
            _dbPath = Path.Combine(FileSystem.AppDataDirectory, "stock.db");

            var documentos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            CarpetaPredeterminada = string.IsNullOrWhiteSpace(documentos)
                ? Path.Combine(FileSystem.AppDataDirectory, "Respaldos")
                : Path.Combine(documentos, "Sistema de Stock - Respaldos");
        }

        public async Task<Result<string>> ExportBackupAsync(CancellationToken cancellationToken = default)
        {
            var temporal = Path.Combine(FileSystem.CacheDirectory, "export_backup.db");
            try
            {
                if (!File.Exists(_dbPath))
                    return Result<string>.Fail("No se encontró la base de datos local para respaldar.");

                await Task.Run(() => RespaldoSqlite.CrearCopia(_dbPath, temporal), cancellationToken);

                bool isSuccessful = false;
                Exception? saveException = null;

                using (var stream = new FileStream(temporal, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try
                    {
                        var fileSaverResult = await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            return await FileSaver.Default.SaveAsync(NombreRespaldo(), stream, cancellationToken);
                        });
                        isSuccessful = fileSaverResult.IsSuccessful;
                    }
                    catch (Exception ex)
                    {
                        saveException = ex;
                    }
                }

                if (saveException != null)
                    return Result<string>.Fail($"Error interno al guardar: {saveException.Message}");

                if (!isSuccessful)
                    return Result<string>.Fail("La operación fue cancelada por el usuario o falló.");

                return Result<string>.Ok("Backup exportado correctamente.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al exportar base de datos: {ex.Message}");
                return Result<string>.Fail(ex.Message);
            }
            finally
            {
                BorrarTemporal(temporal);
            }
        }

        /// <summary>
        /// Restaura un respaldo elegido por el usuario. El archivo elegido solo se lee (se puede
        /// volver a restaurar después) y la base actual se guarda antes como copia previa, que
        /// también se puede restaurar para deshacer. La app debe reiniciarse después.
        /// </summary>
        public async Task<Result<string>> RestoreBackupAsync()
        {
            var temporal = Path.Combine(FileSystem.CacheDirectory, "restore_backup.db");
            try
            {
                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".db", ".sqlite", ".sqlite3" } },
                    { DevicePlatform.Android, new[] { "application/octet-stream", "application/x-sqlite3" } }
                });

                var pickResult = await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    return await FilePicker.Default.PickAsync(new PickOptions
                    {
                        PickerTitle = "Selecciona el respaldo a restaurar (.db)",
                        FileTypes = customFileType
                    });
                });

                if (pickResult == null)
                    return Result<string>.Fail("Cancelado por el usuario");

                string ext = Path.GetExtension(pickResult.FileName).ToLower();
                if (ext != ".db" && ext != ".sqlite" && ext != ".sqlite3")
                    return Result<string>.Fail($"El archivo '{pickResult.FileName}' no es una base de datos válida.");

                // Copia local del archivo elegido: en Android puede no ser una ruta del disco
                using (var sourceStream = await pickResult.OpenReadAsync())
                using (var destStream = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destStream);
                }

                // Cerrar las conexiones que EF Core dejó en el pool antes de reemplazar el contenido
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                var copiaPrevia = Path.Combine(CarpetaParaCopiaPrevia(), $"Backup_Stock_AntesDeRestaurar_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                var result = await Task.Run(() => RespaldoSqlite.Restaurar(temporal, _dbPath, copiaPrevia));
                if (!result.Success)
                    return result;

                return Result<string>.Ok($"Respaldo '{pickResult.FileName}' restaurado. Tus datos anteriores quedaron guardados en '{copiaPrevia}'.");
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Error al restaurar base de datos: {ex.Message}");
            }
            finally
            {
                BorrarTemporal(temporal);
            }
        }

        public async Task<Result<string>> ExecuteBackupToFolderAsync(string targetFolder, bool isAutomatic)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                    return Result<string>.Fail("La carpeta de destino no es válida.");

                if (!File.Exists(_dbPath))
                    return Result<string>.Fail("No se encontró la base de datos.");

                if (RespaldoSqlite.EstaVacia(_dbPath))
                    return Result<string>.Fail(MensajeBaseVacia);

                var destinationPath = Path.Combine(targetFolder, NombreRespaldo());
                await Task.Run(() => RespaldoSqlite.CrearCopia(_dbPath, destinationPath));

                // Update preferences
                Preferences.Set(TargetFolderKey, targetFolder);
                Preferences.Set(LastRunUtcKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                // Cleanup old backups
                try { RespaldoSqlite.LimpiarRespaldosViejos(targetFolder, RetentionCount); }
                catch { /* Ignore cleanup errors */ }

                var prefix = isAutomatic ? "automático" : "manual";
                return Result<string>.Ok($"Backup {prefix} creado en {destinationPath}");
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Error general al respaldar: {ex.Message}");
            }
        }

        public async Task<Result<string>> ExecuteClosingBackupAsync(string targetFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                    return Result<string>.Fail("No hay carpeta configurada para el backup de cierre.");

                if (!File.Exists(_dbPath))
                    return Result<string>.Fail("No se encontró la base de datos.");

                if (RespaldoSqlite.EstaVacia(_dbPath))
                    return Result<string>.Fail(MensajeBaseVacia);

                var destinationPath = Path.Combine(targetFolder, "Backup_Stock_UltimoCierre.db");
                await Task.Run(() => RespaldoSqlite.CrearCopia(_dbPath, destinationPath));

                Preferences.Set(TargetFolderKey, targetFolder);
                Preferences.Set(LastCloseUtcKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                return Result<string>.Ok("Backup de cierre creado correctamente.");
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Error en el backup de cierre: {ex.Message}");
            }
        }

        public async Task<Result<string>> CheckAndRunAutoBackupAsync()
        {
            try
            {
                var folder = GetConfiguredFolder();
                if (!Directory.Exists(folder))
                    return Result<string>.Fail("No hay carpeta configurada para respaldos automáticos.");

                var lastRunString = Preferences.Get(LastRunUtcKey, string.Empty);
                DateTime lastRunUtc = DateTime.MinValue;

                if (!string.IsNullOrWhiteSpace(lastRunString))
                    DateTime.TryParse(lastRunString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out lastRunUtc);

                var elapsed = DateTime.UtcNow - lastRunUtc;
                if (elapsed < TimeSpan.FromHours(24))
                    return Result<string>.Ok("Aún no pasaron 24 horas desde el último respaldo automático.");

                return await ExecuteBackupToFolderAsync(folder, isAutomatic: true);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Error al verificar respaldo automático: {ex.Message}");
            }
        }

        /// <summary>Carpeta elegida por el usuario o, si no eligió ninguna, la predeterminada (se crea si falta).</summary>
        public string GetConfiguredFolder()
        {
            var carpeta = Preferences.Get(TargetFolderKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(carpeta))
                return carpeta;

            try { Directory.CreateDirectory(CarpetaPredeterminada); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Backup] No se pudo crear la carpeta predeterminada: {ex.Message}"); }
            return CarpetaPredeterminada;
        }

        private static string NombreRespaldo() => $"Backup_Stock_{DateTime.Now:yyyyMMdd_HHmm}.db";

        // Si la carpeta elegida no está disponible (ej. un pendrive desconectado), la copia
        // previa a una restauración va a la predeterminada: nunca se restaura sin ella.
        private string CarpetaParaCopiaPrevia()
        {
            var carpeta = GetConfiguredFolder();
            if (!Directory.Exists(carpeta))
                carpeta = CarpetaPredeterminada;
            Directory.CreateDirectory(carpeta);
            return carpeta;
        }

        private static void BorrarTemporal(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Backup] No se pudo borrar {path}: {ex.Message}"); }
        }

        public DateTime? GetLastBackupUtc()
        {
            var lastRunString = Preferences.Get(LastRunUtcKey, string.Empty);
            if (string.IsNullOrWhiteSpace(lastRunString)) return null;
            if (DateTime.TryParse(lastRunString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return null;
        }

        public DateTime? GetLastClosingBackupUtc()
        {
            var lastCloseString = Preferences.Get(LastCloseUtcKey, string.Empty);
            if (string.IsNullOrWhiteSpace(lastCloseString)) return null;
            if (DateTime.TryParse(lastCloseString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return null;
        }
    }
}
