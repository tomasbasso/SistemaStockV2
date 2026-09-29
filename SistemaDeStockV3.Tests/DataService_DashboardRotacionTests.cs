using SistemaDeStockV3.Models;
using SistemaDeStockV3.Data;
using Xunit;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// Tests del dashboard (GetDashboardDataAsync), análisis de rotación por producto
/// (GetRotacionProductosAsync), totales y paginación de movimientos financieros.
/// Nota: decimal se persiste como TEXT en SQLite — los valores esperados se calculan en C#.
/// </summary>
public class DataService_DashboardRotacionTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers de seeding
    // ──────────────────────────────────────────────────────────────────────

    private static Categoria CrearCategoria(StockDbContext db, string nombre = "General")
    {
        var cat = new Categoria { Name = nombre };
        db.Categorias.Add(cat);
        db.SaveChanges();
        return cat;
    }

    private static Producto CrearProducto(StockDbContext db, Guid categoriaId, string nombre, string sku,
        int stock, decimal precio = 0, int stockMinimo = 0, bool borrado = false, decimal precioCosto = 0)
    {
        var p = new Producto
        {
            Name = nombre,
            SKU = sku,
            Stock = stock,
            StockMinimo = stockMinimo,
            Price = precio,
            PrecioCosto = precioCosto,
            CategoryId = categoriaId,
            IsDeleted = borrado
        };
        db.Productos.Add(p);
        db.SaveChanges();
        return p;
    }

    private static Venta CrearVentaConDetalle(StockDbContext db, int numero, DateTime fecha, Guid productoId,
        int cantidad, decimal total = 0, bool borrada = false)
    {
        var venta = new Venta { NumeroVenta = numero, Date = fecha, Total = total, IsDeleted = borrada };
        db.Ventas.Add(venta);
        db.SaveChanges();

        db.VentaDetalles.Add(new VentaDetalle { VentaId = venta.Id, ProductoId = productoId, Quantity = cantidad });
        db.SaveChanges();
        return venta;
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetDashboardDataAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDashboardData_VentasDeHoy_NoIncluyeVentasDeAyer()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetDashboardData_VentasDeHoy_NoIncluyeVentasDeAyer));

        db.Ventas.Add(new Venta { NumeroVenta = 1, Date = DateTime.Today.AddHours(12), Total = 1500.50m });
        db.Ventas.Add(new Venta { NumeroVenta = 2, Date = DateTime.Today.AddDays(-1).AddHours(12), Total = 999m });
        // Venta de hoy borrada: el query filter global de Venta la excluye
        db.Ventas.Add(new Venta { NumeroVenta = 3, Date = DateTime.Today.AddHours(10), Total = 5000m, IsDeleted = true });
        db.SaveChanges();

        var data = await svc.GetDashboardDataAsync();

        Assert.Equal(1500.50m, data.TotalVentas);
        Assert.Equal(1, data.CantidadVentas);
    }

    [Fact]
    public async Task GetDashboardData_DeudaTotal_SumaSaldosDeCuentasCorrientes()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetDashboardData_DeudaTotal_SumaSaldosDeCuentasCorrientes));

        var c1 = new Cliente { Name = "Cliente A" };
        var c2 = new Cliente { Name = "Cliente B" };
        db.Clientes.AddRange(c1, c2);
        db.SaveChanges();

        db.CuentasCorrientes.Add(new CuentaCorriente { ClienteId = c1.Id, Balance = 1500.50m });
        db.CuentasCorrientes.Add(new CuentaCorriente { ClienteId = c2.Id, Balance = -200.25m });
        db.SaveChanges();

        var data = await svc.GetDashboardDataAsync();

        // Suma materializada en memoria (decimal como TEXT en SQLite)
        Assert.Equal(1500.50m + (-200.25m), data.TotalDeuda);
    }

    [Fact]
    public async Task GetDashboardData_ValorInventario_PrecioPorStock_ExcluyeBorradosYStockNegativo()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetDashboardData_ValorInventario_PrecioPorStock_ExcluyeBorradosYStockNegativo));
        var cat = CrearCategoria(db);

        CrearProducto(db, cat.Id, "Normal", "SKU-1", stock: 4, precio: 10.50m);
        CrearProducto(db, cat.Id, "StockNegativo", "SKU-2", stock: -5, precio: 20m);       // Math.Max(0, stock) => aporta 0
        CrearProducto(db, cat.Id, "Borrado", "SKU-3", stock: 10, precio: 100m, borrado: true); // query filter lo excluye

        var data = await svc.GetDashboardDataAsync();

        Assert.Equal(10.50m * 4, data.ValorInventario);
        Assert.Equal(2, data.TotalProductos); // el borrado no cuenta
    }

    [Fact]
    public async Task GetDashboardData_BajoStock_UmbralStockMenorOIgualAlMinimo_OrdenadoPorStock()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetDashboardData_BajoStock_UmbralStockMenorOIgualAlMinimo_OrdenadoPorStock));
        var cat = CrearCategoria(db);

        CrearProducto(db, cat.Id, "Critico", "SKU-1", stock: 1, stockMinimo: 5);
        CrearProducto(db, cat.Id, "EnElLimite", "SKU-2", stock: 5, stockMinimo: 5); // Stock <= StockMinimo incluye la igualdad
        CrearProducto(db, cat.Id, "Sano", "SKU-3", stock: 6, stockMinimo: 5);

        var data = await svc.GetDashboardDataAsync();

        Assert.Equal(2, data.BajoStock.Count);
        Assert.Equal("Critico", data.BajoStock[0].Name);    // ordenado por Stock ascendente
        Assert.Equal("EnElLimite", data.BajoStock[1].Name);
        Assert.DoesNotContain(data.BajoStock, p => p.Name == "Sano");
    }

    [Fact]
    public async Task GetDashboardData_UltimosMovimientos_SoloDeHoy_MasRecientePrimero()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetDashboardData_UltimosMovimientos_SoloDeHoy_MasRecientePrimero));

        db.MovimientosFinancieros.Add(new MovimientoFinanciero
        {
            Type = TipoMovimiento.Ingreso, Amount = 100m, Description = "Temprano", Date = DateTime.Today.AddHours(9)
        });
        db.MovimientosFinancieros.Add(new MovimientoFinanciero
        {
            Type = TipoMovimiento.Egreso, Amount = 200m, Description = "Tarde", Date = DateTime.Today.AddHours(15)
        });
        db.MovimientosFinancieros.Add(new MovimientoFinanciero
        {
            Type = TipoMovimiento.Ingreso, Amount = 300m, Description = "Ayer", Date = DateTime.Today.AddDays(-1).AddHours(12)
        });
        db.SaveChanges();

        var data = await svc.GetDashboardDataAsync();

        Assert.Equal(2, data.UltimosMovimientos.Count);
        Assert.Equal("Tarde", data.UltimosMovimientos[0].Description);
        Assert.Equal("Temprano", data.UltimosMovimientos[1].Description);
    }

    [Fact]
    public async Task GetDashboardData_BaseVacia_TodoEnCeroSinExplotar()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetDashboardData_BaseVacia_TodoEnCeroSinExplotar));

        var data = await svc.GetDashboardDataAsync();

        Assert.Equal(0m, data.TotalVentas);
        Assert.Equal(0, data.CantidadVentas);
        Assert.Equal(0m, data.TotalDeuda);
        Assert.Equal(0m, data.ValorInventario);
        Assert.Equal(0, data.TotalProductos);
        Assert.Empty(data.BajoStock);
        Assert.Empty(data.UltimosMovimientos);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetRotacionProductosAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRotacionProductos_VentasAltasVsSinVentas_EstadosYAccionesDistintos()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetRotacionProductos_VentasAltasVsSinVentas_EstadosYAccionesDistintos));
        var cat = CrearCategoria(db);

        var pAlta = CrearProducto(db, cat.Id, "MuchaVenta", "SKU-A", stock: 10, precio: 100m);
        var pSin = CrearProducto(db, cat.Id, "NuncaVendido", "SKU-B", stock: 10, precio: 50m);

        // 50 unidades vendidas / stock 10 => rotación 5 >= umbral media (4.0) => "Alta"
        CrearVentaConDetalle(db, 1, DateTime.Today.AddMonths(-1), pAlta.Id, 50);

        var result = await svc.GetRotacionProductosAsync();

        var alta = result.Single(r => r.Nombre == "MuchaVenta");
        Assert.Equal(5.00m, alta.Rotacion);
        Assert.Equal("Alta", alta.EstadoRotacion);
        Assert.Equal("Mantener", alta.AccionSugerida);
        Assert.Equal(50, alta.UnidadesVendidas12m);

        var sin = result.Single(r => r.Nombre == "NuncaVendido");
        Assert.Equal(0m, sin.Rotacion);
        Assert.Equal("Sin rotación", sin.EstadoRotacion);
        Assert.Equal("Descontinuar / limpiar stock", sin.AccionSugerida);
        Assert.Equal(9999, sin.DiasSinVenta); // sin última venta => sentinel 9999
        Assert.Null(sin.UltimaVenta);
        Assert.Equal("→", sin.Tendencia);

        // Orden: rotación ascendente => el "Sin rotación" aparece primero
        Assert.Equal("NuncaVendido", result[0].Nombre);
    }

    [Fact]
    public async Task GetRotacionProductos_EstadosBajaYMedia_SegunUmbralesPorDefecto()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetRotacionProductos_EstadosBajaYMedia_SegunUmbralesPorDefecto));
        var cat = CrearCategoria(db);

        var pBaja = CrearProducto(db, cat.Id, "RotaBaja", "SKU-B", stock: 10);
        var pMedia = CrearProducto(db, cat.Id, "RotaMedia", "SKU-M", stock: 10);

        // 5 / 10 = 0.5 < umbral baja por defecto (1.0) => "Baja"
        CrearVentaConDetalle(db, 1, DateTime.Today.AddMonths(-2), pBaja.Id, 5);
        // 20 / 10 = 2.0, entre 1.0 y 4.0 => "Media"
        CrearVentaConDetalle(db, 2, DateTime.Today.AddMonths(-2), pMedia.Id, 20);

        var result = await svc.GetRotacionProductosAsync();

        var baja = result.Single(r => r.Nombre == "RotaBaja");
        Assert.Equal(0.50m, baja.Rotacion);
        Assert.Equal("Baja", baja.EstadoRotacion);
        Assert.Equal("Promocionar o ajustar precio", baja.AccionSugerida);

        var media = result.Single(r => r.Nombre == "RotaMedia");
        Assert.Equal(2.00m, media.Rotacion);
        Assert.Equal("Media", media.EstadoRotacion);
        Assert.Equal("Monitorear", media.AccionSugerida);
    }

    [Fact]
    public async Task GetRotacionProductos_Tendencia_CrecienteYDecreciente()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetRotacionProductos_Tendencia_CrecienteYDecreciente));
        var cat = CrearCategoria(db);

        var pCrece = CrearProducto(db, cat.Id, "Creciente", "SKU-C", stock: 100);
        var pDecae = CrearProducto(db, cat.Id, "Decreciente", "SKU-D", stock: 100);

        // Creciente: últimos 3 meses (10) > 3 meses previos (5) => "↗"
        CrearVentaConDetalle(db, 1, DateTime.Today.AddMonths(-1), pCrece.Id, 10);
        CrearVentaConDetalle(db, 2, DateTime.Today.AddMonths(-4), pCrece.Id, 5);

        // Decreciente: últimos 3 meses (3) < 3 meses previos (8) => "↘"
        CrearVentaConDetalle(db, 3, DateTime.Today.AddMonths(-1), pDecae.Id, 3);
        CrearVentaConDetalle(db, 4, DateTime.Today.AddMonths(-4), pDecae.Id, 8);

        var result = await svc.GetRotacionProductosAsync();

        Assert.Equal("↗", result.Single(r => r.Nombre == "Creciente").Tendencia);
        Assert.Equal("↘", result.Single(r => r.Nombre == "Decreciente").Tendencia);
    }

    [Fact]
    public async Task GetRotacionProductos_ProductosBorrados_NoAparecen()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetRotacionProductos_ProductosBorrados_NoAparecen));
        var cat = CrearCategoria(db);

        CrearProducto(db, cat.Id, "Vivo", "SKU-V", stock: 10);
        CrearProducto(db, cat.Id, "Borrado", "SKU-X", stock: 10, borrado: true);

        var result = await svc.GetRotacionProductosAsync();

        Assert.Single(result);
        Assert.Equal("Vivo", result[0].Nombre);
    }

    [Fact]
    public async Task GetRotacionProductos_StockCeroConVentas_EstadoAgotadoReponer()
    {
        // Un producto que se vendió hasta agotar el stock no es "Sin rotación":
        // tuvo ventas en el período => estado "Agotado", acción "Reponer stock"
        var (svc, db) = TestDbHelper.Create(nameof(GetRotacionProductos_StockCeroConVentas_EstadoAgotadoReponer));
        var cat = CrearCategoria(db);

        var p = CrearProducto(db, cat.Id, "VendidoTodo", "SKU-AG", stock: 0, precio: 100m);
        CrearVentaConDetalle(db, 1, DateTime.Today.AddMonths(-1), p.Id, 30);

        var result = await svc.GetRotacionProductosAsync();

        var r = result.Single();
        Assert.Equal("Agotado", r.EstadoRotacion);
        Assert.Equal("Reponer stock", r.AccionSugerida);
    }

    [Fact]
    public async Task GetRotacionProductos_StockCeroSinVentas_SigueSiendoSinRotacion()
    {
        // Stock 0 sin ventas en el período: sigue clasificando "Sin rotación"
        var (svc, db) = TestDbHelper.Create(nameof(GetRotacionProductos_StockCeroSinVentas_SigueSiendoSinRotacion));
        var cat = CrearCategoria(db);

        CrearProducto(db, cat.Id, "MuertoSinStock", "SKU-M", stock: 0, precio: 100m);

        var result = await svc.GetRotacionProductosAsync();

        var r = result.Single();
        Assert.Equal("Sin rotación", r.EstadoRotacion);
        Assert.Equal("Descontinuar / limpiar stock", r.AccionSugerida);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetTotalesMovimientosAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTotalesMovimientos_DatosMezclados_SeparaIngresosDeEgresos()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetTotalesMovimientos_DatosMezclados_SeparaIngresosDeEgresos));

        db.MovimientosFinancieros.AddRange(
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 100.25m, Description = "Venta 1" },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 200.50m, Description = "Venta 2" },
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 50.10m, Description = "Gasto 1" },
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 9.90m, Description = "Gasto 2" });
        db.SaveChanges();

        var (ingresos, egresos) = await svc.GetTotalesMovimientosAsync();

        // Esperados calculados en C# (Amount se persiste como TEXT en SQLite)
        Assert.Equal(100.25m + 200.50m, ingresos);
        Assert.Equal(50.10m + 9.90m, egresos);
    }

    [Fact]
    public async Task GetTotalesMovimientos_MontosGrandes_NoPierdePrecisionDecimal()
    {
        // Amount se persiste como TEXT: sumar del lado SQL coerciona a REAL (double)
        // y pierde precisión con montos grandes. La suma debe hacerse en memoria.
        var (svc, db) = TestDbHelper.Create(nameof(GetTotalesMovimientos_MontosGrandes_NoPierdePrecisionDecimal));

        db.MovimientosFinancieros.AddRange(
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 10000000000000000.01m, Description = "Grande" },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 0.01m, Description = "Chico" });
        db.SaveChanges();

        var (ingresos, _) = await svc.GetTotalesMovimientosAsync();

        Assert.Equal(10000000000000000.02m, ingresos);
    }

    [Fact]
    public async Task GetTotalesMovimientos_BaseVacia_RetornaCeros()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetTotalesMovimientos_BaseVacia_RetornaCeros));

        var (ingresos, egresos) = await svc.GetTotalesMovimientosAsync();

        Assert.Equal(0m, ingresos);
        Assert.Equal(0m, egresos);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetMovimientosPaginadosAsync / GetTotalMovimientosAsync
    // ──────────────────────────────────────────────────────────────────────

    private static void SeedMovimientosPaginacion(StockDbContext db)
    {
        // Mov1 es el más reciente, Mov5 el más antiguo
        for (int i = 1; i <= 5; i++)
        {
            db.MovimientosFinancieros.Add(new MovimientoFinanciero
            {
                Type = TipoMovimiento.Ingreso,
                Amount = i * 100m,
                Description = $"Mov{i}",
                Date = DateTime.Today.AddDays(-i)
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task GetMovimientosPaginados_PaginaCorrecta_OrdenDescendentePorFecha()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetMovimientosPaginados_PaginaCorrecta_OrdenDescendentePorFecha));
        SeedMovimientosPaginacion(db);

        var pagina1 = await svc.GetMovimientosPaginadosAsync(page: 1, pageSize: 2);
        var pagina2 = await svc.GetMovimientosPaginadosAsync(page: 2, pageSize: 2);
        var pagina3 = await svc.GetMovimientosPaginadosAsync(page: 3, pageSize: 2);

        Assert.Equal(new[] { "Mov1", "Mov2" }, pagina1.Select(m => m.Description).ToArray());
        Assert.Equal(new[] { "Mov3", "Mov4" }, pagina2.Select(m => m.Description).ToArray());
        Assert.Equal(new[] { "Mov5" }, pagina3.Select(m => m.Description).ToArray()); // última página incompleta
    }

    [Fact]
    public async Task GetMovimientosPaginados_ConBusqueda_FiltraPorDescripcionSinDistinguirMayusculas()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetMovimientosPaginados_ConBusqueda_FiltraPorDescripcionSinDistinguirMayusculas));

        db.MovimientosFinancieros.AddRange(
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 100m, Description = "Pago proveedor", Date = DateTime.Today.AddDays(-1) },
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 200m, Description = "pago alquiler", Date = DateTime.Today.AddDays(-2) },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 300m, Description = "Venta mostrador", Date = DateTime.Today.AddDays(-3) });
        db.SaveChanges();

        var result = await svc.GetMovimientosPaginadosAsync(page: 1, pageSize: 10, searchTerm: "PAGO");

        Assert.Equal(2, result.Count);
        Assert.All(result, m => Assert.Contains("pago", m.Description.ToLower()));
        Assert.Equal("Pago proveedor", result[0].Description); // más reciente primero
    }

    [Fact]
    public async Task GetTotalMovimientos_ConYSinFiltro_CuentaCorrecto()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetTotalMovimientos_ConYSinFiltro_CuentaCorrecto));

        db.MovimientosFinancieros.AddRange(
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 100m, Description = "Pago proveedor" },
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 200m, Description = "pago alquiler" },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 300m, Description = "Venta mostrador" });
        db.SaveChanges();

        Assert.Equal(3, await svc.GetTotalMovimientosAsync());
        Assert.Equal(2, await svc.GetTotalMovimientosAsync("pago"));
        Assert.Equal(0, await svc.GetTotalMovimientosAsync("inexistente"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetIngresosPorDiaAsync (gráfico "Evolución de Ingresos")
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetIngresosPorDia_SumaSoloIngresosPorDia_TerminaHoy_Y_CompletaDiasVaciosConCero()
    {
        var (svc, db) = TestDbHelper.Create(nameof(GetIngresosPorDia_SumaSoloIngresosPorDia_TerminaHoy_Y_CompletaDiasVaciosConCero));
        var hoy = DateTime.Today;

        db.MovimientosFinancieros.AddRange(
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 100.10m, Description = "Hoy mañana", Date = hoy.AddHours(9) },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 200.20m, Description = "Hoy tarde", Date = hoy.AddHours(18) },
            new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 999m, Description = "Gasto hoy", Date = hoy.AddHours(10) },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 50m, Description = "Hace 6 días", Date = hoy.AddDays(-6).AddHours(12) },
            new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 70m, Description = "Hace 7 días", Date = hoy.AddDays(-7).AddHours(12) });
        db.SaveChanges();

        var serie = await svc.GetIngresosPorDiaAsync(7);

        Assert.Equal(7, serie.Count);
        Assert.Equal(hoy.AddDays(-6), serie[0].Dia); // el más viejo primero
        Assert.Equal(hoy, serie[6].Dia);             // termina hoy
        Assert.Equal(50m, serie[0].Total);           // lo de hace 7 días queda afuera
        Assert.Equal(100.10m + 200.20m, serie[6].Total); // los egresos no suman
        Assert.All(serie.Skip(1).Take(5), d => Assert.Equal(0m, d.Total));
    }

    [Fact]
    public async Task GetIngresosPorDia_BaseVacia_DevuelveTodosLosDiasEnCero()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetIngresosPorDia_BaseVacia_DevuelveTodosLosDiasEnCero));

        var serie = await svc.GetIngresosPorDiaAsync(7);

        Assert.Equal(7, serie.Count);
        Assert.All(serie, d => Assert.Equal(0m, d.Total));
    }
}
