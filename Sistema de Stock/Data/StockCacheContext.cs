using Microsoft.EntityFrameworkCore;
using Sistema_de_Stock.Models;
using Sistema_de_Stock.Services;

namespace Sistema_de_Stock.Data
{
    /// <summary>
    /// Contexto SQLite local para modo offline (solo lectura).
    /// Se sincroniza desde Supabase cuando hay internet disponible.
    /// Mismos Query Filters por TenantId que StockOnlineContext.
    /// </summary>
    public class StockCacheContext : DbContext, IStockContext
    {
        private readonly TenantService _tenantService;

        public StockCacheContext(DbContextOptions<StockCacheContext> options, TenantService tenantService)
            : base(options)
        {
            _tenantService = tenantService;
        }

        public Guid TenantId => _tenantService?.CurrentTenantId ?? Guid.Empty;

        // ── DbSets ────────────────────────────────────────────────────────────
        public DbSet<ConfiguracionApp>     Configuraciones       { get; set; }
        public DbSet<Categoria>            Categorias            { get; set; }
        public DbSet<Producto>             Productos             { get; set; }
        public DbSet<Cliente>              Clientes              { get; set; }
        public DbSet<CuentaCorriente>      CuentasCorrientes     { get; set; }
        public DbSet<MovimientoFinanciero> MovimientosFinancieros { get; set; }
        public DbSet<Venta>                Ventas                { get; set; }
        public DbSet<VentaDetalle>         VentaDetalles         { get; set; }
        public DbSet<Presupuesto>          Presupuestos          { get; set; }
        public DbSet<PresupuestoDetalle>   PresupuestoDetalles   { get; set; }
        public DbSet<HistorialPrecio>      HistorialPrecios      { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // SQLite sí necesita HasColumnType para decimales
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                        property.SetColumnType("TEXT");
                }
            }

            // CondicionIva como string (igual que antes)
            modelBuilder.Entity<Cliente>()
                .Property(e => e.CondicionIva)
                .HasConversion<string>()
                .HasDefaultValue(CondicionIva.ConsumidorFinal)
                .ValueGeneratedNever();

            modelBuilder.Entity<MovimientoFinanciero>()
                .Property(e => e.Type)
                .HasConversion<string>();

            // ── Query Filters — mismos que StockOnlineContext ─────────────────
            modelBuilder.Entity<Producto>()
                .HasQueryFilter(p => p.TenantId == _tenantService.CurrentTenantId && !p.IsDeleted);
            modelBuilder.Entity<Cliente>()
                .HasQueryFilter(c => c.TenantId == _tenantService.CurrentTenantId && !c.IsDeleted);
            modelBuilder.Entity<Venta>()
                .HasQueryFilter(v => v.TenantId == _tenantService.CurrentTenantId && !v.IsDeleted);
            modelBuilder.Entity<Presupuesto>()
                .HasQueryFilter(p => p.TenantId == _tenantService.CurrentTenantId && !p.IsDeleted);
            modelBuilder.Entity<Categoria>()
                .HasQueryFilter(c => c.TenantId == _tenantService.CurrentTenantId);
            modelBuilder.Entity<CuentaCorriente>()
                .HasQueryFilter(c => c.TenantId == _tenantService.CurrentTenantId);
            modelBuilder.Entity<MovimientoFinanciero>()
                .HasQueryFilter(m => m.TenantId == _tenantService.CurrentTenantId);
            modelBuilder.Entity<VentaDetalle>()
                .HasQueryFilter(v => v.TenantId == _tenantService.CurrentTenantId);
            modelBuilder.Entity<PresupuestoDetalle>()
                .HasQueryFilter(p => p.TenantId == _tenantService.CurrentTenantId);
            modelBuilder.Entity<HistorialPrecio>()
                .HasQueryFilter(h => h.TenantId == _tenantService.CurrentTenantId);
            modelBuilder.Entity<ConfiguracionApp>()
                .HasQueryFilter(c => c.TenantId == _tenantService.CurrentTenantId);
        }

        /// <summary>
        /// Crea la base de datos de cache si no existe.
        /// </summary>
        public async Task InitializeCacheAsync()
        {
            await Database.EnsureCreatedAsync();
        }

        /// <summary>
        /// Elimina todos los datos del tenant actual del cache local.
        /// Se usa al hacer logout.
        /// </summary>
        public async Task ClearTenantCacheAsync()
        {
            var tid = _tenantService.CurrentTenantId;
            if (tid == Guid.Empty) return;

            Configuraciones.RemoveRange(Configuraciones.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            Categorias.RemoveRange(Categorias.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            Productos.RemoveRange(Productos.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            Clientes.RemoveRange(Clientes.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            CuentasCorrientes.RemoveRange(CuentasCorrientes.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            MovimientosFinancieros.RemoveRange(MovimientosFinancieros.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            Ventas.RemoveRange(Ventas.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            VentaDetalles.RemoveRange(VentaDetalles.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            Presupuestos.RemoveRange(Presupuestos.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            PresupuestoDetalles.RemoveRange(PresupuestoDetalles.IgnoreQueryFilters().Where(e => e.TenantId == tid));
            HistorialPrecios.RemoveRange(HistorialPrecios.IgnoreQueryFilters().Where(e => e.TenantId == tid));

            await SaveChangesAsync();
        }
    }
}
