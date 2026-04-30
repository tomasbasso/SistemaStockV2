using Microsoft.EntityFrameworkCore;
using Sistema_de_Stock.Models;

namespace Sistema_de_Stock.Data;

/// <summary>
/// Interfaz compartida entre StockOnlineContext y StockCacheContext.
/// Permite que DataService acceda a los DbSets sin importar cuál contexto está activo.
/// </summary>
public interface IStockContext
{
    Guid TenantId { get; }
    DbSet<ConfiguracionApp>      Configuraciones       { get; }
    DbSet<Categoria>             Categorias            { get; }
    DbSet<Producto>              Productos             { get; }
    DbSet<Cliente>               Clientes              { get; }
    DbSet<CuentaCorriente>       CuentasCorrientes     { get; }
    DbSet<MovimientoFinanciero>  MovimientosFinancieros { get; }
    DbSet<Venta>                 Ventas                { get; }
    DbSet<VentaDetalle>          VentaDetalles         { get; }
    DbSet<Presupuesto>           Presupuestos          { get; }
    DbSet<PresupuestoDetalle>    PresupuestoDetalles   { get; }
    DbSet<HistorialPrecio>       HistorialPrecios      { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
}
