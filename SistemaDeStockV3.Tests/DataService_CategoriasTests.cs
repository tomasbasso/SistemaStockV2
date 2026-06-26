using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

public class DataService_CategoriasTests
{
    [Fact]
    public async Task GetCategoriasAsync_DevuelveListaVacia_SiNoHayCategorias()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetCategoriasAsync_DevuelveListaVacia_SiNoHayCategorias));

        var result = await svc.GetCategoriasAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveCategoriaAsync_Crea_NuevaCategoria()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SaveCategoriaAsync_Crea_NuevaCategoria));
        var cat = new Categoria { Name = "Herramientas" };

        await svc.SaveCategoriaAsync(cat);
        var result = await svc.GetCategoriasAsync();

        Assert.Single(result);
        Assert.Equal("Herramientas", result[0].Name);
    }

    [Fact]
    public async Task SaveCategoriaAsync_Actualiza_CategoriaExistente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(SaveCategoriaAsync_Actualiza_CategoriaExistente));
        var cat = new Categoria { Name = "Original" };
        await svc.SaveCategoriaAsync(cat);

        cat.Name = "Actualizado";
        await svc.SaveCategoriaAsync(cat);
        var result = await svc.GetCategoriasAsync();

        Assert.Single(result);
        Assert.Equal("Actualizado", result[0].Name);
    }

    [Fact]
    public async Task DeleteCategoriaAsync_Elimina_CategoriaExistente()
    {
        var (svc, _) = TestDbHelper.Create(nameof(DeleteCategoriaAsync_Elimina_CategoriaExistente));
        var cat = new Categoria { Name = "A Eliminar" };
        await svc.SaveCategoriaAsync(cat);

        await svc.DeleteCategoriaAsync(cat.Id);
        var result = await svc.GetCategoriasAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCategoriasAsync_RetornaOrdenado_PorNombre()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetCategoriasAsync_RetornaOrdenado_PorNombre));
        await svc.SaveCategoriaAsync(new Categoria { Name = "Zanahorias" });
        await svc.SaveCategoriaAsync(new Categoria { Name = "Almendras" });
        await svc.SaveCategoriaAsync(new Categoria { Name = "Manzanas" });

        var result = await svc.GetCategoriasAsync();

        Assert.Equal("Almendras", result[0].Name);
        Assert.Equal("Manzanas", result[1].Name);
        Assert.Equal("Zanahorias", result[2].Name);
    }
}
