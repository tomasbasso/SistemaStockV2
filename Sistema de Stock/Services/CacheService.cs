using Microsoft.EntityFrameworkCore;
using Sistema_de_Stock.Data;
using Sistema_de_Stock.Models;

namespace Sistema_de_Stock.Services;

/// <summary>
/// Descarga los datos del tenant desde Supabase y los persiste en SQLite local (cache offline).
/// Se llama después de cada login exitoso y periódicamente cuando hay internet.
/// </summary>
public class CacheService
{
    private readonly StockOnlineContext _online;
    private readonly StockCacheContext  _cache;
    private readonly TenantService _tenantService;

    public CacheService(StockOnlineContext online, StockCacheContext cache, TenantService tenantService)
    {
        _online = online;
        _cache  = cache;
        _tenantService = tenantService;
    }

    /// <summary>
    /// Sincroniza todos los datos del tenant actual desde Supabase al SQLite local.
    /// Operación de reemplazo completo: borra el cache anterior y lo reconstruye.
    /// </summary>
    public async Task RefreshAllAsync()
    {
        await _cache.InitializeCacheAsync();

        var tenantId = _tenantService.CurrentTenantId;
        if (tenantId == Guid.Empty) throw new InvalidOperationException("No hay sesión de negocio activa.");

        // Limpiar cache anterior del tenant
        await _cache.ClearTenantCacheAsync();

        // ── Descargar y guardar cada entidad ─────────────────────────────────
        var configs = await _online.Configuraciones.ToListAsync();
        _cache.Configuraciones.AddRange(configs);

        var categorias = await _online.Categorias.ToListAsync();
        _cache.Categorias.AddRange(categorias);

        var productos = await _online.Productos.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId).ToListAsync();
        _cache.Productos.AddRange(productos);

        var clientes = await _online.Clientes.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).ToListAsync();
        _cache.Clientes.AddRange(clientes);

        var cuentas = await _online.CuentasCorrientes.ToListAsync();
        _cache.CuentasCorrientes.AddRange(cuentas);

        var movimientos = await _online.MovimientosFinancieros.ToListAsync();
        _cache.MovimientosFinancieros.AddRange(movimientos);

        var ventas = await _online.Ventas.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId).ToListAsync();
        _cache.Ventas.AddRange(ventas);

        var ventaDetalles = await _online.VentaDetalles.ToListAsync();
        _cache.VentaDetalles.AddRange(ventaDetalles);

        var presupuestos = await _online.Presupuestos.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId).ToListAsync();
        _cache.Presupuestos.AddRange(presupuestos);

        var presupuestoDetalles = await _online.PresupuestoDetalles.ToListAsync();
        _cache.PresupuestoDetalles.AddRange(presupuestoDetalles);

        var historial = await _online.HistorialPrecios.ToListAsync();
        _cache.HistorialPrecios.AddRange(historial);

        await _cache.SaveChangesAsync();
    }
}
