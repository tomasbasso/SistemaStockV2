using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SistemaDeStockV3.Models;

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
    }

    /// <summary>
    /// Genera documentos PDF de estados de cuenta de clientes usando QuestPDF.
    /// </summary>
    public class PdfService
    {
        public PdfService()
        {
            // Configurar licencia comunitaria de QuestPDF (gratuita para uso no comercial / proyectos pequeños)
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>
        /// Genera el PDF del estado de cuenta de un cliente.
        /// Retorna los bytes del PDF para ser guardados en disco.
        /// </summary>
        public byte[] GenerarEstadoCuenta(EstadoCuentaData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Element(ComposeHeader);
                    page.Content().Element(c => ComposeContent(c, data));
                    page.Footer().Element(ComposeFooter);

                    void ComposeHeader(QuestPDF.Infrastructure.IContainer header)
                    {
                        header.Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                // Left: Store name
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text(data.Config.NombreNegocio)
                                        .FontSize(22).Bold().FontColor("#1e293b");
                                    if (!string.IsNullOrEmpty(data.Config.DireccionNegocio))
                                        c.Item().Text(data.Config.DireccionNegocio).FontSize(9).FontColor("#64748b");
                                    if (!string.IsNullOrEmpty(data.Config.Telefono))
                                        c.Item().Text($"Tel: {data.Config.Telefono}").FontSize(9).FontColor("#64748b");
                                });

                                // Right: Document title
                                row.ConstantItem(180).Column(c =>
                                {
                                    c.Item().Background("#1e40af").Padding(12).Column(inner =>
                                    {
                                        inner.Item().Text("ESTADO DE CUENTA")
                                            .FontSize(14).Bold().FontColor("#ffffff").AlignCenter();
                                        inner.Item().Text($"C/C — {data.FechaGeneracion:dd/MM/yyyy}")
                                            .FontSize(9).FontColor("#bfdbfe").AlignCenter();
                                    });
                                });
                            });

                            col.Item().PaddingTop(12).LineHorizontal(1.5f).LineColor("#1e40af");
                        });
                    }

                    void ComposeContent(QuestPDF.Infrastructure.IContainer content, EstadoCuentaData d)
                    {
                        content.PaddingTop(16).Column(col =>
                        {
                            // ── Cliente Info ──────────────────────────
                            col.Item().Background("#f1f5f9").Padding(14).Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("CLIENTE").FontSize(8).FontColor("#64748b").Bold();
                                    c.Item().Text(d.Cliente.Name).FontSize(14).Bold().FontColor("#1e293b");
                                    if (!string.IsNullOrEmpty(d.Cliente.Phone))
                                        c.Item().Text($"Tel: {d.Cliente.Phone}").FontSize(9).FontColor("#475569");
                                    if (!string.IsNullOrEmpty(d.Cliente.Address))
                                        c.Item().Text($"Dir: {d.Cliente.Address}").FontSize(9).FontColor("#475569");
                                    if (!string.IsNullOrEmpty(d.Cliente.CUIT))
                                        c.Item().Text($"CUIT: {d.Cliente.CUIT}").FontSize(9).FontColor("#475569");
                                    if (!string.IsNullOrEmpty(d.Cliente.Email))
                                        c.Item().Text($"Email: {d.Cliente.Email}").FontSize(9).FontColor("#475569");
                                });

                                row.ConstantItem(160).Column(c =>
                                {
                                    c.Item().Text("DEUDA TOTAL").FontSize(8).FontColor("#64748b").Bold().AlignRight();
                                    var balanceColor = d.CuentaCorriente.Balance > 0 ? "#dc2626" : "#16a34a";
                                    c.Item().Text($"{d.CuentaCorriente.Balance:C}")
                                        .FontSize(20).Bold().FontColor(balanceColor).AlignRight();
                                    var statusText = d.CuentaCorriente.Balance > 0 ? "DEUDA PENDIENTE"
                                                   : d.CuentaCorriente.Balance < 0 ? "A FAVOR"
                                                   : "SIN DEUDA";
                                    c.Item().Text(statusText).FontSize(8).FontColor(balanceColor).AlignRight();
                                });
                            });

                            col.Item().PaddingTop(20).Text("HISTORIAL DE VENTAS EN CUENTA CORRIENTE")
                                .FontSize(10).Bold().FontColor("#1e40af");

                            col.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor("#cbd5e1");

                            if (!d.VentasFiadas.Any())
                            {
                                col.Item().PaddingTop(16).AlignCenter()
                                    .Text("No hay ventas en cuenta corriente registradas para este cliente.")
                                    .FontColor("#94a3b8").Italic();
                            }
                            else
                            {
                                // Table header
                                col.Item().PaddingTop(8).Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.ConstantColumn(60);   // Nro
                                        cols.ConstantColumn(90);   // Fecha
                                        cols.RelativeColumn();     // Detalle
                                        cols.ConstantColumn(90);   // Total
                                    });

                                    // Header
                                    table.Header(header =>
                                    {
                                        static void HeaderCell(QuestPDF.Infrastructure.IContainer c, string text) =>
                                            c.Background("#1e40af").Padding(8)
                                             .Text(text).Bold().FontColor("#ffffff").FontSize(9);

                                        header.Cell().Element(c => HeaderCell(c, "VENTA #"));
                                        header.Cell().Element(c => HeaderCell(c, "FECHA"));
                                        header.Cell().Element(c => HeaderCell(c, "DETALLE"));
                                        header.Cell().Element(c => HeaderCell(c, "MONTO"));
                                    });

                                    // Rows
                                    bool alternate = false;
                                    foreach (var v in d.VentasFiadas.OrderByDescending(x => x.Fecha))
                                    {
                                        var bg = alternate ? "#f8fafc" : "#ffffff";
                                        alternate = !alternate;

                                        static QuestPDF.Infrastructure.IContainer CellStyle(QuestPDF.Infrastructure.IContainer c, string bg) =>
                                            c.Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(7);

                                        table.Cell().Element(c => CellStyle(c, bg))
                                            .Text($"#{v.NumeroVenta:D4}").FontSize(9).FontColor("#334155");

                                        table.Cell().Element(c => CellStyle(c, bg))
                                            .Text(v.Fecha.ToString("dd/MM/yyyy")).FontSize(9).FontColor("#334155");

                                        table.Cell().Element(c => CellStyle(c, bg)).Column(itemCol =>
                                        {
                                            foreach (var item in v.Items)
                                                itemCol.Item().Text($"• {item}").FontSize(8.5f).FontColor("#475569");
                                        });

                                        table.Cell().Element(c => CellStyle(c, bg)).AlignRight()
                                            .Text($"{v.Total:C}").FontSize(9).Bold().FontColor("#1e293b");
                                    }
                                });

                                // Totals row
                                col.Item().PaddingTop(4).Background("#1e293b").Padding(10).Row(row =>
                                {
                                    row.RelativeItem().Text("TOTAL DEUDA EN CUENTA CORRIENTE")
                                        .FontSize(10).Bold().FontColor("#ffffff");
                                    row.ConstantItem(90).AlignRight()
                                        .Text($"{d.VentasFiadas.Sum(v => v.Total):C}")
                                        .FontSize(11).Bold().FontColor("#fbbf24");
                                });
                            }

                            // Nota al pie
                            col.Item().PaddingTop(24).Text(
                                "Este documento es un resumen informativo del estado de cuenta corriente. " +
                                "No tiene validez como comprobante fiscal.")
                                .FontSize(8).FontColor("#94a3b8").Italic();
                        });
                    }

                    void ComposeFooter(QuestPDF.Infrastructure.IContainer footer)
                    {
                        footer.BorderTop(0.5f).BorderColor("#cbd5e1").PaddingTop(8).Row(row =>
                        {
                            row.RelativeItem().Text($"Generado el {data.FechaGeneracion:dd/MM/yyyy HH:mm}")
                                .FontSize(8).FontColor("#94a3b8");
                            row.RelativeItem().AlignRight()
                                .Text(x =>
                                {
                                    x.Span("Pág. ").FontSize(8).FontColor("#94a3b8");
                                    x.CurrentPageNumber().FontSize(8).FontColor("#94a3b8");
                                    x.Span(" de ").FontSize(8).FontColor("#94a3b8");
                                    x.TotalPages().FontSize(8).FontColor("#94a3b8");
                                });
                        });
                    }
                });
            }).GeneratePdf();
        }

        /// <summary>
        /// Genera el PDF del remito/comprobante de una venta individual.
        /// </summary>
        public byte[] GenerarRemitoVenta(RemitoVentaData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A5);
                    page.Margin(30);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(data.Config.NombreNegocio).FontSize(18).Bold().FontColor("#1e293b");
                                if (!string.IsNullOrEmpty(data.Config.DireccionNegocio))
                                    c.Item().Text(data.Config.DireccionNegocio).FontSize(8).FontColor("#64748b");
                                if (!string.IsNullOrEmpty(data.Config.Telefono))
                                    c.Item().Text($"Tel: {data.Config.Telefono}").FontSize(8).FontColor("#64748b");
                            });
                            row.ConstantItem(130).Column(c =>
                            {
                                c.Item().Background("#1e40af").Padding(10).Column(inner =>
                                {
                                    inner.Item().Text("REMITO").FontSize(13).Bold().FontColor("#ffffff").AlignCenter();
                                    inner.Item().Text($"N° {data.Venta.NumeroVenta:D6}").FontSize(10).FontColor("#bfdbfe").AlignCenter();
                                    inner.Item().Text(data.Venta.Date.ToString("dd/MM/yyyy")).FontSize(8).FontColor("#bfdbfe").AlignCenter();
                                });
                            });
                        });
                        col.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#1e40af");
                    });

                    page.Content().PaddingTop(12).Column(col =>
                    {
                        if (data.Cliente != null)
                        {
                            col.Item().Background("#f1f5f9").Padding(10).Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("CLIENTE").FontSize(7).FontColor("#64748b").Bold();
                                    c.Item().Text(data.Cliente.Name).FontSize(12).Bold().FontColor("#1e293b");
                                    if (!string.IsNullOrEmpty(data.Cliente.Phone))
                                        c.Item().Text($"Tel: {data.Cliente.Phone}").FontSize(8).FontColor("#475569");
                                    if (!string.IsNullOrEmpty(data.Cliente.Address))
                                        c.Item().Text($"Dir: {data.Cliente.Address}").FontSize(8).FontColor("#475569");
                                    if (!string.IsNullOrEmpty(data.Cliente.CUIT))
                                        c.Item().Text($"CUIT: {data.Cliente.CUIT}").FontSize(8).FontColor("#475569");
                                });
                                if (data.Venta.IsFiado)
                                    row.ConstantItem(80).AlignRight().AlignMiddle()
                                        .Text("FIADO").FontSize(9).Bold().FontColor("#dc2626");
                            });
                            col.Item().PaddingTop(10);
                        }

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(35);
                                cols.RelativeColumn();
                                cols.ConstantColumn(70);
                                cols.ConstantColumn(75);
                            });

                            table.Header(header =>
                            {
                                static void H(QuestPDF.Infrastructure.IContainer c, string t, bool right = false)
                                {
                                    var cell = c.Background("#1e40af").Padding(6);
                                    if (right) cell.AlignRight().Text(t).Bold().FontColor("#ffffff").FontSize(8);
                                    else cell.Text(t).Bold().FontColor("#ffffff").FontSize(8);
                                }
                                header.Cell().Element(c => H(c, "CANT"));
                                header.Cell().Element(c => H(c, "DESCRIPCIÓN"));
                                header.Cell().Element(c => H(c, "P.UNIT", true));
                                header.Cell().Element(c => H(c, "SUBTOTAL", true));
                            });

                            bool alt = false;
                            foreach (var d in data.Detalles)
                            {
                                var bg = alt ? "#f8fafc" : "#ffffff";
                                alt = !alt;
                                var nombre = data.NombreProductos.TryGetValue(d.ProductoId, out var n) ? n : "Producto";

                                static QuestPDF.Infrastructure.IContainer Cell(QuestPDF.Infrastructure.IContainer c, string bgColor)
                                    => c.Background(bgColor).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6);

                                table.Cell().Element(c => Cell(c, bg)).Text($"{d.Quantity}").FontSize(9);
                                table.Cell().Element(c => Cell(c, bg)).Text(nombre).FontSize(9).FontColor("#334155");
                                table.Cell().Element(c => Cell(c, bg)).AlignRight().Text($"{d.UnitPrice:C}").FontSize(9);
                                table.Cell().Element(c => Cell(c, bg)).AlignRight().Text($"{d.UnitPrice * d.Quantity:C}").FontSize(9).Bold();
                            }
                        });

                        // Mostrar descuento si el total cobrado difiere del subtotal de los ítems
                        var subtotalItems = data.Detalles.Sum(d => d.UnitPrice * d.Quantity);
                        var descuento = subtotalItems - data.Venta.Total;
                        if (descuento > 0)
                        {
                            col.Item().PaddingTop(2).Background("#f1f5f9").Padding(8).Row(row =>
                            {
                                row.RelativeItem().Text("Subtotal").FontSize(9).FontColor("#334155");
                                row.ConstantItem(90).AlignRight().Text($"{subtotalItems:C}").FontSize(9).FontColor("#334155");
                            });
                            col.Item().Background("#fef9c3").Padding(8).Row(row =>
                            {
                                row.RelativeItem().Text("Descuento").FontSize(9).FontColor("#854d0e");
                                row.ConstantItem(90).AlignRight().Text($"-{descuento:C}").FontSize(9).Bold().FontColor("#854d0e");
                            });
                        }

                        col.Item().PaddingTop(4).Background("#1e293b").Padding(10).Row(row =>
                        {
                            row.RelativeItem().Text(data.Venta.IsFiado ? "TOTAL  (Cuenta Corriente)" : "TOTAL  (Contado)").FontSize(10).Bold().FontColor("#ffffff");
                            row.ConstantItem(90).AlignRight().Text($"{data.Venta.Total:C}").FontSize(12).Bold().FontColor("#fbbf24");
                        });

                        col.Item().PaddingTop(20).Text("Firma: ___________________________").FontSize(9).FontColor("#94a3b8");
                        col.Item().PaddingTop(4).Text("Aclaración: ___________________________").FontSize(9).FontColor("#94a3b8");
                    });

                    page.Footer().BorderTop(0.5f).BorderColor("#cbd5e1").PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(7).FontColor("#94a3b8");
                        row.RelativeItem().AlignRight().Text("No válido como comprobante fiscal").FontSize(7).FontColor("#94a3b8").Italic();
                    });
                });
            }).GeneratePdf();
        }

        public byte[] GenerarPresupuesto(PresupuestoData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(data.Config.NombreNegocio).FontSize(22).Bold().FontColor("#1e293b");
                                if (!string.IsNullOrEmpty(data.Config.DireccionNegocio))
                                    c.Item().Text(data.Config.DireccionNegocio).FontSize(9).FontColor("#64748b");
                                if (!string.IsNullOrEmpty(data.Config.Telefono))
                                    c.Item().Text($"Tel: {data.Config.Telefono}").FontSize(9).FontColor("#64748b");
                            });
                            row.ConstantItem(180).Column(c =>
                            {
                                c.Item().Background("#0f766e").Padding(14).Column(inner =>
                                {
                                    inner.Item().Text("PRESUPUESTO").FontSize(15).Bold().FontColor("#ffffff").AlignCenter();
                                    inner.Item().Text($"N° {data.Presupuesto.NumeroPresupuesto:D6}").FontSize(10).FontColor("#ccfbf1").AlignCenter();
                                    inner.Item().Text(data.Presupuesto.Date.ToString("dd/MM/yyyy")).FontSize(8).FontColor("#ccfbf1").AlignCenter();
                                });
                            });
                        });
                        col.Item().PaddingTop(10).LineHorizontal(1.5f).LineColor("#0f766e");
                    });

                    page.Content().PaddingTop(16).Column(col =>
                    {
                        // Validez
                        if (data.Presupuesto.FechaVencimiento.HasValue)
                        {
                            col.Item().Background("#f0fdf4").Border(0.5f).BorderColor("#86efac").Padding(8).Row(row =>
                            {
                                row.AutoItem().Text("✓ ").FontSize(9).FontColor("#16a34a").Bold();
                                row.RelativeItem().Text($"Válido hasta el {data.Presupuesto.FechaVencimiento.Value:dd/MM/yyyy}").FontSize(9).FontColor("#15803d").Bold();
                            });
                            col.Item().PaddingTop(10);
                        }

                        // Cliente
                        if (data.Cliente != null)
                        {
                            col.Item().Background("#f1f5f9").Padding(12).Row(row =>
                            {
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Text("DESTINATARIO").FontSize(7).FontColor("#64748b").Bold();
                                    c.Item().Text(data.Cliente.Name).FontSize(13).Bold().FontColor("#1e293b");
                                    if (!string.IsNullOrEmpty(data.Cliente.Phone))
                                        c.Item().Text($"Tel: {data.Cliente.Phone}").FontSize(8).FontColor("#475569");
                                    if (!string.IsNullOrEmpty(data.Cliente.Address))
                                        c.Item().Text($"Dir: {data.Cliente.Address}").FontSize(8).FontColor("#475569");
                                });
                            });
                            col.Item().PaddingTop(14);
                        }

                        // Tabla de items
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(40);
                                cols.RelativeColumn();
                                cols.ConstantColumn(80);
                                cols.ConstantColumn(85);
                            });

                            table.Header(header =>
                            {
                                static void H(QuestPDF.Infrastructure.IContainer c, string t, bool right = false)
                                {
                                    var cell = c.Background("#0f766e").Padding(8);
                                    if (right) cell.AlignRight().Text(t).Bold().FontColor("#ffffff").FontSize(9);
                                    else cell.Text(t).Bold().FontColor("#ffffff").FontSize(9);
                                }
                                header.Cell().Element(c => H(c, "CANT"));
                                header.Cell().Element(c => H(c, "DESCRIPCIÓN"));
                                header.Cell().Element(c => H(c, "P. UNIT", true));
                                header.Cell().Element(c => H(c, "SUBTOTAL", true));
                            });

                            bool alt = false;
                            foreach (var d in data.Detalles)
                            {
                                var bg = alt ? "#f8fafc" : "#ffffff";
                                alt = !alt;
                                var nombre = data.NombreProductos.TryGetValue(d.ProductoId, out var n) ? n : "Producto";

                                static QuestPDF.Infrastructure.IContainer Cell(QuestPDF.Infrastructure.IContainer c, string bgColor)
                                    => c.Background(bgColor).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(8);

                                table.Cell().Element(c => Cell(c, bg)).Text($"{d.Quantity}").FontSize(9);
                                table.Cell().Element(c => Cell(c, bg)).Text(nombre).FontSize(9).FontColor("#334155");
                                table.Cell().Element(c => Cell(c, bg)).AlignRight().Text($"{d.UnitPrice:C}").FontSize(9);
                                table.Cell().Element(c => Cell(c, bg)).AlignRight().Text($"{d.UnitPrice * d.Quantity:C}").FontSize(9).Bold();
                            }
                        });

                        // Total
                        col.Item().PaddingTop(4).AlignRight().Width(165).Background("#0f766e").Padding(12).Row(row =>
                        {
                            row.RelativeItem().Text("TOTAL").FontSize(11).Bold().FontColor("#ffffff");
                            row.AutoItem().Text($"{data.Presupuesto.Total:C}").FontSize(13).Bold().FontColor("#ccfbf1");
                        });

                        // Notas
                        if (!string.IsNullOrWhiteSpace(data.Presupuesto.Notas))
                        {
                            col.Item().PaddingTop(20).Column(c =>
                            {
                                c.Item().Text("OBSERVACIONES").FontSize(8).Bold().FontColor("#64748b");
                                c.Item().PaddingTop(4).Background("#f8fafc").Border(0.5f).BorderColor("#e2e8f0").Padding(10)
                                    .Text(data.Presupuesto.Notas).FontSize(9).FontColor("#334155");
                            });
                        }

                        col.Item().PaddingTop(24).Text("Los precios indicados no incluyen IVA salvo indicación expresa. Este presupuesto no constituye factura.")
                            .FontSize(8).FontColor("#94a3b8").Italic();
                    });

                    page.Footer().BorderTop(0.5f).BorderColor("#cbd5e1").PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Text($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8).FontColor("#94a3b8");
                        row.RelativeItem().AlignCenter().Text(data.Config.NombreNegocio).FontSize(8).FontColor("#94a3b8");
                        row.RelativeItem().AlignRight().Text(x =>
                        {
                            x.Span("Pág. ").FontSize(8).FontColor("#94a3b8");
                            x.CurrentPageNumber().FontSize(8).FontColor("#94a3b8");
                        });
                    });
                });
            }).GeneratePdf();
        }
    }
}