using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SistemaDeStockV3.Models;
using System.Globalization;

namespace SistemaDeStockV3.Data
{
    /// <summary>
    /// Contexto principal de Entity Framework Core para la base de datos SQLite de la aplicación.
    /// Define el esquema y el seeding inicial de datos.
    /// </summary>
    public class StockDbContext : DbContext
    {
        public StockDbContext(DbContextOptions<StockDbContext> options) : base(options) { }

        public DbSet<ConfiguracionApp> Configuraciones { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<Producto> Productos { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<CuentaCorriente> CuentasCorrientes { get; set; }
        public DbSet<MovimientoFinanciero> MovimientosFinancieros { get; set; }
        public DbSet<Venta> Ventas { get; set; }
        public DbSet<VentaDetalle> VentaDetalles { get; set; }
        public DbSet<Presupuesto> Presupuestos { get; set; }
        public DbSet<PresupuestoDetalle> PresupuestoDetalles { get; set; }
        public DbSet<HistorialPrecio> HistorialPrecios { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Converter compartido: EF Core 8 + SQLite lanza NotSupportedException al intentar
            // aplicar Sum() sobre columnas decimal mapeadas como TEXT sin un converter explícito.
            var decimalConverter = new ValueConverter<decimal, string>(
                v => v.ToString(CultureInfo.InvariantCulture),
                v => string.IsNullOrEmpty(v) ? 0m : decimal.Parse(v, CultureInfo.InvariantCulture)
            );

            // --- Configuracion ---
            modelBuilder.Entity<ConfiguracionApp>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.NombreNegocio).IsRequired().HasMaxLength(150);
                entity.Property(e => e.Moneda).IsRequired().HasMaxLength(10);
                entity.Property(e => e.DireccionNegocio).HasMaxLength(300);
                entity.Property(e => e.Telefono).HasMaxLength(50);
                entity.Property(e => e.UmbralRotacionBaja).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.UmbralRotacionMedia).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.DiasAlertaSinVenta);
            });

            // --- Categorias ---
            modelBuilder.Entity<Categoria>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.Name).IsUnique();
            });

            // --- Productos ---
            modelBuilder.Entity<Producto>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.SKU).IsRequired().HasMaxLength(50);
                entity.HasIndex(e => e.SKU).IsUnique();
                entity.Property(e => e.Price).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.Margen).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.PrecioCosto).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.CategoryId).IsRequired();
            });

            // --- Clientes ---
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
                .HasColumnType("TEXT")
                .HasDefaultValue(CondicionIva.ConsumidorFinal)
                .ValueGeneratedNever();
            });

            // --- Cuentas Corrientes ---
            modelBuilder.Entity<CuentaCorriente>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Balance).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.HasIndex(e => e.ClienteId).IsUnique(); // One CC per client
            });

            // --- Movimientos Financieros ---
            modelBuilder.Entity<MovimientoFinanciero>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Amount).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.Type).HasConversion<string>(); // Store enum as readable string
            });

            // --- Ventas ---
            modelBuilder.Entity<Venta>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Total).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.HasIndex(e => e.NumeroVenta).IsUnique();
            });

            // --- Venta Detalles ---
            modelBuilder.Entity<VentaDetalle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UnitPrice).HasConversion(decimalConverter).HasColumnType("TEXT");
            });

            // --- Presupuestos ---
            modelBuilder.Entity<Presupuesto>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Total).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.HasIndex(e => e.NumeroPresupuesto).IsUnique();
                entity.Property(e => e.Notas).HasMaxLength(500);
            });

            // --- Presupuesto Detalles ---
            modelBuilder.Entity<PresupuestoDetalle>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UnitPrice).HasConversion(decimalConverter).HasColumnType("TEXT");
            });

            // --- Historial Precios ---
            modelBuilder.Entity<HistorialPrecio>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProductoNombre).IsRequired().HasMaxLength(200);
                entity.Property(e => e.PrecioAnterior).HasConversion(decimalConverter).HasColumnType("TEXT");
                entity.Property(e => e.PrecioNuevo).HasConversion(decimalConverter).HasColumnType("TEXT");
            });

            // Query filters for soft deletes
            modelBuilder.Entity<Producto>().HasQueryFilter(p => !p.IsDeleted);
            modelBuilder.Entity<Cliente>().HasQueryFilter(c => !c.IsDeleted);
            modelBuilder.Entity<Venta>().HasQueryFilter(v => !v.IsDeleted);
            modelBuilder.Entity<Presupuesto>().HasQueryFilter(p => !p.IsDeleted);
        }

        /// <summary>
        /// Crea la base de datos (si no existe) directamente desde el modelo EF Core y ejecuta el seeding inicial.
        /// Se usa EnsureCreatedAsync en lugar de MigrateAsync porque dotnet ef no puede ejecutarse
        /// en proyectos MAUI multi-target. Para una app de usuario único offline, esto es equivalente.
        /// </summary>
        public async Task InitializeDatabaseAsync()
        {
            await Database.EnsureCreatedAsync();

            // === APLICAR MIGRACIONES MANUALES ===
            var connection = Database.GetDbConnection();
            bool wasClosed = connection.State == System.Data.ConnectionState.Closed;
            if (wasClosed) await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(Productos);";
                bool hasUnidadMedida = false;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var colName = reader.GetString(1);
                        if (string.Equals(colName, "UnidadMedida", StringComparison.OrdinalIgnoreCase))
                        {
                            hasUnidadMedida = true;
                            break;
                        }
                    }
                }

                if (!hasUnidadMedida)
                {
                    command.CommandText = "ALTER TABLE Productos ADD COLUMN UnidadMedida TEXT NOT NULL DEFAULT 'u.';";
                    await command.ExecuteNonQueryAsync();
                }

                // Migración para columna Ubicacion
                command.CommandText = "PRAGMA table_info(Productos);";
                bool hasUbicacion = false;
                using (var reader2 = await command.ExecuteReaderAsync())
                {
                    while (await reader2.ReadAsync())
                    {
                        if (string.Equals(reader2.GetString(1), "Ubicacion", StringComparison.OrdinalIgnoreCase))
                        {
                            hasUbicacion = true;
                            break;
                        }
                    }
                }
                if (!hasUbicacion)
                {
                    command.CommandText = "ALTER TABLE Productos ADD COLUMN Ubicacion TEXT NOT NULL DEFAULT '';";
                    await command.ExecuteNonQueryAsync();
                }
                // ── CodigoBarras: Productos ───────────────────────────────────
                command.CommandText = "PRAGMA table_info(Productos);";
                bool hasCodigoBarras = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "CodigoBarras", StringComparison.OrdinalIgnoreCase))
                        { hasCodigoBarras = true; break; }
                if (!hasCodigoBarras)
                {
                    command.CommandText = "ALTER TABLE Productos ADD COLUMN CodigoBarras TEXT NULL;";
                    await command.ExecuteNonQueryAsync();
                }

                // Crear tablas de Presupuestos si no existen
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Presupuestos (
                        Id TEXT PRIMARY KEY,
                        NumeroPresupuesto INTEGER NOT NULL UNIQUE,
                        Date TEXT NOT NULL,
                        FechaVencimiento TEXT,
                        Total TEXT NOT NULL,
                        ClienteId TEXT,
                        Notas TEXT NOT NULL DEFAULT '',
                        IsDeleted INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS PresupuestoDetalles (
                        Id TEXT PRIMARY KEY,
                        PresupuestoId TEXT NOT NULL,
                        ProductoId TEXT NOT NULL,
                        Quantity INTEGER NOT NULL,
                        UnitPrice TEXT NOT NULL
                    );";
                await command.ExecuteNonQueryAsync();

                // ── IsDeleted: Productos ──────────────────────────────────────
                command.CommandText = "PRAGMA table_info(Productos);";
                bool hasIsDeletedProductos = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "IsDeleted", StringComparison.OrdinalIgnoreCase))
                        { hasIsDeletedProductos = true; break; }
                if (!hasIsDeletedProductos)
                {
                    command.CommandText = "ALTER TABLE Productos ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    await command.ExecuteNonQueryAsync();
                }

                // ── IsDeleted: Clientes ───────────────────────────────────────
                command.CommandText = "PRAGMA table_info(Clientes);";
                bool hasIsDeletedClientes = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "IsDeleted", StringComparison.OrdinalIgnoreCase))
                        { hasIsDeletedClientes = true; break; }
                if (!hasIsDeletedClientes)
                {
                    command.CommandText = "ALTER TABLE Clientes ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    await command.ExecuteNonQueryAsync();
                }

                // Configuracion umbrales rotación
                command.CommandText = "PRAGMA table_info(Configuraciones);";
                bool hasUmbralBaja = false, hasUmbralMedia = false, hasDiasSinVenta = false;
                using (var r = await command.ExecuteReaderAsync())
                {
                    while (await r.ReadAsync())
                    {
                        var col = r.GetString(1);
                        if (string.Equals(col, "UmbralRotacionBaja", StringComparison.OrdinalIgnoreCase)) hasUmbralBaja = true;
                        if (string.Equals(col, "UmbralRotacionMedia", StringComparison.OrdinalIgnoreCase)) hasUmbralMedia = true;
                        if (string.Equals(col, "DiasAlertaSinVenta", StringComparison.OrdinalIgnoreCase)) hasDiasSinVenta = true;
                    }
                }
                if (!hasUmbralBaja)
                {
                    command.CommandText = "ALTER TABLE Configuraciones ADD COLUMN UmbralRotacionBaja TEXT NOT NULL DEFAULT '1.0';";
                    await command.ExecuteNonQueryAsync();
                }
                if (!hasUmbralMedia)
                {
                    command.CommandText = "ALTER TABLE Configuraciones ADD COLUMN UmbralRotacionMedia TEXT NOT NULL DEFAULT '4.0';";
                    await command.ExecuteNonQueryAsync();
                }
                if (!hasDiasSinVenta)
                {
                    command.CommandText = "ALTER TABLE Configuraciones ADD COLUMN DiasAlertaSinVenta INTEGER NOT NULL DEFAULT 90;";
                    await command.ExecuteNonQueryAsync();
                }

                // ── IsDeleted: Ventas ─────────────────────────────────────────
                command.CommandText = "PRAGMA table_info(Ventas);";
                bool hasIsDeletedVentas = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "IsDeleted", StringComparison.OrdinalIgnoreCase))
                        { hasIsDeletedVentas = true; break; }
                if (!hasIsDeletedVentas)
                {
                    command.CommandText = "ALTER TABLE Ventas ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    await command.ExecuteNonQueryAsync();
                }

                // ── IsFiado: Ventas ───────────────────────────────────────────
                command.CommandText = "PRAGMA table_info(Ventas);";
                bool hasIsFiado = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "IsFiado", StringComparison.OrdinalIgnoreCase))
                        { hasIsFiado = true; break; }
                if (!hasIsFiado)
                {
                    command.CommandText = "ALTER TABLE Ventas ADD COLUMN IsFiado INTEGER NOT NULL DEFAULT 0;";
                    await command.ExecuteNonQueryAsync();
                }

                // ── ClienteId: Ventas ────────────────────────────────────────
                command.CommandText = "PRAGMA table_info(Ventas);";
                bool hasClienteIdVentas = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "ClienteId", StringComparison.OrdinalIgnoreCase))
                        { hasClienteIdVentas = true; break; }
                if (!hasClienteIdVentas)
                {
                    command.CommandText = "ALTER TABLE Ventas ADD COLUMN ClienteId TEXT NULL;";
                    await command.ExecuteNonQueryAsync();
                }

                // ── IsDeleted: Presupuestos ───────────────────────────────────
                command.CommandText = "PRAGMA table_info(Presupuestos);";
                bool hasIsDeletedPresupuestos = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "IsDeleted", StringComparison.OrdinalIgnoreCase))
                        { hasIsDeletedPresupuestos = true; break; }
                if (!hasIsDeletedPresupuestos)
                {
                    command.CommandText = "ALTER TABLE Presupuestos ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0;";
                    await command.ExecuteNonQueryAsync();
                }

                // ── PrecioCosto: Productos ───────────────────────────────────
                command.CommandText = "PRAGMA table_info(Productos);";
                bool hasPrecioCosto = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "PrecioCosto", StringComparison.OrdinalIgnoreCase))
                        { hasPrecioCosto = true; break; }
                if (!hasPrecioCosto)
                {
                    command.CommandText = "ALTER TABLE Productos ADD COLUMN PrecioCosto TEXT NOT NULL DEFAULT '0';";
                    await command.ExecuteNonQueryAsync();
                }
                // ── Margen: Productos ───────────────────────────────────
                command.CommandText = "PRAGMA table_info(Productos);";
                bool hasMargen = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                        if (string.Equals(r.GetString(1), "Margen", StringComparison.OrdinalIgnoreCase))
                        { hasMargen = true; break; }
                if (!hasMargen)
                {
                    command.CommandText = "ALTER TABLE Productos ADD COLUMN Margen TEXT NOT NULL DEFAULT '0';";
                    await command.ExecuteNonQueryAsync();
                }
                // ── CUIT y Email: Clientes ───────────────────────────────────
                command.CommandText = "PRAGMA table_info(Clientes);";
                bool hasCUITClientes = false, hasEmailClientes = false, hasCondicionIva = false;
                using (var r = await command.ExecuteReaderAsync())
                    while (await r.ReadAsync())
                    {
                        var col = r.GetString(1);
                        if (string.Equals(col, "CUIT", StringComparison.OrdinalIgnoreCase)) hasCUITClientes = true;
                        if (string.Equals(col, "Email", StringComparison.OrdinalIgnoreCase)) hasEmailClientes = true;
                        if (string.Equals(col, "CondicionIva", StringComparison.OrdinalIgnoreCase)) hasCondicionIva = true;
                    }
                if (!hasCUITClientes)
                {
                    command.CommandText = "ALTER TABLE Clientes ADD COLUMN CUIT TEXT NOT NULL DEFAULT '';";
                    await command.ExecuteNonQueryAsync();
                }
                if (!hasEmailClientes)
                {
                    command.CommandText = "ALTER TABLE Clientes ADD COLUMN Email TEXT NOT NULL DEFAULT '';";
                    await command.ExecuteNonQueryAsync();
                }
                if (!hasCondicionIva)
                {
                    command.CommandText = "ALTER TABLE Clientes ADD COLUMN CondicionIva TEXT NOT NULL DEFAULT 'ConsumidorFinal';";
                    await command.ExecuteNonQueryAsync();
                }

                // Crear tabla MovimientosFinancieros si no existe
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS MovimientosFinancieros (
                        Id TEXT PRIMARY KEY,
                        Type TEXT NOT NULL,
                        Amount TEXT NOT NULL,
                        Date TEXT NOT NULL,
                        Description TEXT NOT NULL DEFAULT '',
                        VentaId TEXT NULL
                    );";
                await command.ExecuteNonQueryAsync();

                // Crear tabla HistorialPrecios si no existe
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS HistorialPrecios (
                        Id TEXT PRIMARY KEY,
                        ProductoId TEXT NOT NULL,
                        ProductoNombre TEXT NOT NULL,
                        FechaModificacion TEXT NOT NULL,
                        PrecioAnterior TEXT NOT NULL,
                        PrecioNuevo TEXT NOT NULL
                    );";
                await command.ExecuteNonQueryAsync();
            }

            if (wasClosed) await connection.CloseAsync();
            // ====================================

            await SeedInitialDataAsync();
        }

        private async Task SeedInitialDataAsync()
        {
            // Only seed if everything is empty (first run)
            if (await Configuraciones.AnyAsync()) return;

            // --- Configuración inicial ---
            Configuraciones.Add(new ConfiguracionApp
            {
                NombreNegocio = "Comercial Kai Ken",
                Moneda = "ARS"
            });


            await SaveChangesAsync();
        }
    }
}
