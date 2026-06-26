using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

public class DataService_ProductosTests
{
    private static Categoria CategoriaTest() => new Categoria { Name = "Test Cat" };

    [Fact]
    public async Task SaveProductoAsync_Crea_NuevoProducto()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SaveProductoAsync_Crea_NuevoProducto));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = new Producto
        {
            Name = "Taladro 700W",
            SKU = "T-001",
            Price = 85000,
            Stock = 10,
            StockMinimo = 2,
            CategoryId = cat.Id,
            UnidadMedida = "u."
        };

        await svc.SaveProductoAsync(prod);
        var result = await svc.GetProductosAsync();

        Assert.Single(result);
        Assert.Equal("Taladro 700W", result[0].Name);
        Assert.Equal(85000, result[0].Price);
    }

    [Fact]
    public async Task SaveProductoAsync_Actualiza_ProductoExistente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SaveProductoAsync_Actualiza_ProductoExistente));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = new Producto { Name = "Original", SKU = "O-001", Price = 100, Stock = 5, CategoryId = cat.Id, UnidadMedida = "u." };
        await svc.SaveProductoAsync(prod);

        prod.Name = "Actualizado";
        prod.Price = 200;
        await svc.SaveProductoAsync(prod);
        var result = await svc.GetProductosAsync();

        Assert.Single(result);
        Assert.Equal("Actualizado", result[0].Name);
        Assert.Equal(200, result[0].Price);
    }

    [Fact]
    public async Task DeleteProductoAsync_Elimina_ProductoExistente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(DeleteProductoAsync_Elimina_ProductoExistente));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = new Producto { Name = "A Borrar", SKU = "B-001", Price = 50, Stock = 1, CategoryId = cat.Id, UnidadMedida = "u." };
        await svc.SaveProductoAsync(prod);

        await svc.DeleteProductoAsync(prod.Id);
        var result = await svc.GetProductosAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProductosAsync_RetornaOrdenado_PorNombre()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetProductosAsync_RetornaOrdenado_PorNombre));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(new Producto { Name = "Zanahoria", SKU = "Z", Price = 10, CategoryId = cat.Id, UnidadMedida = "Kg" });
        await svc.SaveProductoAsync(new Producto { Name = "Almendra", SKU = "A", Price = 20, CategoryId = cat.Id, UnidadMedida = "Kg" });

        var result = await svc.GetProductosAsync();

        Assert.Equal("Almendra", result[0].Name);
        Assert.Equal("Zanahoria", result[1].Name);
    }

    [Fact]
    public async Task SaveProductoAsync_Nuevo_NoReduceStockDeOtros()
    {
        // Regla de negocio: crear un nuevo producto no modifica el stock de otros products
        var (svc, _) = TestDbHelper.Create(nameof(SaveProductoAsync_Nuevo_NoReduceStockDeOtros));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod1 = new Producto { Name = "Prod1", SKU = "P1", Price = 10, Stock = 50, CategoryId = cat.Id, UnidadMedida = "u." };
        await svc.SaveProductoAsync(prod1);

        var prod2 = new Producto { Name = "Prod2", SKU = "P2", Price = 20, Stock = 30, CategoryId = cat.Id, UnidadMedida = "u." };
        await svc.SaveProductoAsync(prod2);

        var result = await svc.GetProductosAsync();
        var prod1Result = result.First(p => p.SKU == "P1");

        Assert.Equal(50, prod1Result.Stock);
    }
}
