using SistemaDeStockV3.Models;
using SistemaDeStockV3.Services;

namespace SistemaDeStockV3.Tests;

public class DataService_VentasTests
{
    /// <summary>
    /// Crea una categoría y un producto con el stock indicado, listos para vender.
    /// </summary>
    private static async Task<Producto> SeedProductoAsync(DataService svc, int stock = 10, decimal precio = 100)
    {
        var cat = new Categoria { Name = "Test Cat" };
        await svc.SaveCategoriaAsync(cat);

        var prod = new Producto
        {
            Name = "Producto Test",
            SKU = "PT-001",
            Price = precio,
            Stock = stock,
            CategoryId = cat.Id,
            UnidadMedida = "u."
        };
        await svc.SaveProductoAsync(prod);
        return prod;
    }

    private static VentaDetalle DetalleDe(Producto prod, int cantidad) => new VentaDetalle
    {
        ProductoId = prod.Id,
        Quantity = cantidad,
        UnitPrice = prod.Price
    };

    // ══════════════════════════════════════════════════════════════════════
    // ProcesarVentaAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcesarVentaAsync_Efectivo_DecrementaStock_CreaVenta_Y_MovimientoIngreso()
    {
        var (svc, _) = TestDbHelper.Create(nameof(ProcesarVentaAsync_Efectivo_DecrementaStock_CreaVenta_Y_MovimientoIngreso));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);

        var venta = new Venta { Total = 300, IsFiado = false };
        var ok = await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 3) });

        Assert.True(ok);

        // Stock decrementado
        var productos = await svc.GetProductosAsync();
        Assert.Equal(7, productos.Single().Stock);

        // Venta creada con numeración automática
        var ventas = await svc.GetVentasAsync();
        Assert.Single(ventas);
        Assert.Equal(1, ventas[0].NumeroVenta);
        Assert.Equal(300, ventas[0].Total);

        // Movimiento financiero de ingreso vinculado a la venta
        var movimientos = await svc.GetMovimientosAsync();
        Assert.Single(movimientos);
        Assert.Equal(TipoMovimiento.Ingreso, movimientos[0].Type);
        Assert.Equal(300, movimientos[0].Amount);
        Assert.Equal(venta.Id, movimientos[0].VentaId);
    }

    [Fact]
    public async Task ProcesarVentaAsync_Fiado_DecrementaStock_IncrementaCC_Y_NoCreaMovimiento()
    {
        // Regla de negocio: la venta fiada NO genera ingreso de caja;
        // el ingreso se registra recién al cobrar la deuda (PagarFiadoAsync)
        var (svc, _) = TestDbHelper.Create(nameof(ProcesarVentaAsync_Fiado_DecrementaStock_IncrementaCC_Y_NoCreaMovimiento));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);
        var cliente = new Cliente { Name = "Cliente Fiado" };
        await svc.SaveClienteAsync(cliente); // crea la CuentaCorriente automáticamente

        var venta = new Venta { Total = 500, IsFiado = true, ClienteId = cliente.Id };
        var ok = await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 5) });

        Assert.True(ok);

        var productos = await svc.GetProductosAsync();
        Assert.Equal(5, productos.Single().Stock);

        // El saldo de la cuenta corriente refleja la deuda
        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.NotNull(cc);
        Assert.Equal(500, cc!.Balance);

        // No se registró movimiento financiero
        var movimientos = await svc.GetMovimientosAsync();
        Assert.Empty(movimientos);
    }

    [Fact]
    public async Task ProcesarVentaAsync_StockInsuficiente_Lanza_Y_NoModificaNada()
    {
        var (svc, _) = TestDbHelper.Create(nameof(ProcesarVentaAsync_StockInsuficiente_Lanza_Y_NoModificaNada));
        var prod = await SeedProductoAsync(svc, stock: 2, precio: 100);

        var venta = new Venta { Total = 500, IsFiado = false };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 5) }));

        // El stock queda intacto
        var productos = await svc.GetProductosAsync();
        Assert.Equal(2, productos.Single().Stock);

        // No se creó venta ni movimiento
        Assert.Empty(await svc.GetVentasAsync());
        Assert.Empty(await svc.GetMovimientosAsync());
    }

    [Fact]
    public async Task ProcesarVentaAsync_NumeracionAutoincremental_EntreVentasSucesivas()
    {
        var (svc, _) = TestDbHelper.Create(nameof(ProcesarVentaAsync_NumeracionAutoincremental_EntreVentasSucesivas));
        var prod = await SeedProductoAsync(svc, stock: 100, precio: 10);

        var venta1 = new Venta { Total = 10, IsFiado = false };
        var venta2 = new Venta { Total = 20, IsFiado = false };
        var venta3 = new Venta { Total = 30, IsFiado = false };

        await svc.ProcesarVentaAsync(venta1, new List<VentaDetalle> { DetalleDe(prod, 1) });
        await svc.ProcesarVentaAsync(venta2, new List<VentaDetalle> { DetalleDe(prod, 2) });
        await svc.ProcesarVentaAsync(venta3, new List<VentaDetalle> { DetalleDe(prod, 3) });

        Assert.Equal(1, venta1.NumeroVenta);
        Assert.Equal(2, venta2.NumeroVenta);
        Assert.Equal(3, venta3.NumeroVenta);
    }

    [Fact]
    public async Task ProcesarVentaAsync_DespuesDeAnularLaUltimaVenta_UsaElNumeroSiguiente_SinReutilizarElAnulado()
    {
        // Regresión: el máximo se calculaba respetando el filtro de soft-delete, así que
        // al anular la última venta el próximo número chocaba con el índice UNIQUE
        // de la venta anulada y el POS no podía volver a vender.
        var (svc, _) = TestDbHelper.Create(nameof(ProcesarVentaAsync_DespuesDeAnularLaUltimaVenta_UsaElNumeroSiguiente_SinReutilizarElAnulado));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);

        var venta1 = new Venta { Total = 100, IsFiado = false };
        await svc.ProcesarVentaAsync(venta1, new List<VentaDetalle> { DetalleDe(prod, 1) });
        await svc.AnularVentaAsync(venta1.Id);

        var venta2 = new Venta { Total = 100, IsFiado = false };
        await svc.ProcesarVentaAsync(venta2, new List<VentaDetalle> { DetalleDe(prod, 1) });

        Assert.Equal(2, venta2.NumeroVenta);
    }

    [Fact]
    public async Task ProcesarVentaAsync_DespuesDeUnaVentaFallida_LaSiguienteFunciona_Y_DescuentaSoloLoVendido()
    {
        // Regresión: el rollback deshacía la transacción pero el contexto seguía con la
        // venta fallida pendiente y el stock ya descontado en memoria. La venta siguiente
        // en la misma pantalla volvía a fallar (o descontaba de más).
        var (svc, db) = TestDbHelper.Create(nameof(ProcesarVentaAsync_DespuesDeUnaVentaFallida_LaSiguienteFunciona_Y_DescuentaSoloLoVendido));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);

        // Forzamos una falla a mitad de camino: fiado a un cliente sin cuenta corriente
        var clienteSinCc = new Cliente { Name = "Sin C/C" };
        db.Clientes.Add(clienteSinCc);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ProcesarVentaAsync(
            new Venta { Total = 300, ClienteId = clienteSinCc.Id, IsFiado = true },
            new List<VentaDetalle> { DetalleDe(prod, 3) }));

        var venta = new Venta { Total = 100, IsFiado = false };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 1) });

        var (otroSvc, _) = TestDbHelper.Create(nameof(ProcesarVentaAsync_DespuesDeUnaVentaFallida_LaSiguienteFunciona_Y_DescuentaSoloLoVendido));
        Assert.Equal(9, (await otroSvc.GetProductosAsync()).Single().Stock);
        Assert.Equal(1, venta.NumeroVenta);
    }

    // ══════════════════════════════════════════════════════════════════════
    // AnularVentaAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AnularVentaAsync_Efectivo_RestauraStock_Y_EliminaMovimiento()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AnularVentaAsync_Efectivo_RestauraStock_Y_EliminaMovimiento));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);

        var venta = new Venta { Total = 400, IsFiado = false };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 4) });

        await svc.AnularVentaAsync(venta.Id);

        // Stock restaurado al valor original
        var productos = await svc.GetProductosAsync();
        Assert.Equal(10, productos.Single().Stock);

        // La venta queda marcada como anulada (soft delete)
        Assert.True(venta.IsDeleted);

        // El movimiento financiero fue eliminado
        Assert.Empty(await svc.GetMovimientosAsync());
    }

    [Fact]
    public async Task AnularVentaAsync_Fiado_RestauraStock_Y_RevierteSaldoCC()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AnularVentaAsync_Fiado_RestauraStock_Y_RevierteSaldoCC));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);
        var cliente = new Cliente { Name = "Cliente Fiado" };
        await svc.SaveClienteAsync(cliente);

        var venta = new Venta { Total = 600, IsFiado = true, ClienteId = cliente.Id };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 6) });

        await svc.AnularVentaAsync(venta.Id);

        // Stock restaurado
        var productos = await svc.GetProductosAsync();
        Assert.Equal(10, productos.Single().Stock);

        // El saldo de la CC vuelve a cero
        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.NotNull(cc);
        Assert.Equal(0, cc!.Balance);

        Assert.True(venta.IsDeleted);
    }

    [Fact]
    public async Task AnularVentaAsync_VentaYaAnulada_Lanza()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AnularVentaAsync_VentaYaAnulada_Lanza));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);

        var venta = new Venta { Total = 100, IsFiado = false };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 1) });
        await svc.AnularVentaAsync(venta.Id);

        // Anular dos veces no debe duplicar la restitución de stock
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.AnularVentaAsync(venta.Id));

        var productos = await svc.GetProductosAsync();
        Assert.Equal(10, productos.Single().Stock);
    }

    // ══════════════════════════════════════════════════════════════════════
    // PagarFiadoAsync
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PagarFiadoAsync_PagoValido_ReduceSaldo_Y_CreaMovimientoIngreso()
    {
        var (svc, _) = TestDbHelper.Create(nameof(PagarFiadoAsync_PagoValido_ReduceSaldo_Y_CreaMovimientoIngreso));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);
        var cliente = new Cliente { Name = "Deudor" };
        await svc.SaveClienteAsync(cliente);

        var venta = new Venta { Total = 800, IsFiado = true, ClienteId = cliente.Id };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 8) });

        await svc.PagarFiadoAsync(cliente.Id, 300);

        // El saldo se reduce en el monto pagado
        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.Equal(500, cc!.Balance);

        // Se registra el ingreso de caja por el cobro
        var movimientos = await svc.GetMovimientosAsync();
        Assert.Single(movimientos);
        Assert.Equal(TipoMovimiento.Ingreso, movimientos[0].Type);
        Assert.Equal(300, movimientos[0].Amount);
        Assert.Contains("Deudor", movimientos[0].Description);
    }

    [Fact]
    public async Task PagarFiadoAsync_MontoCeroONegativo_Lanza()
    {
        var (svc, _) = TestDbHelper.Create(nameof(PagarFiadoAsync_MontoCeroONegativo_Lanza));
        var cliente = new Cliente { Name = "Cliente" };
        await svc.SaveClienteAsync(cliente);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PagarFiadoAsync(cliente.Id, 0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PagarFiadoAsync(cliente.Id, -50));

        // No se registró ningún movimiento
        Assert.Empty(await svc.GetMovimientosAsync());
    }

    [Fact]
    public async Task PagarFiadoAsync_MontoMayorAlSaldo_Lanza_Y_NoModificaSaldo()
    {
        var (svc, _) = TestDbHelper.Create(nameof(PagarFiadoAsync_MontoMayorAlSaldo_Lanza_Y_NoModificaSaldo));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);
        var cliente = new Cliente { Name = "Cliente" };
        await svc.SaveClienteAsync(cliente);

        var venta = new Venta { Total = 200, IsFiado = true, ClienteId = cliente.Id };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 2) });

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.PagarFiadoAsync(cliente.Id, 500));

        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.Equal(200, cc!.Balance);
        Assert.Empty(await svc.GetMovimientosAsync());
    }

    [Fact]
    public async Task AnularVentaAsync_FiadoYaCobrado_CreaEgresoDevolucion_Y_NoDejaSaldoNegativo()
    {
        // Venta fiada cobrada en su totalidad y luego anulada: el saldo no queda
        // negativo; se registra un egreso de devolución por lo ya cobrado
        var (svc, _) = TestDbHelper.Create(nameof(AnularVentaAsync_FiadoYaCobrado_CreaEgresoDevolucion_Y_NoDejaSaldoNegativo));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);
        var cliente = new Cliente { Name = "Cliente" };
        await svc.SaveClienteAsync(cliente);

        var venta = new Venta { Total = 500, IsFiado = true, ClienteId = cliente.Id };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 5) });
        await svc.PagarFiadoAsync(cliente.Id, 500); // deuda saldada

        await svc.AnularVentaAsync(venta.Id);

        // Stock restaurado y saldo en cero (no -500)
        var productos = await svc.GetProductosAsync();
        Assert.Equal(10, productos.Single().Stock);
        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.Equal(0, cc!.Balance);

        // El ingreso del cobro sigue (la plata entró) y se compensa con un egreso
        var movimientos = await svc.GetMovimientosAsync();
        Assert.Equal(2, movimientos.Count);
        Assert.Single(movimientos, m => m.Type == TipoMovimiento.Ingreso && m.Amount == 500);
        var egreso = Assert.Single(movimientos, m => m.Type == TipoMovimiento.Egreso);
        Assert.Equal(500, egreso.Amount);
        Assert.Contains("Devolución", egreso.Description);
    }

    [Fact]
    public async Task AnularVentaAsync_FiadoParcialmentePagado_EgresoSoloPorLoCobrado()
    {
        // Venta fiada de 500 con pago parcial de 200: al anular se revierte la
        // deuda pendiente (300) y se devuelve solo lo cobrado (egreso de 200)
        var (svc, _) = TestDbHelper.Create(nameof(AnularVentaAsync_FiadoParcialmentePagado_EgresoSoloPorLoCobrado));
        var prod = await SeedProductoAsync(svc, stock: 10, precio: 100);
        var cliente = new Cliente { Name = "Cliente" };
        await svc.SaveClienteAsync(cliente);

        var venta = new Venta { Total = 500, IsFiado = true, ClienteId = cliente.Id };
        await svc.ProcesarVentaAsync(venta, new List<VentaDetalle> { DetalleDe(prod, 5) });
        await svc.PagarFiadoAsync(cliente.Id, 200); // saldo queda en 300

        await svc.AnularVentaAsync(venta.Id);

        var cc = await svc.GetCuentaCorrienteAsync(cliente.Id);
        Assert.Equal(0, cc!.Balance);

        var movimientos = await svc.GetMovimientosAsync();
        var egreso = Assert.Single(movimientos, m => m.Type == TipoMovimiento.Egreso);
        Assert.Equal(200, egreso.Amount);
    }
}
