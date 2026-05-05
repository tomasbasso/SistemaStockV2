using CommunityToolkit.Maui.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sistema_de_Stock.Data;
using Sistema_de_Stock.Models;
using Microsoft.Maui.Storage;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Linq;

namespace Sistema_de_Stock.Services
{
    public class BackupService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TenantService _tenantService;
        
        private const string TargetFolderKey = "Backup.TargetFolder";
        private const string LastRunUtcKey = "Backup.LastRunUtc";
        private const string LastCloseUtcKey = "Backup.LastCloseUtc";
        private const int RetentionCount = 15;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public BackupService(IServiceScopeFactory scopeFactory, TenantService tenantService)
        {
            _scopeFactory = scopeFactory;
            _tenantService = tenantService;
        }

        public async Task<Result<string>> ExportBackupAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var tenantId = _tenantService.CurrentTenantId;
                if (tenantId == Guid.Empty)
                    return Result<string>.Fail("No hay un negocio (Tenant) activo.");

                var dto = await BuildExportDtoAsync(tenantId, cancellationToken);
                var json = JsonSerializer.Serialize(dto, JsonOptions);

                // Create a temp file to hold the JSON so we can use FileSaver
                var tempBackupPath = Path.Combine(FileSystem.CacheDirectory, $"Backup_Temp_{Guid.NewGuid()}.json");
                await File.WriteAllTextAsync(tempBackupPath, json, cancellationToken);

                var fileName = $"Backup_Stock_{DateTime.Now:yyyyMMdd_HHmm}.json";
                bool isSuccessful = false;
                Exception? saveException = null;

                using (var stream = new FileStream(tempBackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
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

                if (File.Exists(tempBackupPath))
                {
                    try { File.Delete(tempBackupPath); } catch { /* Ignore */ }
                }

                if (saveException != null)
                    return Result<string>.Fail($"Error interno al guardar: {saveException.Message}");
                    
                if (!isSuccessful)
                    return Result<string>.Fail("La operación fue cancelada por el usuario o falló.");

                return Result<string>.Ok("Backup exportado correctamente en formato JSON.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al exportar JSON: {ex.Message}");
                return Result<string>.Fail(ex.Message);
            }
        }

        public async Task<Result<string>> RestoreBackupAsync()
        {
            try
            {
                var tenantId = _tenantService.CurrentTenantId;
                if (tenantId == Guid.Empty)
                    return Result<string>.Fail("No hay un negocio (Tenant) activo.");

                var customFileType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json" } },
                    { DevicePlatform.Android, new[] { "application/json" } }
                });

                var pickResult = await MainThread.InvokeOnMainThreadAsync(async () => 
                {
                    return await FilePicker.Default.PickAsync(new PickOptions
                    {
                        PickerTitle = "Selecciona el respaldo a restaurar (JSON)",
                        FileTypes = customFileType
                    });
                });

                if (pickResult == null)
                    return Result<string>.Fail("Cancelado por el usuario");

                string ext = Path.GetExtension(pickResult.FileName).ToLower();
                if (ext != ".json")
                    return Result<string>.Fail($"El archivo '{pickResult.FileName}' no es un JSON válido.");

                string jsonContent;
                using (var stream = await pickResult.OpenReadAsync())
                using (var reader = new StreamReader(stream))
                {
                    jsonContent = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(jsonContent))
                    return Result<string>.Fail("El archivo seleccionado está vacío.");

                var dto = JsonSerializer.Deserialize<BackupExportDto>(jsonContent, JsonOptions);
                if (dto == null)
                    return Result<string>.Fail("No se pudo leer el formato del archivo.");

                if (dto.TenantId != tenantId)
                {
                    return Result<string>.Fail("Este respaldo pertenece a otro negocio (diferente TenantId). No puedes restaurarlo aquí.");
                }

                // Restore to database
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<StockOnlineContext>();

                // Execution strategy for resilience (Supabase)
                var strategy = context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    using var transaction = await context.Database.BeginTransactionAsync();
                    try
                    {
                        // 1. Delete existing data for current tenant
                        // Because we rely on EF Core QueryFilters, simply doing context.Set<T>().ExecuteDeleteAsync() 
                        // will ONLY delete records for the current TenantId!
                        
                        await context.HistorialPrecios.ExecuteDeleteAsync();
                        await context.PresupuestoDetalles.ExecuteDeleteAsync();
                        await context.Presupuestos.ExecuteDeleteAsync();
                        await context.VentaDetalles.ExecuteDeleteAsync();
                        await context.Ventas.ExecuteDeleteAsync();
                        await context.MovimientosFinancieros.ExecuteDeleteAsync();
                        await context.CuentasCorrientes.ExecuteDeleteAsync();
                        await context.Clientes.ExecuteDeleteAsync();
                        await context.Productos.ExecuteDeleteAsync();
                        await context.Categorias.ExecuteDeleteAsync();
                        await context.Configuraciones.ExecuteDeleteAsync();

                        // 2. Map and generate NEW Guids for everything to avoid PK collisions across tenants
                        var catMap = new Dictionary<Guid, Guid>();
                        var seenCatNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in dto.Categorias) { 
                            var old = c.Id; c.Id = Guid.NewGuid(); catMap[old] = c.Id; 
                            
                            if (string.IsNullOrWhiteSpace(c.Name)) c.Name = "Sin Nombre";
                            int cCount = 1;
                            string cOrig = c.Name;
                            while (seenCatNames.Contains(c.Name)) { c.Name = $"{cOrig} {cCount++}"; }
                            seenCatNames.Add(c.Name);
                        }

                        var prodMap = new Dictionary<Guid, Guid>();
                        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in dto.Productos) { 
                            var old = p.Id; p.Id = Guid.NewGuid(); prodMap[old] = p.Id; 
                            if (catMap.TryGetValue(p.CategoryId, out var nId)) p.CategoryId = nId; 
                            
                            if (string.IsNullOrWhiteSpace(p.SKU)) p.SKU = $"SKU-{p.Id.ToString().Substring(0, 8)}";
                            int sCount = 1;
                            string sOrig = p.SKU;
                            while (seenSkus.Contains(p.SKU)) { p.SKU = $"{sOrig}-{sCount++}"; }
                            seenSkus.Add(p.SKU);
                        }

                        var cliMap = new Dictionary<Guid, Guid>();
                        foreach (var c in dto.Clientes) { var old = c.Id; c.Id = Guid.NewGuid(); cliMap[old] = c.Id; }

                        var seenCC = new HashSet<Guid>();
                        var cleanCC = new List<CuentaCorriente>();
                        foreach (var cc in dto.CuentasCorrientes) { 
                            cc.Id = Guid.NewGuid(); 
                            if (cliMap.TryGetValue(cc.ClienteId, out var nId)) cc.ClienteId = nId; 
                            if (!seenCC.Contains(cc.ClienteId)) { seenCC.Add(cc.ClienteId); cleanCC.Add(cc); }
                        }
                        dto.CuentasCorrientes = cleanCC;

                        var ventaMap = new Dictionary<Guid, Guid>();
                        var seenVentas = new HashSet<int>();
                        int maxVenta = dto.Ventas.Any() ? dto.Ventas.Max(x => x.NumeroVenta) : 0;
                        foreach (var v in dto.Ventas) { 
                            var old = v.Id; v.Id = Guid.NewGuid(); ventaMap[old] = v.Id; 
                            if (v.ClienteId.HasValue && cliMap.TryGetValue(v.ClienteId.Value, out var nId)) v.ClienteId = nId; 
                            
                            if (seenVentas.Contains(v.NumeroVenta)) { v.NumeroVenta = ++maxVenta; }
                            seenVentas.Add(v.NumeroVenta);
                        }

                        foreach (var vd in dto.VentaDetalles) { 
                            vd.Id = Guid.NewGuid(); 
                            if (ventaMap.TryGetValue(vd.VentaId, out var nVId)) vd.VentaId = nVId; 
                            if (prodMap.TryGetValue(vd.ProductoId, out var nPId)) vd.ProductoId = nPId; 
                        }

                        var presMap = new Dictionary<Guid, Guid>();
                        var seenPres = new HashSet<int>();
                        int maxPres = dto.Presupuestos.Any() ? dto.Presupuestos.Max(x => x.NumeroPresupuesto) : 0;
                        foreach (var p in dto.Presupuestos) { 
                            var old = p.Id; p.Id = Guid.NewGuid(); presMap[old] = p.Id; 
                            if (p.ClienteId.HasValue && cliMap.TryGetValue(p.ClienteId.Value, out var nId)) p.ClienteId = nId; 

                            if (seenPres.Contains(p.NumeroPresupuesto)) { p.NumeroPresupuesto = ++maxPres; }
                            seenPres.Add(p.NumeroPresupuesto);
                        }

                        foreach (var pd in dto.PresupuestoDetalles) { 
                            pd.Id = Guid.NewGuid(); 
                            if (presMap.TryGetValue(pd.PresupuestoId, out var nPrId)) pd.PresupuestoId = nPrId; 
                            if (prodMap.TryGetValue(pd.ProductoId, out var nPId)) pd.ProductoId = nPId; 
                        }

                        foreach (var hp in dto.HistorialPrecios) { 
                            hp.Id = Guid.NewGuid(); 
                            if (prodMap.TryGetValue(hp.ProductoId, out var nPId)) hp.ProductoId = nPId; 
                        }

                        foreach (var m in dto.MovimientosFinancieros) { 
                            m.Id = Guid.NewGuid(); 
                            if (m.VentaId.HasValue && ventaMap.TryGetValue(m.VentaId.Value, out var nVId)) m.VentaId = nVId; 
                        }

                        foreach (var conf in dto.Configuraciones) { conf.Id = Guid.NewGuid(); }

                        // 3. Insert new data with fresh IDs
                        // EF Core will automatically re-assign TenantId in SaveChanges if we missed it, 
                        // but it's already in the DTO objects anyway.
                        
                        if (dto.Configuraciones.Any()) await context.Configuraciones.AddRangeAsync(dto.Configuraciones);
                        if (dto.Categorias.Any()) await context.Categorias.AddRangeAsync(dto.Categorias);
                        if (dto.Productos.Any()) await context.Productos.AddRangeAsync(dto.Productos);
                        if (dto.Clientes.Any()) await context.Clientes.AddRangeAsync(dto.Clientes);
                        if (dto.CuentasCorrientes.Any()) await context.CuentasCorrientes.AddRangeAsync(dto.CuentasCorrientes);
                        if (dto.MovimientosFinancieros.Any()) await context.MovimientosFinancieros.AddRangeAsync(dto.MovimientosFinancieros);
                        if (dto.Ventas.Any()) await context.Ventas.AddRangeAsync(dto.Ventas);
                        if (dto.VentaDetalles.Any()) await context.VentaDetalles.AddRangeAsync(dto.VentaDetalles);
                        if (dto.Presupuestos.Any()) await context.Presupuestos.AddRangeAsync(dto.Presupuestos);
                        if (dto.PresupuestoDetalles.Any()) await context.PresupuestoDetalles.AddRangeAsync(dto.PresupuestoDetalles);
                        if (dto.HistorialPrecios.Any()) await context.HistorialPrecios.AddRangeAsync(dto.HistorialPrecios);

                        // Disable automatic state tracking assignments that might interfere
                        context.ChangeTracker.AutoDetectChangesEnabled = false;
                        await context.SaveChangesAsync();
                        context.ChangeTracker.AutoDetectChangesEnabled = true;

                        await transaction.CommitAsync();
                    }
                    catch (Exception innerEx)
                    {
                        await transaction.RollbackAsync();
                        var realError = innerEx.InnerException != null ? innerEx.InnerException.Message : innerEx.Message;
                        throw new Exception($"Error transaccional al restaurar la base de datos: {realError}");
                    }
                });

                return Result<string>.Ok($"Respaldo JSON '{pickResult.FileName}' restaurado correctamente. La nube de Supabase ha sido actualizada.");
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"Error al restaurar JSON: {ex.Message}");
            }
        }

        public async Task<Result<string>> ExecuteBackupToFolderAsync(string targetFolder, bool isAutomatic)
        {
            try
            {
                var tenantId = _tenantService.CurrentTenantId;
                if (tenantId == Guid.Empty)
                    return Result<string>.Fail("No hay un negocio (Tenant) activo.");

                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                    return Result<string>.Fail("La carpeta de destino no es válida.");

                var dto = await BuildExportDtoAsync(tenantId);
                var json = JsonSerializer.Serialize(dto, JsonOptions);

                var fileName = $"Backup_Stock_{DateTime.Now:yyyyMMdd_HHmm}.json";
                var destinationPath = Path.Combine(targetFolder, fileName);

                await File.WriteAllTextAsync(destinationPath, json);

                // Update preferences
                Preferences.Set(TargetFolderKey, targetFolder);
                Preferences.Set(LastRunUtcKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                // Cleanup old backups
                try
                {
                    var files = Directory.EnumerateFiles(targetFolder, "Backup_Stock_*.json")
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
                return Result<string>.Ok($"Backup JSON {prefix} creado en {destinationPath}");
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
                var tenantId = _tenantService.CurrentTenantId;
                if (tenantId == Guid.Empty)
                    return Result<string>.Fail("No hay un negocio (Tenant) activo para respaldo.");

                if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
                    return Result<string>.Fail("No hay carpeta configurada para el backup de cierre.");

                var dto = await BuildExportDtoAsync(tenantId);
                var json = JsonSerializer.Serialize(dto, JsonOptions);

                var destinationPath = Path.Combine(targetFolder, "Backup_Stock_UltimoCierre.json");
                await File.WriteAllTextAsync(destinationPath, json);

                Preferences.Set(TargetFolderKey, targetFolder);
                Preferences.Set(LastCloseUtcKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                return Result<string>.Ok("Backup JSON de cierre creado correctamente.");
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

        private async Task<BackupExportDto> BuildExportDtoAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<StockOnlineContext>();
            
            // AsNoTracking() is important for read-only queries to improve performance
            // The QueryFilters in StockOnlineContext automatically filter by TenantId!
            var dto = new BackupExportDto
            {
                TenantId = tenantId,
                Configuraciones = await context.Configuraciones.AsNoTracking().ToListAsync(cancellationToken),
                Categorias = await context.Categorias.AsNoTracking().ToListAsync(cancellationToken),
                Productos = await context.Productos.AsNoTracking().ToListAsync(cancellationToken),
                Clientes = await context.Clientes.AsNoTracking().ToListAsync(cancellationToken),
                CuentasCorrientes = await context.CuentasCorrientes.AsNoTracking().ToListAsync(cancellationToken),
                MovimientosFinancieros = await context.MovimientosFinancieros.AsNoTracking().ToListAsync(cancellationToken),
                Ventas = await context.Ventas.AsNoTracking().ToListAsync(cancellationToken),
                VentaDetalles = await context.VentaDetalles.AsNoTracking().ToListAsync(cancellationToken),
                Presupuestos = await context.Presupuestos.AsNoTracking().ToListAsync(cancellationToken),
                PresupuestoDetalles = await context.PresupuestoDetalles.AsNoTracking().ToListAsync(cancellationToken),
                HistorialPrecios = await context.HistorialPrecios.AsNoTracking().ToListAsync(cancellationToken)
            };

            return dto;
        }

        public string? GetConfiguredFolder() => Preferences.Get(TargetFolderKey, string.Empty);

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
