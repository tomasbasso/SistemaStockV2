using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

public class DataService_MovimientosTests
{
    [Fact]
    public async Task AddMovimientoAsync_Crea_NuevoMovimiento()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AddMovimientoAsync_Crea_NuevoMovimiento));
        var mov = new MovimientoFinanciero
        {
            Type = TipoMovimiento.Ingreso,
            Amount = 50000,
            Description = "Capital inicial"
        };

        await svc.AddMovimientoAsync(mov);
        var result = await svc.GetMovimientosAsync();

        Assert.Single(result);
        Assert.Equal(TipoMovimiento.Ingreso, result[0].Type);
        Assert.Equal(50000, result[0].Amount);
    }

    [Fact]
    public async Task DeleteMovimientoAsync_Elimina_Movimiento()
    {
        var (svc, _) = TestDbHelper.Create(nameof(DeleteMovimientoAsync_Elimina_Movimiento));
        var mov = new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 1000, Description = "Test" };
        await svc.AddMovimientoAsync(mov);

        await svc.DeleteMovimientoAsync(mov.Id);
        var result = await svc.GetMovimientosAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMovimientosAsync_RetornaOrdenado_FechaMasRecientePrimero()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetMovimientosAsync_RetornaOrdenado_FechaMasRecientePrimero));

        var viejo = new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 100, Description = "Viejo", Date = DateTime.Now.AddDays(-5) };
        var nuevo = new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 200, Description = "Nuevo", Date = DateTime.Now };

        await svc.AddMovimientoAsync(viejo);
        await svc.AddMovimientoAsync(nuevo);
        var result = await svc.GetMovimientosAsync();

        Assert.Equal("Nuevo", result[0].Description);
        Assert.Equal("Viejo", result[1].Description);
    }

    [Fact]
    public async Task Movimientos_Ingreso_Y_Egreso_SeAlmacenanCorrectamente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(Movimientos_Ingreso_Y_Egreso_SeAlmacenanCorrectamente));

        await svc.AddMovimientoAsync(new MovimientoFinanciero { Type = TipoMovimiento.Ingreso, Amount = 10000, Description = "Venta" });
        await svc.AddMovimientoAsync(new MovimientoFinanciero { Type = TipoMovimiento.Egreso, Amount = 3000, Description = "Gasto" });

        var result = await svc.GetMovimientosAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.Type == TipoMovimiento.Ingreso);
        Assert.Contains(result, m => m.Type == TipoMovimiento.Egreso);
    }
}
