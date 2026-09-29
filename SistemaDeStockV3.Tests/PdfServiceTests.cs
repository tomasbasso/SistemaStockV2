using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SistemaDeStockV3.Models;
using SistemaDeStockV3.Services;

namespace SistemaDeStockV3.Tests;

/// <summary>
/// Los tres documentos (presupuesto, remito, estado de cuenta) tienen que generarse siempre:
/// con logo, sin logo, con un logo roto o un color inválido guardado en la base.
/// </summary>
public class PdfServiceTests
{
    private readonly PdfService _pdf = new();

    private static readonly byte[] LogoPng = CrearLogoDePrueba();

    /// <summary>Un PNG real (un rectángulo naranja) generado con el propio QuestPDF.</summary>
    private static byte[] CrearLogoDePrueba()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document
            .Create(doc => doc.Page(p =>
            {
                p.Size(30, 20, Unit.Point);
                p.PageColor("#E8681A");
                p.Content().Text("");
            }))
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 72 })
            .First();
    }

    private static ConfiguracionApp Config(byte[]? logo = null, string color = "#E8681A") => new()
    {
        NombreNegocio = "Ferretería Caiquen",
        DireccionNegocio = "Winifreda, La Pampa",
        Telefono = "2302 000000",
        LogoNegocio = logo,
        ColorMarca = color
    };

    private static PresupuestoData Presupuesto(ConfiguracionApp config, int items = 3)
    {
        var data = new PresupuestoData
        {
            Config = config,
            Cliente = new Cliente { Name = "Juan Pérez", CUIT = "20-12345678-9", CondicionIva = CondicionIva.ResponsableInscripto },
            Presupuesto = new Presupuesto { NumeroPresupuesto = 7, FechaVencimiento = DateTime.Today.AddDays(15), Notas = "Entrega a coordinar." }
        };
        for (int i = 0; i < items; i++)
        {
            var id = Guid.NewGuid();
            data.NombreProductos[id] = $"Artículo {i + 1}";
            data.Detalles.Add(new PresupuestoDetalle { ProductoId = id, Quantity = 2, UnitPrice = 1500.50m });
        }
        data.Presupuesto.Total = data.Detalles.Sum(d => d.UnitPrice * d.Quantity);
        return data;
    }

    private static RemitoVentaData Remito(ConfiguracionApp config, Cliente? cliente)
    {
        var id = Guid.NewGuid();
        return new RemitoVentaData
        {
            Config = config,
            Cliente = cliente,
            Venta = new Venta { NumeroVenta = 12, Total = 2700m, IsFiado = cliente != null },
            NombreProductos = { [id] = "Martillo" },
            Detalles = { new VentaDetalle { ProductoId = id, Quantity = 2, UnitPrice = 1500m } } // con descuento de 300
        };
    }

    private static EstadoCuentaData EstadoCuenta(ConfiguracionApp config, bool conVentas) => new()
    {
        Config = config,
        Cliente = new Cliente { Name = "Juan Pérez" },
        CuentaCorriente = new CuentaCorriente { Balance = conVentas ? 3001m : 0m },
        VentasFiadas = conVentas
            ? new List<VentaFiadaDetalle> { new() { NumeroVenta = 1, Fecha = DateTime.Today, Total = 3001m, Items = { "2x Martillo ($ 1.500,50)" } } }
            : new List<VentaFiadaDetalle>()
    };

    private static void AssertEsPdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Presupuesto_ConYSinLogo_GeneraPdf()
    {
        AssertEsPdf(_pdf.GenerarPresupuesto(Presupuesto(Config(LogoPng))));
        AssertEsPdf(_pdf.GenerarPresupuesto(Presupuesto(Config())));
    }

    [Fact]
    public void Presupuesto_SinCliente_NiItems_GeneraPdf()
    {
        var data = Presupuesto(Config(), items: 0);
        data.Cliente = null;
        AssertEsPdf(_pdf.GenerarPresupuesto(data));
    }

    [Fact]
    public void Presupuesto_Largo_OcupaVariasPaginas()
    {
        var paginas = PdfService.DocumentoPresupuesto(Presupuesto(Config(LogoPng), items: 80))
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 20 })
            .Count();

        Assert.True(paginas > 1);
    }

    [Fact]
    public void Remito_ConClienteYDescuento_Y_SinCliente_GeneraPdf()
    {
        AssertEsPdf(_pdf.GenerarRemitoVenta(Remito(Config(LogoPng), new Cliente { Name = "Juan Pérez", Phone = "123" })));
        AssertEsPdf(_pdf.GenerarRemitoVenta(Remito(Config(), cliente: null)));
    }

    [Theory]
    [InlineData(true, 3001, true)]    // recién vendido: con saldo anterior
    [InlineData(true, 3001, false)]   // reimpresión: solo saldo actual
    [InlineData(true, -500, false)]   // saldo a favor
    [InlineData(true, null, true)]    // sin cuenta corriente cargada
    [InlineData(false, 3001, true)]   // contado: ignora el saldo
    public void RemitoFiado_ConSaldo_GeneraPdf(bool fiado, int? saldo, bool recienVendido)
    {
        var data = Remito(Config(LogoPng), new Cliente { Name = "Juan Pérez" });
        data.Venta.IsFiado = fiado;
        data.SaldoCuentaCorriente = saldo;
        data.SaldoRecienVendido = recienVendido;

        AssertEsPdf(_pdf.GenerarRemitoVenta(data));
    }

    [Fact]
    public void EstadoDeCuenta_ConVentas_Y_Vacio_GeneraPdf()
    {
        AssertEsPdf(_pdf.GenerarEstadoCuenta(EstadoCuenta(Config(LogoPng), conVentas: true)));
        AssertEsPdf(_pdf.GenerarEstadoCuenta(EstadoCuenta(Config(), conVentas: false)));
    }

    [Fact]
    public void LogoCorrupto_NoEsValido_Y_ElDocumentoSaleConElNombre()
    {
        var basura = new byte[] { 1, 2, 3, 4, 5 };

        Assert.False(PdfService.EsLogoValido(basura));
        Assert.False(PdfService.EsLogoValido(null));
        Assert.True(PdfService.EsLogoValido(LogoPng));
        AssertEsPdf(_pdf.GenerarPresupuesto(Presupuesto(Config(basura))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("naranja")]
    [InlineData("#12345")]
    public void ColorInvalido_UsaElPredeterminado(string color)
    {
        var estilo = new EstiloPdf(Config(color: color));

        Assert.Equal(ConfiguracionApp.ColorMarcaPredeterminado, estilo.Acento);
        AssertEsPdf(_pdf.GenerarPresupuesto(Presupuesto(Config(color: color))));
    }

    [Fact]
    public void ColorSinNumeral_SeNormaliza()
        => Assert.Equal("#E8681A", new EstiloPdf(Config(color: "e8681a")).Acento);

    [Fact]
    public void ColorDeMarcaClaro_SeOscureceParaLeerseComoTexto()
    {
        var amarillo = new EstiloPdf(Config(color: "#FFE600"));
        var naranja = new EstiloPdf(Config(color: "#E8681A"));

        Assert.Equal("#FFE600", amarillo.Acento);          // la franja conserva el color elegido
        Assert.NotEqual("#FFE600", amarillo.AcentoTexto);  // el número del documento se oscurece
        Assert.Equal("#E8681A", naranja.AcentoTexto);      // el naranja del logo ya se lee bien
    }

    [Fact]
    public void VistaPrevia_DevuelvePng()
    {
        var png = _pdf.GenerarVistaPreviaPresupuesto(Config(LogoPng));

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
    }
}
