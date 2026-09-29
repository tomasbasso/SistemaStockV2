using System.Globalization;
using System.Text.RegularExpressions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SistemaDeStockV3.Models;
// MAUI también define IContainer, Image e ImageFormat en sus usings globales.
using IContainer = QuestPDF.Infrastructure.IContainer;
using ImagenPdf = QuestPDF.Infrastructure.Image;
using FormatoImagen = QuestPDF.Infrastructure.ImageFormat;

namespace SistemaDeStockV3.Services
{
    /// <summary>
    /// Modelo con los datos necesarios para generar el estado de cuenta de un cliente.
    /// </summary>
    public class EstadoCuentaData
    {
        public Cliente Cliente { get; set; } = new();
        public CuentaCorriente CuentaCorriente { get; set; } = new();
        public ConfiguracionApp Config { get; set; } = new();
        public List<VentaFiadaDetalle> VentasFiadas { get; set; } = new();
        public DateTime FechaGeneracion { get; set; } = DateTime.Now;
    }

    public class VentaFiadaDetalle
    {
        public int NumeroVenta { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Total { get; set; }
        public List<string> Items { get; set; } = new();
    }

    public class PresupuestoData
    {
        public Presupuesto Presupuesto { get; set; } = new();
        public List<PresupuestoDetalle> Detalles { get; set; } = new();
        public Dictionary<Guid, string> NombreProductos { get; set; } = new();
        public Cliente? Cliente { get; set; }
        public ConfiguracionApp Config { get; set; } = new();
    }

    public class RemitoVentaData
    {
        public Venta Venta { get; set; } = new();
        public List<VentaDetalle> Detalles { get; set; } = new();
        public Dictionary<Guid, string> NombreProductos { get; set; } = new();
        public Cliente? Cliente { get; set; }
        public ConfiguracionApp Config { get; set; } = new();

        /// <summary>Saldo de la cuenta corriente del cliente al emitir el remito. Solo se usa en ventas fiadas.</summary>
        public decimal? SaldoCuentaCorriente { get; set; }

        /// <summary>
        /// True si el saldo es el de recién vendido (incluye esta venta y ninguna posterior): permite mostrar
        /// el saldo anterior. En una reimpresión el saldo ya tiene otros movimientos y solo se muestra el actual.
        /// </summary>
        public bool SaldoRecienVendido { get; set; }
    }

    /// <summary>
    /// Genera los documentos PDF del negocio (presupuesto, remito y estado de cuenta) con QuestPDF.
    /// Los tres comparten el mismo sistema visual: logo y color de marca de la configuración,
    /// tinta oscura neutra para la estructura, y el acento reservado para los detalles.
    /// </summary>
    public class PdfService
    {
        public PdfService()
        {
            // Configurar licencia comunitaria de QuestPDF (gratuita para uso no comercial / proyectos pequeños)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] GenerarPresupuesto(PresupuestoData data) => DocumentoPresupuesto(data).GeneratePdf();

        /// <summary>Genera el PDF del remito/comprobante de una venta individual.</summary>
        public byte[] GenerarRemitoVenta(RemitoVentaData data) => DocumentoRemito(data).GeneratePdf();

        /// <summary>Genera el PDF del estado de cuenta corriente de un cliente.</summary>
        public byte[] GenerarEstadoCuenta(EstadoCuentaData data) => DocumentoEstadoCuenta(data).GeneratePdf();

        /// <summary>
        /// Renderiza como PNG la primera página de un presupuesto de ejemplo con el logo y el color
        /// de la configuración, para previsualizar la identidad de los documentos sin guardar nada.
        /// </summary>
        public byte[] GenerarVistaPreviaPresupuesto(ConfiguracionApp config)
        {
            var hoy = DateTime.Today;
            var data = new PresupuestoData
            {
                Config = config,
                Cliente = new Cliente
                {
                    Name = "Cliente de ejemplo",
                    Phone = "2302 400000",
                    Address = "Av. San Martín 123",
                    CondicionIva = CondicionIva.ConsumidorFinal
                },
                Presupuesto = new Presupuesto
                {
                    NumeroPresupuesto = 1,
                    Date = hoy,
                    FechaVencimiento = hoy.AddDays(15),
                    Notas = "Así se ven las observaciones del presupuesto."
                }
            };

            var ejemplos = new (string Nombre, int Cantidad, decimal Precio)[]
            {
                ("Martillo carpintero 27 mm", 1, 18500m),
                ("Juego de destornilladores x6", 2, 12900m),
                ("Cinta métrica 5 m", 1, 7400m),
                ("Tornillos autoperforantes 8 x 1\" (caja x100)", 3, 5200m)
            };
            foreach (var (nombre, cantidad, precio) in ejemplos)
            {
                var id = Guid.NewGuid();
                data.NombreProductos[id] = nombre;
                data.Detalles.Add(new PresupuestoDetalle { ProductoId = id, Quantity = cantidad, UnitPrice = precio });
            }
            data.Presupuesto.Total = data.Detalles.Sum(d => d.UnitPrice * d.Quantity);

            return DocumentoPresupuesto(data)
                .GenerateImages(new ImageGenerationSettings { ImageFormat = FormatoImagen.Png, RasterDpi = 110 })
                .First();
        }

        /// <summary>Indica si los bytes son una imagen que QuestPDF puede dibujar como logo.</summary>
        public static bool EsLogoValido(byte[]? logo) => EstiloPdf.CargarLogo(logo) != null;

        // ──────────────────────────────────────────────────────────────────────────────────
        // DOCUMENTOS
        // ──────────────────────────────────────────────────────────────────────────────────

        internal static IDocument DocumentoPresupuesto(PresupuestoData data)
        {
            var e = new EstiloPdf(data.Config);
            var p = data.Presupuesto;
            var lineas = data.Detalles
                .Select(d => new LineaPdf(d.Quantity, NombreProducto(data.NombreProductos, d.ProductoId), d.UnitPrice))
                .ToList();

            var meta = new List<(string, string)> { ("Fecha", p.Date.ToString("dd/MM/yyyy")) };
            if (p.FechaVencimiento.HasValue)
            {
                var vence = p.FechaVencimiento.Value;
                var dias = (vence.Date - p.Date.Date).Days;
                meta.Add(("Válido hasta", dias > 0 ? $"{vence:dd/MM/yyyy} ({dias} días)" : $"{vence:dd/MM/yyyy}"));
            }

            return Document.Create(doc => doc.Page(page =>
            {
                ConfigurarPagina(page, e, PageSizes.A4, 42);

                page.Header().Element(c => Encabezado(c, e, "PRESUPUESTO", $"N° {p.NumeroPresupuesto:D6}", meta, compacto: false));

                page.Content().PaddingTop(18).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(14);
                        row.RelativeItem().Element(c => BloqueEmisor(c, e, compacto: false));
                        row.RelativeItem().Element(c => BloqueCliente(c, e, data.Cliente, "PRESUPUESTO PARA", compacto: false));
                    });

                    col.Item().PaddingTop(20).Element(c => TablaItems(c, lineas, compacto: false));

                    col.Item().PaddingTop(14).ShowEntire().Row(row =>
                    {
                        row.Spacing(24);
                        row.RelativeItem().Column(izq =>
                        {
                            izq.Spacing(12);
                            if (!string.IsNullOrWhiteSpace(p.Notas))
                                izq.Item().Element(c => BloqueNotas(c, e, "Observaciones", p.Notas));
                            izq.Item().Text("Los precios indicados no incluyen IVA salvo indicación expresa. Este presupuesto no constituye factura.")
                                .FontSize(7.5f).FontColor(EstiloPdf.Tenue).LineHeight(1.35f);
                        });
                        row.ConstantItem(215).Element(c => BloqueTotales(c, e, lineas, p.Total, compacto: false));
                    });
                });

                page.Footer().Element(c => PiePagina(c, e, "Documento no válido como factura", compacto: false));
            }))
            .WithMetadata(Metadatos(e, $"Presupuesto N° {p.NumeroPresupuesto:D6}"));
        }

        internal static IDocument DocumentoRemito(RemitoVentaData data)
        {
            var e = new EstiloPdf(data.Config);
            var v = data.Venta;
            var lineas = data.Detalles
                .Select(d => new LineaPdf(d.Quantity, NombreProducto(data.NombreProductos, d.ProductoId), d.UnitPrice))
                .ToList();

            var meta = new List<(string, string)>
            {
                ("Fecha", v.Date.ToString("dd/MM/yyyy HH:mm")),
                ("Condición", v.IsFiado ? "Cuenta corriente" : "Contado")
            };

            return Document.Create(doc => doc.Page(page =>
            {
                ConfigurarPagina(page, e, PageSizes.A5, 28);

                page.Header().Element(c => Encabezado(c, e, "REMITO", $"N° {v.NumeroVenta:D6}", meta, compacto: true));

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(10);
                        row.RelativeItem().Element(c => BloqueEmisor(c, e, compacto: true));
                        row.RelativeItem().Element(c => BloqueCliente(c, e, data.Cliente, "CLIENTE", compacto: true));
                    });

                    if (v.IsFiado)
                        col.Item().PaddingTop(10).Element(AvisoCuentaCorriente);

                    col.Item().PaddingTop(12).Element(c => TablaItems(c, lineas, compacto: true));

                    col.Item().PaddingTop(10).ShowEntire().Column(fin =>
                    {
                        fin.Item().AlignRight().Width(190).Element(c => BloqueTotales(c, e, lineas, v.Total, compacto: true));

                        if (v.IsFiado && data.SaldoCuentaCorriente.HasValue)
                            fin.Item().PaddingTop(8).AlignRight().Width(190).Element(c => SaldoCuenta(c, data.SaldoCuentaCorriente.Value, v.Total, data.SaldoRecienVendido));

                        fin.Item().PaddingTop(36).Row(r =>
                        {
                            r.Spacing(28);
                            r.RelativeItem().Element(c => LineaFirma(c, v.IsFiado ? "Firma del cliente (conforme)" : "Firma"));
                            r.RelativeItem().Element(c => LineaFirma(c, "Aclaración"));
                        });
                    });
                });

                page.Footer().Element(c => PiePagina(c, e, "No válido como factura", compacto: true));
            }))
            .WithMetadata(Metadatos(e, $"Remito N° {v.NumeroVenta:D6}"));
        }

        internal static IDocument DocumentoEstadoCuenta(EstadoCuentaData d)
        {
            var e = new EstiloPdf(d.Config);
            var balance = d.CuentaCorriente.Balance;
            var ventas = d.VentasFiadas.OrderByDescending(x => x.Fecha).ToList();
            var totalVentas = ventas.Sum(x => x.Total);
            var meta = new List<(string, string)> { ("Emitido", d.FechaGeneracion.ToString("dd/MM/yyyy HH:mm")) };

            var (colorSaldo, estadoSaldo) = balance > 0 ? (EstiloPdf.Deuda, "Deuda pendiente")
                                          : balance < 0 ? (EstiloPdf.AFavor, "Saldo a favor del cliente")
                                          : (EstiloPdf.Tinta, "Sin deuda");

            return Document.Create(doc => doc.Page(page =>
            {
                ConfigurarPagina(page, e, PageSizes.A4, 42);

                page.Header().Element(c => Encabezado(c, e, "ESTADO DE CUENTA", "Cuenta corriente", meta, compacto: false));

                page.Content().PaddingTop(18).Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.Spacing(14);
                        row.RelativeItem().Element(c => BloqueEmisor(c, e, compacto: false));
                        row.RelativeItem().Element(c => BloqueCliente(c, e, d.Cliente, "CLIENTE", compacto: false));
                    });

                    // ── Resumen ───────────────────────────────────────────────
                    col.Item().PaddingTop(14).Row(row =>
                    {
                        row.Spacing(10);
                        row.RelativeItem(1.3f).Element(c => Indicador(c, "SALDO ACTUAL",
                            Moneda(Math.Abs(balance)), estadoSaldo, colorSaldo, destacado: true));
                        row.RelativeItem().Element(c => Indicador(c, "COMPRAS A CUENTA",
                            ventas.Count.ToString(), $"Por {Moneda(totalVentas)}", EstiloPdf.Tinta));
                        row.RelativeItem().Element(c => Indicador(c, "ÚLTIMA COMPRA",
                            ventas.Count > 0 ? ventas[0].Fecha.ToString("dd/MM/yyyy") : "—",
                            ventas.Count > 0 ? $"Venta N° {ventas[0].NumeroVenta:D4}" : "Sin movimientos",
                            EstiloPdf.Tinta));
                    });

                    // ── Detalle ───────────────────────────────────────────────
                    col.Item().PaddingTop(24).Element(c => TituloSeccion(c, e, "Ventas en cuenta corriente"));

                    if (ventas.Count == 0)
                    {
                        col.Item().PaddingTop(10).Border(0.75f).BorderColor(EstiloPdf.Linea).PaddingVertical(22).AlignCenter()
                            .Text("No hay ventas en cuenta corriente registradas para este cliente.")
                            .FontSize(9).FontColor(EstiloPdf.Suave);
                    }
                    else
                    {
                        var columnas = new ColumnaPdf[]
                        {
                            new(62, "VENTA"),
                            new(70, "FECHA"),
                            new(null, "DETALLE"),
                            new(92, "IMPORTE", Alinear.Derecha)
                        };

                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(cols => DefinirColumnas(cols, columnas));
                            table.Header(h => EncabezadoTabla(h, columnas, compacto: false));

                            for (int i = 0; i < ventas.Count; i++)
                            {
                                var venta = ventas[i];
                                var alterna = i % 2 == 1;

                                table.Cell().Element(c => CeldaFila(c, alterna, false))
                                    .Text($"N° {venta.NumeroVenta:D4}").FontSize(8.5f).SemiBold().FontColor(EstiloPdf.Tinta);
                                table.Cell().Element(c => CeldaFila(c, alterna, false))
                                    .Text(venta.Fecha.ToString("dd/MM/yyyy")).FontSize(8.5f);
                                table.Cell().Element(c => CeldaFila(c, alterna, false)).Column(items =>
                                {
                                    items.Spacing(1.5f);
                                    foreach (var item in venta.Items)
                                        items.Item().Text(item).FontSize(8).FontColor(EstiloPdf.Texto);
                                });
                                table.Cell().Element(c => CeldaFila(c, alterna, false)).AlignRight()
                                    .Text(Moneda(venta.Total)).FontSize(8.5f).SemiBold().FontColor(EstiloPdf.Tinta);
                            }
                        });

                        col.Item().PaddingTop(10).ShowEntire().AlignRight().Width(260).Row(r =>
                        {
                            r.ConstantItem(4).Background(e.Acento);
                            r.RelativeItem().Background(EstiloPdf.Tinta).PaddingVertical(9).PaddingHorizontal(12).Row(rr =>
                            {
                                rr.RelativeItem().AlignMiddle().Text("TOTAL COMPRADO A CUENTA")
                                    .FontSize(7.5f).SemiBold().FontColor(EstiloPdf.Blanco).LetterSpacing(0.1f);
                                rr.AutoItem().AlignMiddle().Text(Moneda(totalVentas))
                                    .FontFamily(EstiloPdf.FuenteTitulos).FontSize(13).Bold().FontColor(EstiloPdf.Blanco);
                            });
                        });
                    }

                    col.Item().PaddingTop(22).Text(
                        "Este documento es un resumen informativo del estado de cuenta corriente. " +
                        "El saldo actual contempla los pagos registrados. No tiene validez como comprobante fiscal.")
                        .FontSize(7.5f).FontColor(EstiloPdf.Tenue).LineHeight(1.35f);
                });

                page.Footer().Element(c => PiePagina(c, e, "Resumen informativo", compacto: false));
            }))
            .WithMetadata(Metadatos(e, $"Estado de cuenta — {d.Cliente.Name}"));
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // PIEZAS COMPARTIDAS
        // ──────────────────────────────────────────────────────────────────────────────────

        private static void ConfigurarPagina(PageDescriptor page, EstiloPdf e, PageSize tamano, float margen)
        {
            page.Size(tamano);
            page.MarginHorizontal(margen);
            page.MarginTop(margen - 6);
            page.MarginBottom(margen * 0.7f);
            page.PageColor(EstiloPdf.Blanco);
            page.DefaultTextStyle(s => s.FontFamily(EstiloPdf.FuenteTexto).FontSize(9).FontColor(EstiloPdf.Texto));

            // Franja de color de marca a sangre en el borde superior.
            page.Background().Column(col => col.Item().Height(5).Background(e.Acento));
        }

        /// <summary>Logo (o nombre del negocio) a la izquierda; tipo de documento, número y datos clave a la derecha.</summary>
        private static void Encabezado(IContainer c, EstiloPdf e, string titulo, string numero,
            IReadOnlyList<(string Etiqueta, string Valor)> meta, bool compacto)
        {
            var tamMeta = compacto ? 7.5f : 8.5f;

            c.Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Element(x => Marca(x, e, compacto));

                    row.AutoItem().AlignMiddle().PaddingLeft(16).Column(t =>
                    {
                        t.Item().AlignRight().Text(titulo)
                            .FontFamily(EstiloPdf.FuenteTitulos).FontSize(compacto ? 16 : 22).Bold()
                            .FontColor(EstiloPdf.Tinta).LetterSpacing(0.05f);
                        t.Item().AlignRight().Text(numero)
                            .FontFamily(EstiloPdf.FuenteTitulos).FontSize(compacto ? 10 : 12).SemiBold()
                            .FontColor(e.AcentoTexto);

                        t.Item().PaddingTop(compacto ? 5 : 8).Column(m =>
                        {
                            m.Spacing(1.5f);
                            foreach (var (etiqueta, valor) in meta)
                            {
                                m.Item().AlignRight().Text(txt =>
                                {
                                    txt.Span(etiqueta + "   ").FontSize(tamMeta).FontColor(EstiloPdf.Suave);
                                    txt.Span(valor).FontSize(tamMeta).SemiBold().FontColor(EstiloPdf.Tinta);
                                });
                            }
                        });
                    });
                });

                col.Item().PaddingTop(compacto ? 10 : 14).LineHorizontal(0.75f).LineColor(EstiloPdf.Linea);
            });
        }

        private static void Marca(IContainer c, EstiloPdf e, bool compacto)
        {
            if (e.Logo != null)
            {
                c.Height(compacto ? 64 : 98).MaxWidth(compacto ? 170 : 250)
                 .AlignLeft().AlignMiddle().Image(e.Logo).FitArea()
                 .WithCompressionQuality(ImageCompressionQuality.Best);
                return;
            }

            c.Column(col =>
            {
                col.Item().Text(e.NombreNegocio)
                    .FontFamily(EstiloPdf.FuenteTitulos).FontSize(compacto ? 15 : 20).Bold().FontColor(EstiloPdf.Tinta);
                col.Item().PaddingTop(5).Width(compacto ? 24 : 32).Height(3).Background(e.Acento);
            });
        }

        private static void BloqueEmisor(IContainer c, EstiloPdf e, bool compacto)
        {
            var lineas = new List<string>();
            if (!string.IsNullOrWhiteSpace(e.Config.DireccionNegocio)) lineas.Add(e.Config.DireccionNegocio.Trim());
            if (!string.IsNullOrWhiteSpace(e.Config.Telefono)) lineas.Add($"Tel. {e.Config.Telefono.Trim()}");

            BloqueParte(c, e, "EMITIDO POR", e.NombreNegocio, lineas, compacto);
        }

        private static void BloqueCliente(IContainer c, EstiloPdf e, Cliente? cliente, string etiqueta, bool compacto)
        {
            if (cliente == null)
            {
                BloqueParte(c, e, etiqueta, "Consumidor final", Array.Empty<string>(), compacto);
                return;
            }

            var lineas = new List<string>();
            var fiscal = string.IsNullOrWhiteSpace(cliente.CUIT)
                ? EtiquetaCondicionIva(cliente.CondicionIva)
                : $"CUIT {cliente.CUIT.Trim()} · {EtiquetaCondicionIva(cliente.CondicionIva)}";
            lineas.Add(fiscal);
            if (!string.IsNullOrWhiteSpace(cliente.Address)) lineas.Add(cliente.Address.Trim());
            if (!string.IsNullOrWhiteSpace(cliente.Phone)) lineas.Add($"Tel. {cliente.Phone.Trim()}");
            if (!compacto && !string.IsNullOrWhiteSpace(cliente.Email)) lineas.Add(cliente.Email.Trim());

            BloqueParte(c, e, etiqueta, cliente.Name, lineas, compacto);
        }

        private static void BloqueParte(IContainer c, EstiloPdf e, string etiqueta, string nombre,
            IEnumerable<string> lineas, bool compacto)
        {
            c.BorderLeft(2.5f).BorderColor(e.Acento).Background(EstiloPdf.Fondo)
             .PaddingVertical(compacto ? 7 : 11).PaddingHorizontal(compacto ? 9 : 13)
             .Column(col =>
             {
                 col.Spacing(1.5f);
                 col.Item().Text(etiqueta)
                     .FontSize(compacto ? 6 : 7).SemiBold().FontColor(EstiloPdf.Suave).LetterSpacing(0.12f);
                 col.Item().PaddingTop(compacto ? 1 : 2).Text(nombre)
                     .FontSize(compacto ? 9.5f : 11.5f).Bold().FontColor(EstiloPdf.Tinta);
                 foreach (var linea in lineas)
                     col.Item().Text(linea).FontSize(compacto ? 7 : 8.5f).FontColor(EstiloPdf.Texto);
             });
        }

        private static void TablaItems(IContainer c, IReadOnlyList<LineaPdf> lineas, bool compacto)
        {
            var tam = compacto ? 8f : 9f;

            var columnas = new ColumnaPdf[]
            {
                new(compacto ? 34 : 46, "CANT.", Alinear.Centro),
                new(null, "DESCRIPCIÓN"),
                new(compacto ? 66 : 88, compacto ? "P. UNIT." : "P. UNITARIO", Alinear.Derecha),
                new(compacto ? 72 : 94, "IMPORTE", Alinear.Derecha)
            };

            c.Table(table =>
            {
                table.ColumnsDefinition(cols => DefinirColumnas(cols, columnas));
                table.Header(h => EncabezadoTabla(h, columnas, compacto));

                if (lineas.Count == 0)
                {
                    table.Cell().ColumnSpan(4).Element(x => CeldaFila(x, false, compacto)).AlignCenter()
                        .Text("Sin artículos").FontSize(tam).FontColor(EstiloPdf.Suave);
                    return;
                }

                for (int i = 0; i < lineas.Count; i++)
                {
                    var l = lineas[i];
                    var alterna = i % 2 == 1;

                    table.Cell().Element(x => CeldaFila(x, alterna, compacto)).AlignCenter()
                        .Text(l.Cantidad.ToString(CultureInfo.CurrentCulture)).FontSize(tam).SemiBold().FontColor(EstiloPdf.Tinta);
                    table.Cell().Element(x => CeldaFila(x, alterna, compacto))
                        .Text(l.Descripcion).FontSize(tam).FontColor(EstiloPdf.Texto);
                    table.Cell().Element(x => CeldaFila(x, alterna, compacto)).AlignRight()
                        .Text(Moneda(l.PrecioUnitario)).FontSize(tam).FontColor(EstiloPdf.Texto);
                    table.Cell().Element(x => CeldaFila(x, alterna, compacto)).AlignRight()
                        .Text(Moneda(l.Importe)).FontSize(tam).SemiBold().FontColor(EstiloPdf.Tinta);
                }
            });
        }

        private enum Alinear { Izquierda, Centro, Derecha }

        /// <summary>Columna de tabla: ancho fijo en puntos, o null para ocupar el espacio restante.</summary>
        private sealed record ColumnaPdf(float? Ancho, string Titulo, Alinear Alineacion = Alinear.Izquierda);

        private static void DefinirColumnas(TableColumnsDefinitionDescriptor cols, IEnumerable<ColumnaPdf> columnas)
        {
            foreach (var columna in columnas)
            {
                if (columna.Ancho.HasValue) cols.ConstantColumn(columna.Ancho.Value);
                else cols.RelativeColumn();
            }
        }

        /// <summary>
        /// El encabezado es una sola celda que abarca todas las columnas y replica sus anchos con un Row:
        /// con un fondo por celda, los visores dejan finas líneas blancas entre celdas contiguas.
        /// </summary>
        private static void EncabezadoTabla(TableCellDescriptor h, IReadOnlyList<ColumnaPdf> columnas, bool compacto)
        {
            h.Cell().ColumnSpan((uint)columnas.Count).Background(EstiloPdf.Tinta).Row(row =>
            {
                foreach (var columna in columnas)
                {
                    var celda = (columna.Ancho.HasValue ? row.ConstantItem(columna.Ancho.Value) : row.RelativeItem())
                        .PaddingVertical(compacto ? 5 : 6.5f).PaddingHorizontal(compacto ? 6 : 8);
                    celda = columna.Alineacion switch
                    {
                        Alinear.Centro => celda.AlignCenter(),
                        Alinear.Derecha => celda.AlignRight(),
                        _ => celda
                    };
                    celda.Text(columna.Titulo).FontSize(7).SemiBold().FontColor(EstiloPdf.Blanco).LetterSpacing(0.08f);
                }
            });
        }

        private static IContainer CeldaFila(IContainer c, bool alterna, bool compacto) =>
            c.Background(alterna ? EstiloPdf.FondoFila : EstiloPdf.Blanco)
             .BorderBottom(0.5f).BorderColor(EstiloPdf.Linea)
             .PaddingVertical(compacto ? 5 : 7).PaddingHorizontal(compacto ? 6 : 8);

        /// <summary>Subtotal y descuento (si el total cobrado es menor a la suma de los ítems) y la barra de total.</summary>
        private static void BloqueTotales(IContainer c, EstiloPdf e, IReadOnlyList<LineaPdf> lineas, decimal total, bool compacto)
        {
            var subtotal = lineas.Sum(l => l.Importe);
            var descuento = subtotal - total;
            var unidades = lineas.Sum(l => l.Cantidad);
            var tam = compacto ? 8f : 9f;

            c.Column(col =>
            {
                if (descuento > 0)
                {
                    col.Item().BorderBottom(0.5f).BorderColor(EstiloPdf.Linea).PaddingVertical(4).PaddingHorizontal(4).Row(r =>
                    {
                        r.RelativeItem().Text("Subtotal").FontSize(tam).FontColor(EstiloPdf.Suave);
                        r.AutoItem().Text(Moneda(subtotal)).FontSize(tam).FontColor(EstiloPdf.Texto);
                    });
                    col.Item().PaddingVertical(4).PaddingHorizontal(4).Row(r =>
                    {
                        r.RelativeItem().Text("Descuento").FontSize(tam).FontColor(EstiloPdf.Suave);
                        r.AutoItem().Text("- " + Moneda(descuento)).FontSize(tam).SemiBold().FontColor(EstiloPdf.AFavor);
                    });
                }

                col.Item().PaddingTop(4).Row(r =>
                {
                    r.ConstantItem(4).Background(e.Acento);
                    r.RelativeItem().Background(EstiloPdf.Tinta)
                     .PaddingVertical(compacto ? 7 : 10).PaddingHorizontal(compacto ? 10 : 12)
                     .Row(rr =>
                     {
                         rr.RelativeItem().AlignMiddle().Text("TOTAL")
                             .FontSize(compacto ? 7.5f : 8.5f).SemiBold().FontColor(EstiloPdf.Blanco).LetterSpacing(0.14f);
                         rr.AutoItem().AlignMiddle().Text(Moneda(total))
                             .FontFamily(EstiloPdf.FuenteTitulos).FontSize(compacto ? 13 : 16).Bold().FontColor(EstiloPdf.Blanco);
                     });
                });

                col.Item().PaddingTop(5).AlignRight()
                    .Text($"{lineas.Count} {(lineas.Count == 1 ? "artículo" : "artículos")} · {unidades} {(unidades == 1 ? "unidad" : "unidades")}")
                    .FontSize(compacto ? 6.5f : 7.5f).FontColor(EstiloPdf.Suave);
            });
        }

        private static void BloqueNotas(IContainer c, EstiloPdf e, string titulo, string texto)
        {
            c.Column(col =>
            {
                col.Item().Element(x => TituloSeccion(x, e, titulo));
                col.Item().PaddingTop(6).Background(EstiloPdf.Fondo).Padding(10)
                    .Text(texto.Trim()).FontSize(8.5f).FontColor(EstiloPdf.Texto).LineHeight(1.35f);
            });
        }

        private static void TituloSeccion(IContainer c, EstiloPdf e, string texto)
        {
            c.Row(r =>
            {
                r.AutoItem().AlignMiddle().Width(7).Height(7).Background(e.Acento);
                r.RelativeItem().AlignMiddle().PaddingLeft(7).Text(texto.ToUpperInvariant())
                    .FontSize(7.5f).SemiBold().FontColor(EstiloPdf.Tinta).LetterSpacing(0.1f);
            });
        }

        private static void Indicador(IContainer c, string etiqueta, string valor, string detalle, string colorValor, bool destacado = false)
        {
            c.Border(0.75f).BorderColor(EstiloPdf.Linea).Background(destacado ? EstiloPdf.Fondo : EstiloPdf.Blanco)
             .PaddingVertical(10).PaddingHorizontal(12)
             .Column(col =>
             {
                 col.Item().Text(etiqueta).FontSize(7).SemiBold().FontColor(EstiloPdf.Suave).LetterSpacing(0.12f);
                 col.Item().PaddingTop(3).Text(valor)
                     .FontFamily(EstiloPdf.FuenteTitulos).FontSize(destacado ? 17 : 14).Bold().FontColor(colorValor);
                 col.Item().Text(detalle).FontSize(7.5f)
                     .FontColor(colorValor == EstiloPdf.Tinta ? EstiloPdf.Suave : colorValor);
             });
        }

        /// <summary>Franja que deja claro que la venta no se cobró y se suma a la deuda del cliente.</summary>
        private static void AvisoCuentaCorriente(IContainer c)
        {
            c.BorderLeft(2.5f).BorderColor(EstiloPdf.Deuda).Background("#FEF3F2")
             .PaddingVertical(6).PaddingHorizontal(9)
             .Column(col =>
             {
                 col.Item().Text("VENTA A CUENTA CORRIENTE")
                     .FontSize(8).Bold().FontColor(EstiloPdf.Deuda).LetterSpacing(0.1f);
                 col.Item().Text("El importe de este remito queda pendiente de pago y se suma al saldo del cliente.")
                     .FontSize(7).FontColor(EstiloPdf.Texto);
             });
        }

        /// <summary>Resumen de la cuenta corriente: cuánto debía antes de esta venta y cuánto debe ahora.</summary>
        private static void SaldoCuenta(IContainer c, decimal saldo, decimal totalVenta, bool desglosar)
        {
            var anterior = saldo - totalVenta;
            var color = saldo > 0 ? EstiloPdf.Deuda : saldo < 0 ? EstiloPdf.AFavor : EstiloPdf.Tinta;

            c.Border(0.75f).BorderColor(EstiloPdf.Linea).Padding(7).Column(col =>
            {
                col.Item().Text("CUENTA CORRIENTE").FontSize(6).SemiBold().FontColor(EstiloPdf.Suave).LetterSpacing(0.12f);
                if (desglosar)
                {
                    col.Item().PaddingTop(3).Row(r =>
                    {
                        r.RelativeItem().Text("Saldo anterior").FontSize(7.5f).FontColor(EstiloPdf.Suave);
                        r.AutoItem().Text(Moneda(anterior)).FontSize(7.5f).FontColor(EstiloPdf.Texto);
                    });
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("Esta venta").FontSize(7.5f).FontColor(EstiloPdf.Suave);
                        r.AutoItem().Text("+ " + Moneda(totalVenta)).FontSize(7.5f).FontColor(EstiloPdf.Texto);
                    });
                }
                col.Item().PaddingTop(3).BorderTop(desglosar ? 0.5f : 0).BorderColor(EstiloPdf.Linea).PaddingTop(desglosar ? 3 : 0).Row(r =>
                {
                    r.RelativeItem().Text(saldo < 0 ? "Saldo a favor" : desglosar ? "Saldo actual" : $"Saldo al {DateTime.Now:dd/MM/yyyy}").FontSize(8).SemiBold().FontColor(EstiloPdf.Tinta);
                    r.AutoItem().Text(Moneda(Math.Abs(saldo))).FontSize(9).Bold().FontColor(color);
                });
            });
        }

        private static void LineaFirma(IContainer c, string etiqueta)
        {
            c.Column(col =>
            {
                col.Item().LineHorizontal(0.75f).LineColor(EstiloPdf.Tenue);
                col.Item().PaddingTop(3).AlignCenter().Text(etiqueta).FontSize(7).FontColor(EstiloPdf.Suave);
            });
        }

        private static void PiePagina(IContainer c, EstiloPdf e, string leyenda, bool compacto)
        {
            var datos = new[]
            {
                e.NombreNegocio,
                e.Config.DireccionNegocio?.Trim(),
                string.IsNullOrWhiteSpace(e.Config.Telefono) ? null : $"Tel. {e.Config.Telefono.Trim()}"
            }.Where(s => !string.IsNullOrWhiteSpace(s));
            var tam = compacto ? 6.5f : 7f;

            c.Column(col =>
            {
                col.Item().LineHorizontal(0.5f).LineColor(EstiloPdf.Linea);
                col.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text(string.Join("  ·  ", datos)).FontSize(tam).FontColor(EstiloPdf.Suave);
                    row.AutoItem().PaddingLeft(12).Text(t =>
                    {
                        t.Span(leyenda + "  ·  Pág. ").FontSize(tam).FontColor(EstiloPdf.Tenue);
                        t.CurrentPageNumber().FontSize(tam).FontColor(EstiloPdf.Tenue);
                        t.Span(" de ").FontSize(tam).FontColor(EstiloPdf.Tenue);
                        t.TotalPages().FontSize(tam).FontColor(EstiloPdf.Tenue);
                    });
                });
            });
        }

        private static DocumentMetadata Metadatos(EstiloPdf e, string titulo) => new()
        {
            Title = $"{titulo} — {e.NombreNegocio}",
            Author = e.NombreNegocio,
            Creator = e.NombreNegocio
        };

        // ──────────────────────────────────────────────────────────────────────────────────
        // UTILIDADES
        // ──────────────────────────────────────────────────────────────────────────────────

        private static string Moneda(decimal valor) => valor.ToString("C", CultureInfo.CurrentCulture);

        private static string NombreProducto(Dictionary<Guid, string> nombres, Guid id) =>
            nombres.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Producto";

        private static string EtiquetaCondicionIva(CondicionIva condicion) => condicion switch
        {
            CondicionIva.ResponsableInscripto => "Responsable Inscripto",
            CondicionIva.Monotributista => "Monotributista",
            CondicionIva.MonotributoSocial => "Monotributo Social",
            CondicionIva.Exento => "Exento",
            CondicionIva.NoResponsable => "No Responsable",
            CondicionIva.SujetoNoCategorizado => "Sujeto no categorizado",
            _ => "Consumidor Final"
        };

        private sealed record LineaPdf(int Cantidad, string Descripcion, decimal PrecioUnitario)
        {
            public decimal Importe => Cantidad * PrecioUnitario;
        }
    }

    /// <summary>
    /// Paleta y recursos compartidos por todos los documentos. La estructura usa una tinta
    /// neutra que combina con cualquier logo; el color de marca solo aparece en acentos.
    /// </summary>
    internal sealed class EstiloPdf
    {
        public const string Blanco = "#FFFFFF";
        public const string Tinta = "#1F2429";
        public const string Texto = "#3B424A";
        public const string Suave = "#6B7280";
        public const string Tenue = "#9CA3AF";
        public const string Linea = "#E3E6EA";
        public const string Fondo = "#F4F5F7";
        public const string FondoFila = "#FAFBFC";
        public const string Deuda = "#B42318";
        public const string AFavor = "#067647";

        public static readonly string[] FuenteTexto = { "Segoe UI", "Arial" };
        public static readonly string[] FuenteTitulos = { "Bahnschrift", "Segoe UI", "Arial" };

        public ConfiguracionApp Config { get; }
        public ImagenPdf? Logo { get; }

        /// <summary>Color de marca tal cual lo eligió el usuario: franjas y barras.</summary>
        public string Acento { get; }

        /// <summary>Color de marca oscurecido si hace falta para que se lea como texto sobre blanco.</summary>
        public string AcentoTexto { get; }

        public string NombreNegocio =>
            string.IsNullOrWhiteSpace(Config.NombreNegocio) ? "Mi Negocio" : Config.NombreNegocio.Trim();

        public EstiloPdf(ConfiguracionApp config)
        {
            Config = config;
            Logo = CargarLogo(config.LogoNegocio);
            Acento = NormalizarColor(config.ColorMarca) ?? ConfiguracionApp.ColorMarcaPredeterminado;
            AcentoTexto = LegibleSobreBlanco(Acento);
        }

        public static ImagenPdf? CargarLogo(byte[]? bytes)
        {
            if (bytes is not { Length: > 0 }) return null;
            try
            {
                return ImagenPdf.FromBinaryData(bytes);
            }
            catch
            {
                return null; // Imagen corrupta o formato no soportado: el documento sale con el nombre como texto.
            }
        }

        public static string? NormalizarColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var h = hex.Trim();
            if (!h.StartsWith('#')) h = "#" + h;
            return Regex.IsMatch(h, "^#[0-9A-Fa-f]{6}$") ? h.ToUpperInvariant() : null;
        }

        /// <summary>Oscurece el color hasta alcanzar contraste 3:1 contra blanco (texto grande/negrita, WCAG).</summary>
        public static string LegibleSobreBlanco(string hex)
        {
            var (r, g, b) = Rgb(hex);
            for (int i = 0; i < 12 && Contraste(r, g, b) < 3.0; i++)
            {
                r *= 0.88; g *= 0.88; b *= 0.88;
            }
            return $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
        }

        private static (double R, double G, double B) Rgb(string hex) => (
            Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0);

        private static double Contraste(double r, double g, double b)
        {
            static double Lineal(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            var luminancia = 0.2126 * Lineal(r) + 0.7152 * Lineal(g) + 0.0722 * Lineal(b);
            return 1.05 / (luminancia + 0.05);
        }
    }
}
