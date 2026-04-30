using Microsoft.EntityFrameworkCore;
using Sistema_de_Stock.Models;
using Sistema_de_Stock.Services;

namespace Sistema_de_Stock.Data
{
    /// <summary>
    /// Contexto principal de EF Core conectado a Supabase PostgreSQL.
    /// Todas las queries se filtran automáticamente por TenantId del negocio activo.
    /// Se usa cuando el dispositivo tiene conexión a internet.
    /// </summary>
    public class StockOnlineContext : DbContext, IStockContext
    {
        private readonly TenantService _tenantService;

        public StockOnlineContext(DbContextOptions<StockOnlineContext> options, TenantService tenantService)
            : base(options)
        {
            _tenantService = tenantService;
        }

        public Guid TenantId => _tenantService.CurrentTenantId;

        // ── DbSets ────────────────────────────────────────────────────────────
        public DbSet<ConfiguracionApp>      Configuraciones      { get; set; }
        public DbSet<Categoria>             Categorias           { get; set; }
        public DbSet<Producto>              Productos            { get; set; }
        public DbSet<Cliente>               Clientes             { get; set; }
        public DbSet<CuentaCorriente>       CuentasCorrientes    { get; set; }
        public DbSet<MovimientoFinanciero>  MovimientosFinancieros { get; set; }
        public DbSet<Venta>                 Ventas               { get; set; }
        public DbSet<VentaDetalle>          VentaDetalles        { get; set; }
        public DbSet<Presupuesto>           Presupuestos         { get; set; }
        public DbSet<PresupuestoDetalle>    PresupuestoDetalles  { get; set; }
        public DbSet<HistorialPrecio>       HistorialPrecios     { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── ConfiguracionApp ─────────────────────────────────────────────
            modelBuilder.Entity<ConfiguracionApp>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NombreNegocio).IsRequired().HasMaxLength(150);
                entity.Property(e => e.Moneda).IsRequired().HasMaxLength(10);
                entity.Property(e => e.DireccionNegocio).HasMaxLength(300);
                entity.Property(e => e.Telefono).HasMaxLength(50);
                entity.Property(e => e.UmbralRotacionBaja).HasColumnType("numeric(18,4)");
                entity.Property(e => e.UmbralRotacionMedia).HasColumnType("numeric(18,4)");
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // ── Categorias ───────────────────────────────────────────────────
            modelBuilder.Entity<Categoria>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                // Nombre único POR tenant
                entity.HasIndex(e => new { e.TenantId, e.Name }).IsUnique();
            });

            // ── Productos ────────────────────────────────────────────────────
            modelBuilder.Entity<Producto>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.SKU).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Price).HasColumnType("numeric(18,2)");
                entity.Property(e => e.PrecioCosto).HasColumnType("numeric(18,2)");
                entity.Property(e => e.Margen).HasColumnType("numeric(18,4)");
                entity.Property(e => e.CategoryId).IsRequired();
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                // SKU único POR tenant
                entity.HasIndex(e => new { e.TenantId, e.SKU }).IsUnique();
            });

            // ── Clientes ─────────────────────────────────────────────────────
            modelBuilder.Entity<Cliente>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Phone).HasMaxLength(50);
                entity.Property(e => e.Address).HasMaxLength(300);
                entity.Property(e => e.CUIT).HasMaxLength(13);
                entity.Property(e => e.Email).HasMaxLength(200);
                entity.Property(e => e.CondicionIva)
                    .HasConversion<string>()
                    .HasDefaultValue(CondicionIva.ConsumidorFinal)
                    .ValueGeneratedNever();
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // ── CuentasCorrientes ─────────────────────────────────────────────
            modelBuilder.Entity<CuentaCorriente>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Balance).HasColumnType("numeric(18,2)");
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                // Un CC por cliente POR tenant
                entity.HasIndex(e => new { e.TenantId, e.ClienteId }).IsUnique();
            });

            // ── MovimientosFinancieros ────────────────────────────────────────
            modelBuilder.Entity<MovimientoFinanciero>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Amount).HasColumnType("numeric(18,2)");
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.Type).HasConversion<string>();
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // ── Ventas ───────────────────────────────────────────────────────
            modelBuilder.Entity<Venta>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Total).HasColumnType("numeric(18,2)");
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                // NumeroVenta único POR tenant (cada negocio tiene su propia numeración)
                entity.HasIndex(e => new { e.TenantId, e.NumeroVenta }).IsUnique();
            });

            // ── VentaDetalles ─────────────────────────────────────────────────
            modelBuilder.Entity<VentaDetalle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UnitPrice).HasColumnType("numeric(18,2)");
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // ── Presupuestos ─────────────────────────────────────────────────
            modelBuilder.Entity<Presupuesto>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Total).HasColumnType("numeric(18,2)");
                entity.Property(e => e.Notas).HasMaxLength(500);
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                // NumeroPresupuesto único POR tenant
                entity.HasIndex(e => new { e.TenantId, e.NumeroPresupuesto }).IsUnique();
            });

            // ── PresupuestoDetalles ───────────────────────────────────────────
            modelBuilder.Entity<PresupuestoDetalle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UnitPrice).HasColumnType("numeric(18,2)");
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // ── HistorialPrecios ──────────────────────────────────────────────
            modelBuilder.Entity<HistorialPrecio>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProductoNombre).IsRequired().HasMaxLength(200);
                entity.Property(e => e.PrecioAnterior).HasColumnType("numeric(18,2)");
                entity.Property(e => e.PrecioNuevo).HasColumnType("numeric(18,2)");
                entity.Property(e => e.TenantId).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // ================================================================
            // QUERY FILTERS GLOBALES — filtran por TenantId + soft-delete
            // Se aplican automáticamente a TODAS las queries de EF Core
            // ================================================================

            // Con soft-delete
            modelBuilder.Entity<Producto>()
                .HasQueryFilter(p => p.TenantId == _tenantService.CurrentTenantId && !p.IsDeleted);
            modelBuilder.Entity<Cliente>()
                .HasQueryFilter(c => c.TenantId == _tenantService.CurrentTenantId && !c.IsDeleted);
            modelBuilder.Entity<Venta>()
                .HasQueryFilter(v => v.TenantId == _tenantService.CurrentTenantId && !v.IsDeleted);
            modelBuilder.Entity<Presupuesto>()
                .HasQueryFilter(p => p.TenantId == _tenantService.CurrentTenantId && !p.IsDeleted);

            // Solo por TenantId
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
        /// Asigna el TenantId y UpdatedAt actuales a las entidades antes de guardar.
        /// Garantiza que ningún registro se guarde sin tenant.
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                entry.Entity.UpdatedAt = now;
                if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Added &&
                    entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = _tenantService.CurrentTenantId;
                }
            }
            return base.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Asigna los datos huérfanos (sin TenantId) al tenant actual.
        /// Se llama una sola vez en el primer login en un dispositivo con datos SQLite previos.
        /// </summary>
        public async Task MigrateOrphanDataAsync()
        {
            var tenantId   = _tenantService.CurrentTenantId;
            var emptyId    = Guid.Empty;
            var now        = DateTime.UtcNow;

            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""Categorias""            SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""Productos""              SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""Clientes""               SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""CuentasCorrientes""      SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""MovimientosFinancieros"" SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""Ventas""                 SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""VentaDetalles""          SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""Presupuestos""           SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""PresupuestoDetalles""    SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""HistorialPrecios""       SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
            await Database.ExecuteSqlRawAsync(
                @"UPDATE ""Configuraciones""        SET ""TenantId"" = {0}, ""UpdatedAt"" = {1} WHERE ""TenantId"" = {2}", tenantId, now, emptyId);
        }
    }
}
