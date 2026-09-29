using ClosedXML.Excel;
using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// Tests de ImportarProductosDesdeExcelAsync: auto-detección de columnas,
/// parsing de moneda (coma/punto decimal), dedup por SKU y errores parciales.
/// Los .xlsx se generan en memoria con ClosedXML, la misma librería que usa producción.
/// Nota (CLAUDE.md): decimal se persiste como TEXT en SQLite — los asserts sobre
/// montos se hacen siempre sobre listas ya materializadas, nunca con ORDER BY/SUM en SQL.
/// </summary>
public class DataService_ImportExcelTests
{
    private static Categoria CategoriaTest() => new Categoria { Name = "Importados" };

    /// <summary>Crea un .xlsx en memoria y devuelve el stream posicionado en 0.</summary>
    private static MemoryStream CrearExcel(Action<IXLWorksheet> llenar)
    {
        var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("Hoja1");
            llenar(ws);
            wb.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Importar_ConEncabezadosEstandar_CreaProductos()
    {
        // Arrange
        var (svc, _) = TestDbHelper.Create(nameof(Importar_ConEncabezadosEstandar_CreaProductos));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "T-001";
            ws.Cell(2, 2).Value = "Taladro 700W";
            ws.Cell(2, 3).Value = "85000";
            ws.Cell(3, 1).Value = "A-002";
            ws.Cell(3, 2).Value = "Amoladora 900W";
            ws.Cell(3, 3).Value = "120000";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data.Importados);
        Assert.Equal(0, result.Data.Actualizados);
        Assert.Empty(result.Data.Errores);

        var productos = (await svc.GetProductosAsync()).ToList();
        Assert.Equal(2, productos.Count);

        var taladro = productos.Single(p => p.SKU == "T-001");
        Assert.Equal("Taladro 700W", taladro.Name);
        Assert.Equal(85000m, taladro.Price);
        Assert.Equal(cat.Id, taladro.CategoryId);
        // Sin columna de stock el producto entra en 0: no se inventa mercadería
        // (antes entraba con 5 y inflaba el valor de inventario). Margen 100% sin costo.
        Assert.Equal(0, taladro.Stock);
        Assert.Equal(100m, taladro.Margen);
    }

    [Fact]
    public async Task Importar_DetectaColumnas_EnOrdenDistintoAlDefault()
    {
        // Arrange: encabezados con variantes ("Producto", "Codigo", "Precio Venta")
        // en un orden distinto al default (sku=1, nombre=2, precio=3)
        var (svc, _) = TestDbHelper.Create(nameof(Importar_DetectaColumnas_EnOrdenDistintoAlDefault));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Producto";
            ws.Cell(1, 2).Value = "Codigo";
            ws.Cell(1, 3).Value = "Precio Venta";
            ws.Cell(2, 1).Value = "Martillo Galponero";
            ws.Cell(2, 2).Value = "M-100";
            ws.Cell(2, 3).Value = "9500";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);

        var productos = (await svc.GetProductosAsync()).ToList();
        var prod = Assert.Single(productos);
        Assert.Equal("Martillo Galponero", prod.Name);
        Assert.Equal("M-100", prod.SKU);
        Assert.Equal(9500m, prod.Price);
    }

    [Fact]
    public async Task Importar_SinEncabezado_UsaColumnasFijas()
    {
        // Arrange: sin fila de encabezado reconocible -> lee desde la fila 1
        // con columnas fijas: 1=SKU, 2=Nombre, 3=Precio
        var (svc, _) = TestDbHelper.Create(nameof(Importar_SinEncabezado_UsaColumnasFijas));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "D-010";
            ws.Cell(1, 2).Value = "Destornillador Phillips";
            ws.Cell(1, 3).Value = "3200";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal("Destornillador Phillips", prod.Name);
        Assert.Equal(3200m, prod.Price);
    }

    [Fact]
    public async Task Importar_PrecioConComaDecimal_ParseaFormatoArgentino()
    {
        // Arrange: "1.234,56" (punto de miles + coma decimal) y "$ 1.500,00" con símbolo
        var (svc, _) = TestDbHelper.Create(nameof(Importar_PrecioConComaDecimal_ParseaFormatoArgentino));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "C-001";
            ws.Cell(2, 2).Value = "Cinta Métrica";
            ws.Cell(2, 3).Value = "1.234,56";
            ws.Cell(3, 1).Value = "C-002";
            ws.Cell(3, 2).Value = "Calibre Digital";
            ws.Cell(3, 3).Value = "$ 1.500,00";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data.Importados);
        Assert.Empty(result.Data.Errores);

        var productos = (await svc.GetProductosAsync()).ToList();
        Assert.Equal(1234.56m, productos.Single(p => p.SKU == "C-001").Price);
        Assert.Equal(1500.00m, productos.Single(p => p.SKU == "C-002").Price);
    }

    [Fact]
    public async Task Importar_PrecioConPuntoDecimal_ParseaFormatoInvariante()
    {
        // Arrange: "1234.56" sin coma se parsea con cultura invariante (punto decimal)
        var (svc, _) = TestDbHelper.Create(nameof(Importar_PrecioConPuntoDecimal_ParseaFormatoInvariante));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "P-001";
            ws.Cell(2, 2).Value = "Pinza Universal";
            ws.Cell(2, 3).Value = "1234.56";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal(1234.56m, prod.Price);
    }

    [Fact]
    public async Task Importar_SkuExistente_ActualizaEnVezDeDuplicar()
    {
        // Arrange: producto preexistente con SKU "T-001"; el excel trae el mismo SKU
        // (en otra capitalización) con nombre y precio nuevos
        var (svc, _) = TestDbHelper.Create(nameof(Importar_SkuExistente_ActualizaEnVezDeDuplicar));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        var existente = new Producto
        {
            Name = "Taladro Viejo",
            SKU = "T-001",
            Price = 50000,
            Stock = 8,
            CategoryId = cat.Id,
            UnidadMedida = "u."
        };
        await svc.SaveProductoAsync(existente);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "t-001"; // dedup case-insensitive
            ws.Cell(2, 2).Value = "Taladro Nuevo";
            ws.Cell(2, 3).Value = "85000";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert: actualiza, no duplica
        Assert.True(result.Success);
        Assert.Equal(0, result.Data.Importados);
        Assert.Equal(1, result.Data.Actualizados);

        var productos = (await svc.GetProductosAsync()).ToList();
        var prod = Assert.Single(productos);
        Assert.Equal("Taladro Nuevo", prod.Name);
        Assert.Equal(85000m, prod.Price);
        // El stock existente no se pisa al actualizar por import
        Assert.Equal(8, prod.Stock);
    }

    [Fact]
    public async Task Importar_FilasInvalidas_ReportaErroresYSigueConLasValidas()
    {
        // Arrange: una fila con precio no numérico, otra con precio negativo y una válida
        var (svc, _) = TestDbHelper.Create(nameof(Importar_FilasInvalidas_ReportaErroresYSigueConLasValidas));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "X-001";
            ws.Cell(2, 2).Value = "Producto Roto";
            ws.Cell(2, 3).Value = "no es un precio";
            ws.Cell(3, 1).Value = "X-002";
            ws.Cell(3, 2).Value = "Precio Negativo";
            ws.Cell(3, 3).Value = "-50";
            ws.Cell(4, 1).Value = "X-003";
            ws.Cell(4, 2).Value = "Producto Válido";
            ws.Cell(4, 3).Value = "999";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert: las inválidas van a la colección de errores, la válida se importa igual
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        Assert.Equal(2, result.Data.Errores.Count);
        Assert.Contains(result.Data.Errores, e => e.StartsWith("Fila 2") && e.Contains("precio inválido"));
        Assert.Contains(result.Data.Errores, e => e.StartsWith("Fila 3"));

        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal("Producto Válido", prod.Name);
        Assert.Equal(999m, prod.Price);
    }

    [Fact]
    public async Task Importar_FilaConDatosPeroSinNombre_ReportaError()
    {
        // Una fila con SKU/precio pero sin nombre no debe perderse en silencio:
        // se reporta en la colección de errores y las demás filas se importan igual
        var (svc, _) = TestDbHelper.Create(nameof(Importar_FilaConDatosPeroSinNombre_ReportaError));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "V-001";
            ws.Cell(2, 2).Value = ""; // sin nombre pero con datos
            ws.Cell(2, 3).Value = "100";
            ws.Cell(3, 1).Value = "V-002";
            ws.Cell(3, 2).Value = "Con Nombre";
            ws.Cell(3, 3).Value = "200";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        var error = Assert.Single(result.Data.Errores);
        Assert.StartsWith("Fila 2", error);
        Assert.Contains("sin nombre", error);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal("Con Nombre", prod.Name);
    }

    [Fact]
    public async Task Importar_SkuVacio_GeneraSkuAutomaticoPorFila()
    {
        // Producto nuevo sin SKU -> "IMP-{fila:D4}" (si ese SKU está libre)
        var (svc, _) = TestDbHelper.Create(nameof(Importar_SkuVacio_GeneraSkuAutomaticoPorFila));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 2).Value = "Sin Sku"; // columna SKU vacía, fila 2
            ws.Cell(2, 3).Value = "150";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal("IMP-0002", prod.SKU);
    }

    [Fact]
    public async Task Importar_HojaVacia_RetornaOkSinImportarNada()
    {
        // Comportamiento real: hoja sin celdas usadas -> Ok con (0, 0, sin errores)
        var (svc, _) = TestDbHelper.Create(nameof(Importar_HojaVacia_RetornaOkSinImportarNada));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(_ => { /* hoja vacía */ });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.Data.Importados);
        Assert.Equal(0, result.Data.Actualizados);
        Assert.Empty(result.Data.Errores);
        Assert.Empty((await svc.GetProductosAsync()).ToList());
    }

    [Fact]
    public async Task Importar_StreamNoEsExcel_RetornaFail()
    {
        // Comportamiento real: archivo que no es .xlsx -> Result.Fail con mensaje
        var (svc, _) = TestDbHelper.Create(nameof(Importar_StreamNoEsExcel_RetornaFail));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("esto no es un excel"));

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Error al procesar el archivo", result.Message);
        Assert.Empty((await svc.GetProductosAsync()).ToList());
    }

    [Fact]
    public async Task Importar_EncabezadoConTilde_DetectaColumnas()
    {
        // "Código" con tilde debe detectarse igual que "Codigo" (normalización de acentos)
        var (svc, _) = TestDbHelper.Create(nameof(Importar_EncabezadoConTilde_DetectaColumnas));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "Producto";
            ws.Cell(1, 2).Value = "Código";
            ws.Cell(1, 3).Value = "Precio Venta";
            ws.Cell(2, 1).Value = "Martillo Galponero";
            ws.Cell(2, 2).Value = "M-100";
            ws.Cell(2, 3).Value = "9500";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal("Martillo Galponero", prod.Name);
        Assert.Equal("M-100", prod.SKU);
        Assert.Equal(9500m, prod.Price);
    }

    [Fact]
    public async Task Importar_PrecioFormatoUS_MilesConComaYPuntoDecimal_ParseaCorrecto()
    {
        // "1,234.56" (miles con coma + punto decimal, formato US) debe importarse
        // como 1234.56, no como 1.23
        var (svc, _) = TestDbHelper.Create(nameof(Importar_PrecioFormatoUS_MilesConComaYPuntoDecimal_ParseaCorrecto));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "U-001";
            ws.Cell(2, 2).Value = "Producto US";
            ws.Cell(2, 3).Value = "1,234.56";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal(1234.56m, prod.Price);
    }

    [Fact]
    public async Task Importar_SkuRepetidoEnElMismoArchivo_ActualizaEnVezDeDuplicar()
    {
        // Dos filas con el mismo SKU nuevo: la segunda actualiza a la primera,
        // no crea un producto duplicado
        var (svc, _) = TestDbHelper.Create(nameof(Importar_SkuRepetidoEnElMismoArchivo_ActualizaEnVezDeDuplicar));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "N-001";
            ws.Cell(2, 2).Value = "Primera Versión";
            ws.Cell(2, 3).Value = "100";
            ws.Cell(3, 1).Value = "N-001";
            ws.Cell(3, 2).Value = "Segunda Versión";
            ws.Cell(3, 3).Value = "200";
        });

        // Act
        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data.Importados);
        Assert.Equal(1, result.Data.Actualizados);
        var prod = Assert.Single((await svc.GetProductosAsync()).ToList());
        Assert.Equal("Segunda Versión", prod.Name);
        Assert.Equal(200m, prod.Price);
    }

    [Fact]
    public async Task Importar_ConColumnaStock_LosProductosNuevosEntranConEseStock()
    {
        var (svc, _) = TestDbHelper.Create(nameof(Importar_ConColumnaStock_LosProductosNuevosEntranConEseStock));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(1, 4).Value = "Stock";
            ws.Cell(2, 1).Value = "S-001";
            ws.Cell(2, 2).Value = "Destornillador";
            ws.Cell(2, 3).Value = "5000";
            ws.Cell(2, 4).Value = 12;
            ws.Cell(3, 1).Value = "S-002";
            ws.Cell(3, 2).Value = "Llave inglesa";
            ws.Cell(3, 3).Value = "9000";
            // stock vacío -> 0
        });

        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        Assert.True(result.Success, result.Message);
        Assert.Empty(result.Data.Errores);
        var productos = await svc.GetProductosAsync();
        Assert.Equal(12, productos.Single(p => p.SKU == "S-001").Stock);
        Assert.Equal(0, productos.Single(p => p.SKU == "S-002").Stock);
    }

    [Fact]
    public async Task Importar_StockInvalido_ReportaLaFila_Y_ImportaLasDemas()
    {
        var (svc, _) = TestDbHelper.Create(nameof(Importar_StockInvalido_ReportaLaFila_Y_ImportaLasDemas));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(1, 4).Value = "Cantidad";
            ws.Cell(2, 1).Value = "Q-001";
            ws.Cell(2, 2).Value = "Cinta";
            ws.Cell(2, 3).Value = "800";
            ws.Cell(2, 4).Value = "muchos";
            ws.Cell(3, 1).Value = "Q-002";
            ws.Cell(3, 2).Value = "Pegamento";
            ws.Cell(3, 3).Value = "1200";
            ws.Cell(3, 4).Value = 3;
        });

        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        Assert.True(result.Success, result.Message);
        Assert.Contains(result.Data.Errores, e => e.StartsWith("Fila 2"));
        Assert.Equal(3, Assert.Single(await svc.GetProductosAsync()).Stock);
    }

    /// <summary>Lista con columna SKU vacía: (nombre, precio) desde la fila 2.</summary>
    private static MemoryStream ListaSinSku(params (string Nombre, string Precio)[] filas) => CrearExcel(ws =>
    {
        ws.Cell(1, 1).Value = "SKU";
        ws.Cell(1, 2).Value = "Nombre";
        ws.Cell(1, 3).Value = "Precio";
        for (int i = 0; i < filas.Length; i++)
        {
            ws.Cell(i + 2, 2).Value = filas[i].Nombre;
            ws.Cell(i + 2, 3).Value = filas[i].Precio;
        }
    });

    [Fact]
    public async Task Importar_DosListasSinSku_ConProductosDistintos_NoSePisanEntreSi()
    {
        // Regresión: el SKU automático salía del número de fila (IMP-0002, IMP-0003...), así que
        // la fila 2 de una segunda lista "encontraba" al producto de la fila 2 de la primera
        // y le pisaba nombre y precio (quedándose con su stock e historial de ventas).
        var (svc, _) = TestDbHelper.Create(nameof(Importar_DosListasSinSku_ConProductosDistintos_NoSePisanEntreSi));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);

        using (var listaA = ListaSinSku(("Martillo", "100"), ("Pinza", "200")))
            await svc.ImportarProductosDesdeExcelAsync(listaA, cat.Id);
        using var listaB = ListaSinSku(("Serrucho", "300"), ("Nivel", "400"));
        var result = await svc.ImportarProductosDesdeExcelAsync(listaB, cat.Id);

        Assert.Equal(2, result.Data.Importados);
        Assert.Equal(0, result.Data.Actualizados);
        var productos = await svc.GetProductosAsync();
        Assert.Equal(new[] { "Martillo", "Nivel", "Pinza", "Serrucho" }, productos.Select(p => p.Name).OrderBy(n => n).ToArray());
        Assert.Equal(100m, productos.Single(p => p.Name == "Martillo").Price);
        Assert.Equal(4, productos.Select(p => p.SKU).Distinct().Count());
    }

    [Fact]
    public async Task Importar_ListaSinSkuReimportadaConOtroOrden_ActualizaCadaProductoPorNombre()
    {
        // Sin SKU, el producto se identifica por nombre (sin distinguir mayúsculas, tildes ni
        // espacios de más): reimportar la lista del proveedor actualiza precios, no duplica.
        var (svc, _) = TestDbHelper.Create(nameof(Importar_ListaSinSkuReimportadaConOtroOrden_ActualizaCadaProductoPorNombre));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        using (var original = ListaSinSku(("Martillo Galponero", "100")))
            await svc.ImportarProductosDesdeExcelAsync(original, cat.Id);
        var martilloId = (await svc.GetProductosAsync()).Single().Id;

        using var actualizada = ListaSinSku(("Pinza", "200"), ("  martillo  GALPONERO ", "150"));
        var result = await svc.ImportarProductosDesdeExcelAsync(actualizada, cat.Id);

        Assert.Equal(1, result.Data.Importados);
        Assert.Equal(1, result.Data.Actualizados);
        var martillo = (await svc.GetProductosAsync()).Single(p => p.Id == martilloId);
        Assert.Equal(150m, martillo.Price);
    }

    [Fact]
    public async Task Importar_SinSku_ConVariosProductosDelMismoNombre_ReportaErrorEnVezDeAdivinar()
    {
        var (svc, _) = TestDbHelper.Create(nameof(Importar_SinSku_ConVariosProductosDelMismoNombre_ReportaErrorEnVezDeAdivinar));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        await svc.SaveProductoAsync(new Producto { Name = "Tornillo", SKU = "TOR-3", Price = 10, CategoryId = cat.Id });
        await svc.SaveProductoAsync(new Producto { Name = "Tornillo", SKU = "TOR-5", Price = 20, CategoryId = cat.Id });

        using var lista = ListaSinSku(("Tornillo", "99"));
        var result = await svc.ImportarProductosDesdeExcelAsync(lista, cat.Id);

        Assert.Contains(result.Data.Errores, e => e.StartsWith("Fila 2") && e.Contains("SKU"));
        var productos = await svc.GetProductosAsync();
        Assert.Equal(2, productos.Count);
        Assert.DoesNotContain(productos, p => p.Price == 99m);
    }

    [Fact]
    public async Task Importar_SkuDeProductoEliminado_CreaElProductoSinHacerFallarElArchivo()
    {
        // Regresión: una sola fila con el SKU de un producto eliminado hacía fallar
        // el SaveChanges final y no se importaba ninguna fila del archivo.
        var (svc, _) = TestDbHelper.Create(nameof(Importar_SkuDeProductoEliminado_CreaElProductoSinHacerFallarElArchivo));
        var cat = CategoriaTest();
        await svc.SaveCategoriaAsync(cat);
        var eliminado = new Producto { Name = "Viejo", SKU = "X-9", Price = 10, CategoryId = cat.Id };
        await svc.SaveProductoAsync(eliminado);
        await svc.DeleteProductoAsync(eliminado.Id);

        using var stream = CrearExcel(ws =>
        {
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Nombre";
            ws.Cell(1, 3).Value = "Precio";
            ws.Cell(2, 1).Value = "OK-1";
            ws.Cell(2, 2).Value = "Producto válido";
            ws.Cell(2, 3).Value = "10";
            ws.Cell(3, 1).Value = "X-9";
            ws.Cell(3, 2).Value = "Reingresado";
            ws.Cell(3, 3).Value = "20";
        });

        var result = await svc.ImportarProductosDesdeExcelAsync(stream, cat.Id);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data.Importados);
        Assert.Equal(2, (await svc.GetProductosAsync()).Count);
    }
}
