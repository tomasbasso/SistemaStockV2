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

        private readonly string _dbPath;

        public BackupService()
        {
            _dbPath = Path.Combine(FileSystem.AppDataDirectory, "stock.db");
        }

        public async Task<Result<string>> ExportBackupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!File.Exists(_dbPath))
                    return Result<string>.Fail("No se encontró la base de datos local para respaldar.");

                CheckpointWal();

                var fileName = $"Backup_Stock_{DateTime.Now:yyyyMMdd_HHmm}.db";
                bool isSuccessful = false;
                Exception? saveException = null;

                using (var stream = new FileStream(_dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    try 
                    {
                        var fileSaverResult = await MainThread.InvokeOnMainThreadAsync(async () => 
                        {
                            return await FileSaver.Default.SaveAsync(fileName, stream, cancellationToken);
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
        }

        public async Task<Result<string>> RestoreBackupAsync()
        {
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

                // Check file exists
                var pickedFilePath = pickResult.FullPath;

                // Close database connections in the connection pool so we can overwrite the file
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                // Force garbage collection in case there are undisposed contexts holding connections
                GC.Collect();
                GC.WaitForPendingFinalizers();

                using (var sourceStream = await pickResult.OpenReadAsync())
                using (var destStream = new FileStream(_dbPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destStream);
                }

                // Eliminar WAL/SHM de la sesión anterior: si quedan, SQLite los reaplica
                // sobre la base restaurada y la restauración "no trae datos".
                DeleteWalShmFiles();

                // Forzar cierre de la app para que el usuario la reinicie con la base restaurada
                // Esto evita que contextos EF Core en memoria pisen la base restaurada
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Application.Current?.Quit();
                });

                return Result<string>.Ok($"Respaldo '{pickResult.FileName}' restaurado. La aplicación se cerrará ahora para aplicar los cambios.");
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Error al restaurar base de datos: {ex.Message}");
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

                CheckpointWal();

                var fileName = $"Backup_Stock_{DateTime.Now:yyyyMMdd_HHmm}.db";
                var destinationPath = Path.Combine(targetFolder, fileName);

                using (var sourceStream = new FileStream(_dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destStream);
                }

                // Update preferences
                Preferences.Set(TargetFolderKey, targetFolder);
                Preferences.Set(LastRunUtcKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                // Cleanup old backups
                try
                {
                    var files = Directory.EnumerateFiles(targetFolder, "Backup_Stock_*.db")
                        .Select(path => new FileInfo(path))
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .ToList();

                    if (files.Count > RetentionCount)
                    {
                        foreach (var file in files.Skip(RetentionCount))
                        {
                            try { file.Delete(); } catch { /* ignore */ }
                        }
                    }
                }
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

                CheckpointWal();

                var destinationPath = Path.Combine(targetFolder, "Backup_Stock_UltimoCierre.db");
                
                using (var sourceStream = new FileStream(_dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await sourceStream.CopyToAsync(destStream);
                }

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
                var folder = Preferences.Get(TargetFolderKey, string.Empty);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
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

        public string? GetConfiguredFolder() => Preferences.Get(TargetFolderKey, string.Empty);

        private void CheckpointWal()
        {
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] CheckpointWal falló: {ex.Message}");
            }
        }

        private void DeleteWalShmFiles()
        {
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                try
                {
                    var path = _dbPath + suffix;
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Backup] No se pudo eliminar {_dbPath}{suffix}: {ex.Message}");
                }
            }
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
