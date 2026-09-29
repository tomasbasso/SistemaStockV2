using Microsoft.EntityFrameworkCore;
using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

public class DataService_ProductosAvanzadosTests
{
    private static Categoria CategoriaTest() => new Categoria { Name = "Test Cat" };

    private static Producto NuevoProducto(Guid categoriaId, string nombre, string sku, decimal precio = 100, int stock = 10)
        => new Producto
        {
            Name = nombre,
            SKU = sku,
            Price = precio,
            Stock = stock,
            CategoryId = categoriaId,
            UnidadMedida = "u."
        };

    // ──────────────────────────────────────────────────────────────────────
    // CambiarStockAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarStockAsync_Incrementa_Stock()
    {
        var (svc, _) = TestDbHelper.Create(nameof(CambiarStockAsync_Incrementa_Stock));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Martillo", "M-001", stock: 10);
        await svc.SaveProductoAsync(prod);

        await svc.CambiarStockAsync(prod.Id, 5);
        var result = await svc.GetProductosAsync();

        Assert.Equal(15, result.Single().Stock);
    }

    [Fact]
    public async Task CambiarStockAsync_Decrementa_Stock()
    {
        var (svc, _) = TestDbHelper.Create(nameof(CambiarStockAsync_Decrementa_Stock));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Martillo", "M-001", stock: 10);
        await svc.SaveProductoAsync(prod);

        await svc.CambiarStockAsync(prod.Id, -4);
        var result = await svc.GetProductosAsync();

        Assert.Equal(6, result.Single().Stock);
    }

    [Fact]
    public async Task CambiarStockAsync_NuncaBajaDeCero()
    {
        // Regla de negocio: si la variación deja el stock negativo, se fija en 0
        var (svc, _) = TestDbHelper.Create(nameof(CambiarStockAsync_NuncaBajaDeCero));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Martillo", "M-001", stock: 3);
        await svc.SaveProductoAsync(prod);

        await svc.CambiarStockAsync(prod.Id, -10);
        var result = await svc.GetProductosAsync();

        Assert.Equal(0, result.Single().Stock);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetTotalProductosAsync / GetProductosPaginadosAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTotalProductosAsync_SinFiltro_CuentaTodos()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetTotalProductosAsync_SinFiltro_CuentaTodos));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "T-1"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Amoladora", "A-1"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Lijadora", "L-1"));

        var total = await svc.GetTotalProductosAsync();

        Assert.Equal(3, total);
    }

    [Fact]
    public async Task GetTotalProductosAsync_BusquedaMultiPalabra_ExigeTodasLasPalabras()
    {
        // La búsqueda tokeniza por espacios y cada palabra debe matchear
        // en Name o SKU (AND entre palabras)
        var (svc, _) = TestDbHelper.Create(nameof(GetTotalProductosAsync_BusquedaMultiPalabra_ExigeTodasLasPalabras));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro Bosch 700W", "TB-700"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro Makita 500W", "TM-500"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Amoladora Bosch", "AB-1"));

        var total = await svc.GetTotalProductosAsync("taladro bosch");

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetProductosPaginadosAsync_Pagina2_DevuelveLosSiguientes()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetProductosPaginadosAsync_Pagina2_DevuelveLosSiguientes));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        // Orden alfabético: Alfa, Beta, Delta, Gamma, Omega
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Gamma", "G-1"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Alfa", "A-1"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Omega", "O-1"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Beta", "B-1"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Delta", "D-1"));

        var pagina1 = await svc.GetProductosPaginadosAsync(page: 1, pageSize: 2);
        var pagina2 = await svc.GetProductosPaginadosAsync(page: 2, pageSize: 2);
        var pagina3 = await svc.GetProductosPaginadosAsync(page: 3, pageSize: 2);

        Assert.Equal(new[] { "Alfa", "Beta" }, pagina1.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "Delta", "Gamma" }, pagina2.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "Omega" }, pagina3.Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetProductosPaginadosAsync_BuscaPorSKU()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetProductosPaginadosAsync_BuscaPorSKU));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "XYZ-99"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Amoladora", "ABC-11"));

        var result = await svc.GetProductosPaginadosAsync(page: 1, pageSize: 10, searchTerm: "xyz");

        Assert.Single(result);
        Assert.Equal("Taladro", result[0].Name);
    }

    [Fact]
    public async Task GetProductosPaginadosAsync_BusquedaSinResultados_DevuelveVacio()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetProductosPaginadosAsync_BusquedaSinResultados_DevuelveVacio));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "T-1"));

        var result = await svc.GetProductosPaginadosAsync(page: 1, pageSize: 10, searchTerm: "inexistente");
        var total = await svc.GetTotalProductosAsync("inexistente");

        Assert.Empty(result);
        Assert.Equal(0, total);
    }

    [Theory]
    [InlineData("cañ")]
    [InlineData("CAÑ")]
    [InlineData("caño")]
    public async Task GetProductosPaginadosAsync_BuscaConEñe_SinImportarMayusculas(string termino)
    {
        var (svc, _) = TestDbHelper.Create($"{nameof(GetProductosPaginadosAsync_BuscaConEñe_SinImportarMayusculas)}_{termino}");
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "CAÑO CORTINA 1/2 BRONCEADO", "00-1610"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "CANILLA ESFÉRICA", "00-2000"));

        var result = await svc.GetProductosPaginadosAsync(page: 1, pageSize: 10, searchTerm: termino);
        var total = await svc.GetTotalProductosAsync(termino);

        Assert.Single(result);
        Assert.Equal("CAÑO CORTINA 1/2 BRONCEADO", result[0].Name);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetProductosPaginadosAsync_BuscaConTildeEnMayuscula()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetProductosPaginadosAsync_BuscaConTildeEnMayuscula));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "CANILLA ESFÉRICA", "00-2000"));

        var result = await svc.GetProductosPaginadosAsync(page: 1, pageSize: 10, searchTerm: "esférica");

        Assert.Single(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // GetProductoPorCodigoBarrasAsync / AsignarCodigoBarrasAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AsignarCodigoBarrasAsync_AsignaYPermiteLookup()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AsignarCodigoBarrasAsync_AsignaYPermiteLookup));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Gaseosa", "G-1");
        await svc.SaveProductoAsync(prod);

        await svc.AsignarCodigoBarrasAsync(prod.Id, "7790001112223");
        var encontrado = await svc.GetProductoPorCodigoBarrasAsync("7790001112223");

        Assert.NotNull(encontrado);
        Assert.Equal(prod.Id, encontrado!.Id);
    }

    [Fact]
    public async Task GetProductoPorCodigoBarrasAsync_CodigoInexistente_DevuelveNull()
    {
        var (svc, _) = TestDbHelper.Create(nameof(GetProductoPorCodigoBarrasAsync_CodigoInexistente_DevuelveNull));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Gaseosa", "G-1"));

        var result = await svc.GetProductoPorCodigoBarrasAsync("0000000000000");

        Assert.Null(result);
    }

    [Fact]
    public async Task AsignarCodigoBarrasAsync_CodigoDuplicado_LanzaInvalidOperationException()
    {
        // El mecanismo real es una excepción: InvalidOperationException con el
        // nombre del producto que ya tiene el código asignado
        var (svc, _) = TestDbHelper.Create(nameof(AsignarCodigoBarrasAsync_CodigoDuplicado_LanzaInvalidOperationException));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod1 = NuevoProducto(cat.Id, "Gaseosa", "G-1");
        var prod2 = NuevoProducto(cat.Id, "Cerveza", "C-1");
        await svc.SaveProductoAsync(prod1);
        await svc.SaveProductoAsync(prod2);

        await svc.AsignarCodigoBarrasAsync(prod1.Id, "7790001112223");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AsignarCodigoBarrasAsync(prod2.Id, "7790001112223"));

        Assert.Contains("Gaseosa", ex.Message);
    }

    [Fact]
    public async Task AsignarCodigoBarrasAsync_MismoProductoMismoCodigo_NoFalla()
    {
        // Reasignar el mismo código al mismo producto no es duplicado
        // (la verificación excluye el propio producto por Id)
        var (svc, _) = TestDbHelper.Create(nameof(AsignarCodigoBarrasAsync_MismoProductoMismoCodigo_NoFalla));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Gaseosa", "G-1");
        await svc.SaveProductoAsync(prod);

        await svc.AsignarCodigoBarrasAsync(prod.Id, "7790001112223");
        await svc.AsignarCodigoBarrasAsync(prod.Id, "7790001112223");

        var encontrado = await svc.GetProductoPorCodigoBarrasAsync("7790001112223");
        Assert.Equal(prod.Id, encontrado!.Id);
    }

    [Fact]
    public async Task AsignarCodigoBarrasAsync_ProductoInexistente_LanzaInvalidOperationException()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AsignarCodigoBarrasAsync_ProductoInexistente_LanzaInvalidOperationException));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AsignarCodigoBarrasAsync(Guid.NewGuid(), "123456789"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // AjustarPreciosPorcentajeAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AjustarPreciosPorcentajeAsync_AumentoDiezPorciento_AplicaCorrecto()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AjustarPreciosPorcentajeAsync_AumentoDiezPorciento_AplicaCorrecto));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Taladro", "T-1", precio: 1000);
        await svc.SaveProductoAsync(prod);

        await svc.AjustarPreciosPorcentajeAsync(new List<Guid> { prod.Id }, 10);

        // Materializamos con GetProductosAsync (ToList) antes de comparar montos:
        // decimal se guarda como TEXT en SQLite y no se puede comparar en SQL
        var result = await svc.GetProductosAsync();
        Assert.Equal(1100m, result.Single().Price);
    }

    [Fact]
    public async Task AjustarPreciosPorcentajeAsync_DescuentoDiezPorciento_AplicaCorrecto()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AjustarPreciosPorcentajeAsync_DescuentoDiezPorciento_AplicaCorrecto));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Taladro", "T-1", precio: 1000);
        await svc.SaveProductoAsync(prod);

        await svc.AjustarPreciosPorcentajeAsync(new List<Guid> { prod.Id }, -10);

        var result = await svc.GetProductosAsync();
        Assert.Equal(900m, result.Single().Price);
    }

    [Fact]
    public async Task AjustarPreciosPorcentajeAsync_SoloAfectaLosIdsIndicados()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AjustarPreciosPorcentajeAsync_SoloAfectaLosIdsIndicados));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var afectado = NuevoProducto(cat.Id, "Afectado", "A-1", precio: 100);
        var intacto = NuevoProducto(cat.Id, "Intacto", "I-1", precio: 100);
        await svc.SaveProductoAsync(afectado);
        await svc.SaveProductoAsync(intacto);

        await svc.AjustarPreciosPorcentajeAsync(new List<Guid> { afectado.Id }, 50);

        var result = await svc.GetProductosAsync();
        Assert.Equal(150m, result.First(p => p.SKU == "A-1").Price);
        Assert.Equal(100m, result.First(p => p.SKU == "I-1").Price);
    }

    [Fact]
    public async Task AjustarPreciosPorcentajeAsync_CreaRegistroDeHistorial()
    {
        var (svc, _) = TestDbHelper.Create(nameof(AjustarPreciosPorcentajeAsync_CreaRegistroDeHistorial));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Taladro", "T-1", precio: 1000);
        await svc.SaveProductoAsync(prod);

        await svc.AjustarPreciosPorcentajeAsync(new List<Guid> { prod.Id }, 10);

        var historial = await svc.GetHistorialPreciosAsync();
        var registro = Assert.Single(historial);
        Assert.Equal(prod.Id, registro.ProductoId);
        Assert.Equal(1000m, registro.PrecioAnterior);
        Assert.Equal(1100m, registro.PrecioNuevo);
    }

    [Fact]
    public async Task AjustarPreciosPorcentajeAsync_PorcentajeCero_NoCreaHistorial()
    {
        // RegistrarHistorialPrecio no registra si el precio no cambió
        var (svc, _) = TestDbHelper.Create(nameof(AjustarPreciosPorcentajeAsync_PorcentajeCero_NoCreaHistorial));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Taladro", "T-1", precio: 1000);
        await svc.SaveProductoAsync(prod);

        await svc.AjustarPreciosPorcentajeAsync(new List<Guid> { prod.Id }, 0);

        var historial = await svc.GetHistorialPreciosAsync();
        Assert.Empty(historial);
    }

    // ──────────────────────────────────────────────────────────────────────
    // DeleteProductosAsync (bulk soft-delete)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteProductosAsync_BulkSoftDelete_NoAparecenEnGetProductos()
    {
        var (svc, _) = TestDbHelper.Create(nameof(DeleteProductosAsync_BulkSoftDelete_NoAparecenEnGetProductos));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod1 = NuevoProducto(cat.Id, "Borrar1", "B-1");
        var prod2 = NuevoProducto(cat.Id, "Borrar2", "B-2");
        var prod3 = NuevoProducto(cat.Id, "Queda", "Q-1");
        await svc.SaveProductoAsync(prod1);
        await svc.SaveProductoAsync(prod2);
        await svc.SaveProductoAsync(prod3);

        await svc.DeleteProductosAsync(new List<Guid> { prod1.Id, prod2.Id });

        var result = await svc.GetProductosAsync();
        Assert.Single(result);
        Assert.Equal("Queda", result[0].Name);
    }

    [Fact]
    public async Task DeleteProductosAsync_ListaVacia_NoHaceNada()
    {
        var (svc, _) = TestDbHelper.Create(nameof(DeleteProductosAsync_ListaVacia_NoHaceNada));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Sobrevive", "S-1"));

        await svc.DeleteProductosAsync(new List<Guid>());

        var result = await svc.GetProductosAsync();
        Assert.Single(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // ExisteProductoPorSKUAsync
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExisteProductoPorSKUAsync_SKUExistente_DevuelveTrue()
    {
        var (svc, _) = TestDbHelper.Create(nameof(ExisteProductoPorSKUAsync_SKUExistente_DevuelveTrue));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "SKU-DUP"));

        var existe = await svc.ExisteProductoPorSKUAsync("SKU-DUP", Guid.NewGuid());

        Assert.True(existe);
    }

    [Fact]
    public async Task ExisteProductoPorSKUAsync_SKUInexistente_DevuelveFalse()
    {
        var (svc, _) = TestDbHelper.Create(nameof(ExisteProductoPorSKUAsync_SKUInexistente_DevuelveFalse));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "SKU-A"));

        var existe = await svc.ExisteProductoPorSKUAsync("SKU-NO-EXISTE", Guid.NewGuid());

        Assert.False(existe);
    }

    [Fact]
    public async Task ExisteProductoPorSKUAsync_ExcluyeElPropioProducto()
    {
        // Al editar un producto, su propio SKU no cuenta como duplicado
        var (svc, _) = TestDbHelper.Create(nameof(ExisteProductoPorSKUAsync_ExcluyeElPropioProducto));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var prod = NuevoProducto(cat.Id, "Taladro", "SKU-PROPIO");
        await svc.SaveProductoAsync(prod);

        var existe = await svc.ExisteProductoPorSKUAsync("SKU-PROPIO", prod.Id);

        Assert.False(existe);
    }

    [Fact]
    public async Task ExisteProductoPorSKUAsync_NoDistingueMayusculasDeMinusculas()
    {
        // La búsqueda paginada es case-insensitive; el chequeo de duplicados
        // debe serlo también para no dejar pasar "sku-dup" con "SKU-DUP" existente
        var (svc, _) = TestDbHelper.Create(nameof(ExisteProductoPorSKUAsync_NoDistingueMayusculasDeMinusculas));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "SKU-DUP"));

        var existe = await svc.ExisteProductoPorSKUAsync("sku-dup", Guid.NewGuid());

        Assert.True(existe);
    }

    // ──────────────────────────────────────────────────────────────────────
    // SKU de productos eliminados (soft-delete)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveProductoAsync_SkuDeUnProductoEliminado_SePuedeReutilizar()
    {
        // Regresión: el producto eliminado seguía ocupando el SKU en el índice UNIQUE.
        // La validación de la UI decía "libre" y el INSERT explotaba con error de SQLite.
        var (svc, _) = TestDbHelper.Create(nameof(SaveProductoAsync_SkuDeUnProductoEliminado_SePuedeReutilizar));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        var viejo = NuevoProducto(cat.Id, "Martillo viejo", "MART-01");
        await svc.SaveProductoAsync(viejo);
        await svc.DeleteProductoAsync(viejo.Id);

        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Martillo nuevo", "MART-01"));

        var activo = Assert.Single(await svc.GetProductosAsync());
        Assert.Equal("Martillo nuevo", activo.Name);
    }

    [Fact]
    public async Task SaveProductoAsync_SkuDeOtroProductoActivo_LaBaseLoSigueRechazando()
    {
        // Resguardo: liberar el SKU de los eliminados no debe permitir duplicados entre activos
        var (svc, _) = TestDbHelper.Create(nameof(SaveProductoAsync_SkuDeOtroProductoActivo_LaBaseLoSigueRechazando));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Taladro", "DUP-01"));

        await Assert.ThrowsAsync<DbUpdateException>(() => svc.SaveProductoAsync(NuevoProducto(cat.Id, "Otro", "DUP-01")));
    }

    [Fact]
    public async Task InitializeDatabase_BaseInstaladaConIndiceSkuViejo_LoActualiza_Y_PermiteReutilizarSku()
    {
        // Las bases ya instaladas en las PC de los clientes tienen el índice UNIQUE sin filtro.
        // Al arrancar, la app debe reemplazarlo sin perder datos.
        var (svc, db) = TestDbHelper.Create(nameof(InitializeDatabase_BaseInstaladaConIndiceSkuViejo_LoActualiza_Y_PermiteReutilizarSku));
        db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_Productos_SKU\"; CREATE UNIQUE INDEX \"IX_Productos_SKU\" ON \"Productos\" (\"SKU\");");
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        var viejo = NuevoProducto(cat.Id, "Pinza vieja", "PIN-01");
        await svc.SaveProductoAsync(viejo);
        await svc.DeleteProductoAsync(viejo.Id);

        await db.InitializeDatabaseAsync();
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Pinza nueva", "PIN-01"));

        Assert.Equal("Pinza nueva", Assert.Single(await svc.GetProductosAsync()).Name);
    }

    [Fact]
    public async Task InitializeDatabase_BaseInstaladaSinIndice_ConSkusRepetidos_ArrancaIgual_Y_NoPierdeDatos()
    {
        // Las bases instaladas no tenían ningún índice sobre SKU, así que pueden existir
        // repetidos entre activos. Crear el índice falla en ese caso, pero no debe impedir
        // que la app arranque.
        var (svc, db) = TestDbHelper.Create(nameof(InitializeDatabase_BaseInstaladaSinIndice_ConSkusRepetidos_ArrancaIgual_Y_NoPierdeDatos));
        db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_Productos_SKU\";");
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Tornillo A", "REP-01"));
        await svc.SaveProductoAsync(NuevoProducto(cat.Id, "Tornillo B", "REP-01"));

        var ex = await Record.ExceptionAsync(() => db.InitializeDatabaseAsync());

        Assert.Null(ex);
        Assert.Equal(2, (await svc.GetProductosAsync()).Count);
    }
}
