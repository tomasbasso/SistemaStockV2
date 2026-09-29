using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SistemaDeStockV3.Data;
using SistemaDeStockV3.Models;
using SistemaDeStockV3.Services;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// El procedimiento real de actualización en el cliente: guardar el último respaldo,
/// borrar el programa viejo, copiar el nuevo y restaurar. Se simula con archivos reales
/// y con el esquema exacto de la versión de marzo 2026 (sin datos del cliente).
/// </summary>
public class ActualizacionDeVersionTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly string _respaldoViejo;

    public ActualizacionDeVersionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ActualizacionDeVersionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "stock.db");
        _respaldoViejo = Path.Combine(_dir, "Backup_Stock_UltimoCierre.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static readonly Guid CategoriaId = Guid.NewGuid();
    private static readonly Guid MartilloId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    // EF Core guarda los Guid como TEXT en mayúsculas y compara el texto tal cual en SQL:
    // los datos de prueba tienen que estar igual que en las bases reales.
    private static string G(Guid id) => id.ToString().ToUpperInvariant();

    /// <summary>Respaldo hecho con la versión de marzo 2026: esquema viejo, índices UNIQUE totales, modo WAL.</summary>
    private void CrearRespaldoDeVersionDeMarzo()
    {
        using var conn = new SqliteConnection($"Data Source={_respaldoViejo};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            PRAGMA journal_mode=WAL;
            CREATE TABLE ""Categorias"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_Categorias"" PRIMARY KEY, ""Name"" TEXT NOT NULL);
            CREATE TABLE ""Clientes"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_Clientes"" PRIMARY KEY, ""Name"" TEXT NOT NULL, ""Phone"" TEXT NOT NULL, ""Address"" TEXT NOT NULL);
            CREATE TABLE ""Configuraciones"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_Configuraciones"" PRIMARY KEY, ""NombreNegocio"" TEXT NOT NULL, ""Moneda"" TEXT NOT NULL, ""DireccionNegocio"" TEXT NOT NULL, ""Telefono"" TEXT NOT NULL);
            CREATE TABLE ""CuentasCorrientes"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_CuentasCorrientes"" PRIMARY KEY, ""ClienteId"" TEXT NOT NULL, ""Balance"" TEXT NOT NULL);
            CREATE TABLE ""MovimientosFinancieros"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_MovimientosFinancieros"" PRIMARY KEY, ""Type"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL, ""Date"" TEXT NOT NULL, ""Description"" TEXT NOT NULL, ""VentaId"" TEXT NULL);
            CREATE TABLE ""Productos"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_Productos"" PRIMARY KEY, ""Name"" TEXT NOT NULL, ""SKU"" TEXT NOT NULL, ""CategoryId"" TEXT NOT NULL, ""Stock"" INTEGER NOT NULL, ""StockMinimo"" INTEGER NOT NULL, ""Price"" TEXT NOT NULL, UnidadMedida TEXT NOT NULL DEFAULT 'u.');
            CREATE TABLE ""VentaDetalles"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_VentaDetalles"" PRIMARY KEY, ""VentaId"" TEXT NOT NULL, ""ProductoId"" TEXT NOT NULL, ""Quantity"" INTEGER NOT NULL, ""UnitPrice"" TEXT NOT NULL);
            CREATE TABLE ""Ventas"" (""Id"" TEXT NOT NULL CONSTRAINT ""PK_Ventas"" PRIMARY KEY, ""NumeroVenta"" INTEGER NOT NULL, ""Date"" TEXT NOT NULL, ""Total"" TEXT NOT NULL, ""ClienteId"" TEXT NULL, ""IsFiado"" INTEGER NOT NULL);
            CREATE UNIQUE INDEX ""IX_Categorias_Name"" ON ""Categorias"" (""Name"");
            CREATE UNIQUE INDEX ""IX_CuentasCorrientes_ClienteId"" ON ""CuentasCorrientes"" (""ClienteId"");
            CREATE UNIQUE INDEX ""IX_Productos_SKU"" ON ""Productos"" (""SKU"");
            CREATE UNIQUE INDEX ""IX_Ventas_NumeroVenta"" ON ""Ventas"" (""NumeroVenta"");

            INSERT INTO Configuraciones VALUES ('{G(Guid.NewGuid())}', 'Ferretería del Cliente', 'ARS', '', '');
            INSERT INTO Categorias VALUES ('{G(CategoriaId)}', 'Herramientas');
            INSERT INTO Productos VALUES ('{G(MartilloId)}', 'Martillo', 'MART-01', '{G(CategoriaId)}', 10, 2, '1500.50', 'u.');
            INSERT INTO Productos VALUES ('{G(Guid.NewGuid())}', 'Pinza', 'PIN-01', '{G(CategoriaId)}', 4, 1, '900', 'u.');
            INSERT INTO Clientes VALUES ('{G(ClienteId)}', 'Juan Pérez', '', '');
            INSERT INTO CuentasCorrientes VALUES ('{G(Guid.NewGuid())}', '{G(ClienteId)}', '3001');
            INSERT INTO Ventas VALUES ('{G(Guid.NewGuid())}', 1, '2026-03-10 10:00:00', '3001', '{G(ClienteId)}', 1);";
        cmd.ExecuteNonQuery();
    }

    private StockDbContext Contexto() => new(new DbContextOptionsBuilder<StockDbContext>()
        .UseSqlite($"Data Source={_dbPath};Pooling=False").Options);

    /// <summary>Abrir el programa: lo mismo que hace App al arrancar.</summary>
    private async Task AbrirLaAppAsync()
    {
        using var ctx = Contexto();
        await ctx.InitializeDatabaseAsync();
    }

    private static VentaDetalle Linea(Producto p) => new() { ProductoId = p.Id, Quantity = 1, UnitPrice = p.Price };

    // ──────────────────────────────────────────────────────────────────────
    // Tests
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RespaldoDeVersionVieja_RestauradoEnInstalacionNueva_ConservaLosDatos_Y_LaAppOperaNormal()
    {
        CrearRespaldoDeVersionDeMarzo();

        await AbrirLaAppAsync(); // instalación nueva: base vacía
        var result = RespaldoSqlite.Restaurar(_respaldoViejo, _dbPath, Path.Combine(_dir, "previa.db"));
        Assert.True(result.Success, result.Message);
        await AbrirLaAppAsync(); // la app se cierra y se vuelve a abrir tras restaurar

        using var ctx = Contexto();
        var svc = new DataService(ctx);

        // Los datos del respaldo están todos
        var productos = await svc.GetProductosAsync();
        Assert.Equal(new[] { "Martillo", "Pinza" }, productos.Select(p => p.Name).ToArray());
        var martillo = productos.Single(p => p.Id == MartilloId);
        Assert.Equal(1500.50m, martillo.Price);
        Assert.Equal(10, martillo.Stock);
        Assert.Equal("Ferretería del Cliente", (await svc.GetConfiguracionAsync())!.NombreNegocio);
        Assert.Equal(3001m, (await svc.GetCuentaCorrienteAsync(ClienteId))!.Balance);
        Assert.Single(await svc.GetVentasAsync());

        // Y la app opera con el esquema actualizado: vender, anular, volver a vender
        var venta2 = new Venta { Total = martillo.Price };
        await svc.ProcesarVentaAsync(venta2, new List<VentaDetalle> { Linea(martillo) });
        await svc.AnularVentaAsync(venta2.Id);
        var venta3 = new Venta { Total = martillo.Price };
        await svc.ProcesarVentaAsync(venta3, new List<VentaDetalle> { Linea(martillo) });
        Assert.Equal(2, venta2.NumeroVenta);
        Assert.Equal(3, venta3.NumeroVenta);

        // Y las columnas nuevas funcionan (soft-delete + reutilizar SKU)
        await svc.DeleteProductoAsync(martillo.Id);
        await svc.SaveProductoAsync(new Producto { Name = "Martillo nuevo", SKU = "MART-01", Price = 2000, CategoryId = CategoriaId });
        Assert.Contains(await svc.GetProductosAsync(), p => p.Name == "Martillo nuevo");
    }

    [Fact]
    public async Task RespaldoDeVersionVieja_AgregaLogoYColorDeMarca_SinTocarElRestoDeLaConfiguracion()
    {
        CrearRespaldoDeVersionDeMarzo();
        await AbrirLaAppAsync();
        RespaldoSqlite.Restaurar(_respaldoViejo, _dbPath, Path.Combine(_dir, "previa.db"));
        await AbrirLaAppAsync();

        using (var ctx = Contexto())
        {
            var svc = new DataService(ctx);
            var config = (await svc.GetConfiguracionAsync())!;
            Assert.Null(config.LogoNegocio);
            Assert.Equal(ConfiguracionApp.ColorMarcaPredeterminado, config.ColorMarca);

            await svc.SaveIdentidadDocumentosAsync(new byte[] { 1, 2, 3 }, "#e8681a");
        }

        using (var ctx = Contexto())
        {
            var svc = new DataService(ctx);
            var config = (await svc.GetConfiguracionAsync())!;
            Assert.Equal(new byte[] { 1, 2, 3 }, config.LogoNegocio);
            Assert.Equal("#E8681A", config.ColorMarca);
            Assert.Equal("Ferretería del Cliente", config.NombreNegocio);

            await svc.SaveIdentidadDocumentosAsync(null, "no-es-un-color");
        }

        using (var ctx = Contexto())
        {
            var config = (await new DataService(ctx).GetConfiguracionAsync())!;
            Assert.Null(config.LogoNegocio);
            Assert.Equal(ConfiguracionApp.ColorMarcaPredeterminado, config.ColorMarca);
        }
    }

    [Fact]
    public async Task InstalacionNueva_SinRestaurar_SeConsideraVacia()
    {
        // Así el respaldo de cierre no pisa el último respaldo bueno si se abre
        // y se cierra el programa nuevo antes de restaurar.
        await AbrirLaAppAsync();

        Assert.True(RespaldoSqlite.EstaVacia(_dbPath));
    }

    [Fact]
    public async Task InstalacionNueva_DespuesDeRestaurar_YaNoEstaVacia()
    {
        CrearRespaldoDeVersionDeMarzo();
        await AbrirLaAppAsync();

        RespaldoSqlite.Restaurar(_respaldoViejo, _dbPath, Path.Combine(_dir, "previa.db"));

        Assert.False(RespaldoSqlite.EstaVacia(_dbPath));
    }
}
