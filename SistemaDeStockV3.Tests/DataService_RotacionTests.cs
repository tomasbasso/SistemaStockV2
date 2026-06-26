using SistemaDeStockV3.Models;
using SistemaDeStockV3.Data;
using Xunit;

namespace SistemaDeStockV3.Tests;

public class DataService_RotacionTests
{
    private async Task SetupBaseData(StockDbContext db, DateTime ventaDate, bool ventaDeleted, int unidades, int stock)
    {
        var cat = new Categoria { Name = "Test" };
        db.Categorias.Add(cat);
        db.SaveChanges();

        var p1 = new Producto { Name = "P1", SKU = "P1", Stock = stock, CategoryId = cat.Id };
        db.Productos.Add(p1);
        db.SaveChanges();

        var venta = new Venta { NumeroVenta = 1, Date = ventaDate, IsDeleted = ventaDeleted };
        db.Ventas.Add(venta);
        db.SaveChanges();

        db.VentaDetalles.Add(new VentaDetalle { VentaId = venta.Id, ProductoId = p1.Id, Quantity = unidades });
        db.SaveChanges();
    }

    [Fact]
    public async Task CalcularRotacion_ConVentasYStock_RetornaValorCorrecto()
    {
        var (svc, db) = TestDbHelper.Create(nameof(CalcularRotacion_ConVentasYStock_RetornaValorCorrecto));
        await SetupBaseData(db, DateTime.Today.AddMonths(-6), false, 200, 100);
        
        var rotacion = await svc.CalcularRotacionAnualAsync();
        Assert.Equal(2.0, rotacion); 
    }
    
    [Fact]
    public async Task CalcularRotacion_FiltrarVentasAntiguas_NoLasIncluye()
    {
        var (svc, db) = TestDbHelper.Create(nameof(CalcularRotacion_FiltrarVentasAntiguas_NoLasIncluye));
        await SetupBaseData(db, DateTime.Today.AddYears(-2), false, 1000, 100);
        
        var rotacion = await svc.CalcularRotacionAnualAsync();
        Assert.Equal(0, rotacion); 
    }
    
    [Fact]
    public async Task CalcularRotacion_VentasEliminadas_NoLasIncluye()
    {
        var (svc, db) = TestDbHelper.Create(nameof(CalcularRotacion_VentasEliminadas_NoLasIncluye));
        await SetupBaseData(db, DateTime.Today, true, 200, 100);
        
        var rotacion = await svc.CalcularRotacionAnualAsync();
        Assert.Equal(0, rotacion);
    }
    
    [Fact]
    public async Task CalcularRotacion_ProductosEliminados_NoContanEnStock()
    {
        var (svc, db) = TestDbHelper.Create(nameof(CalcularRotacion_ProductosEliminados_NoContanEnStock));
        
        var cat = new Categoria { Name = "Test" };
        db.Categorias.Add(cat);
        db.SaveChanges();

        var p1 = new Producto { Name = "P1", SKU = "P1", Stock = 100, CategoryId = cat.Id };
        var p2 = new Producto { Name = "P2", SKU = "P2", Stock = 50, IsDeleted = true, CategoryId = cat.Id };
        db.Productos.AddRange(p1, p2);
        db.SaveChanges();

        var venta = new Venta { NumeroVenta = 1, Date = DateTime.Today, IsDeleted = false };
        db.Ventas.Add(venta);
        db.SaveChanges();

        db.VentaDetalles.Add(new VentaDetalle { VentaId = venta.Id, ProductoId = p1.Id, Quantity = 200 });
        db.SaveChanges();

        var rotacion = await svc.CalcularRotacionAnualAsync();
        Assert.Equal(2.0, rotacion); 
    }
    
    [Fact]
    public async Task CalcularRotacion_NumerosGrandes_MantienePrecision()
    {
        var (svc, db) = TestDbHelper.Create(nameof(CalcularRotacion_NumerosGrandes_MantienePrecision));
        await SetupBaseData(db, DateTime.Today, false, 3333333, 1000000);
        
        var rotacion = await svc.CalcularRotacionAnualAsync();
        Assert.Equal(3.33, rotacion); 
    }
}
