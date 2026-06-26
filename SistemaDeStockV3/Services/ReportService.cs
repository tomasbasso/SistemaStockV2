using ClosedXML.Excel;
using SistemaDeStockV3.Models;

namespace SistemaDeStockV3.Services
{
    public class ReportService
    {
        public byte[] GenerateInventoryReport(List<Producto> productos, List<Categoria> categorias)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Inventario");

            // Header
            ws.Cell(1, 1).Value = "SKU";
            ws.Cell(1, 2).Value = "Producto";
            ws.Cell(1, 3).Value = "Categoría";
            ws.Cell(1, 4).Value = "Precio Venta";
            ws.Cell(1, 5).Value = "Stock";
            ws.Cell(1, 6).Value = "Valor Inmovilizado (Costo)";

            var headerRow = ws.Range("A1:F1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.AirForceBlue;
            headerRow.Style.Font.FontColor = XLColor.White;

            // Data
            int row = 2;
            foreach (var p in productos)
            {
                var cat = categorias.FirstOrDefault(c => c.Id == p.CategoryId);
                ws.Cell(row, 1).Value = p.SKU;
                ws.Cell(row, 2).Value = p.Name;
                ws.Cell(row, 3).Value = cat?.Name ?? "Sin Categoría";
                ws.Cell(row, 4).Value = p.Price;
                ws.Cell(row, 4).Style.NumberFormat.Format = "$ #,##0.00";
                ws.Cell(row, 5).Value = p.Stock;
                
                decimal total = p.Stock > 0 ? p.Stock * p.PrecioCosto : 0;
                ws.Cell(row, 6).Value = total;
                ws.Cell(row, 6).Style.NumberFormat.Format = "$ #,##0.00";
                
                if (p.Stock <= p.StockMinimo)
                {
                    ws.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
                    ws.Cell(row, 5).Style.Font.Bold = true;
                }

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] GenerateSalesReport(List<Venta> ventas, List<Cliente> clientes)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Ventas");

            ws.Cell(1, 1).Value = "Nro. Venta";
            ws.Cell(1, 2).Value = "Fecha y Hora";
            ws.Cell(1, 3).Value = "Cliente";
            ws.Cell(1, 4).Value = "Tipo Pago";
            ws.Cell(1, 5).Value = "Total (ARS)";

            var headerRow = ws.Range("A1:E1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.DarkGreen;
            headerRow.Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var v in ventas.OrderByDescending(x => x.Date))
            {
                string clienteName = "Consumidor Final";
                if (v.ClienteId.HasValue)
                {
                    var c = clientes.FirstOrDefault(x => x.Id == v.ClienteId.Value);
                    if (c != null) clienteName = c.Name;
                }

                ws.Cell(row, 1).Value = v.NumeroVenta;
                ws.Cell(row, 2).Value = v.Date.ToString("yyyy-MM-dd HH:mm");
                ws.Cell(row, 3).Value = clienteName;
                ws.Cell(row, 4).Value = v.IsFiado ? "Fiado (C/C)" : "Contado";
                
                ws.Cell(row, 5).Value = v.Total;
                ws.Cell(row, 5).Style.NumberFormat.Format = "$ #,##0.00";

                row++;
            }
            
            // Total Row
            ws.Cell(row, 4).Value = "TOTAL VENTAS:";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 5).FormulaA1 = $"=SUM(E2:E{row-1})";
            ws.Cell(row, 5).Style.Font.Bold = true;
            ws.Cell(row, 5).Style.NumberFormat.Format = "$ #,##0.00";

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] GenerateFinancialReport(List<MovimientoFinanciero> movimientos)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Finanzas");

            ws.Cell(1, 1).Value = "Fecha y Hora";
            ws.Cell(1, 2).Value = "Concepto";
            ws.Cell(1, 3).Value = "Tipo";
            ws.Cell(1, 4).Value = "Ingreso (+) / Egreso (-)";

            var headerRow = ws.Range("A1:D1");
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.DarkBlue;
            headerRow.Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var m in movimientos.OrderByDescending(x => x.Date))
            {
                ws.Cell(row, 1).Value = m.Date.ToString("yyyy-MM-dd HH:mm");
                ws.Cell(row, 2).Value = m.Description;
                ws.Cell(row, 3).Value = m.Type.ToString();
                
                decimal displayAmount = m.Type == TipoMovimiento.Egreso ? -m.Amount : m.Amount;
                ws.Cell(row, 4).Value = displayAmount;
                ws.Cell(row, 4).Style.NumberFormat.Format = "$ #,##0.00;[Red]-$ #,##0.00";

                row++;
            }
            
            // Total Row
            ws.Cell(row, 3).Value = "BALANCE NETO:";
            ws.Cell(row, 3).Style.Font.Bold = true;
            ws.Cell(row, 4).FormulaA1 = $"=SUM(D2:D{row-1})";
            ws.Cell(row, 4).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.NumberFormat.Format = "$ #,##0.00;[Red]-$ #,##0.00";

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] GenerateInventoryRotationReport(List<RotacionProductoDto> rotaciones)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Rotación");

            // Encabezados
            ws.Cell(1, 1).Value = "Producto";
            ws.Cell(1, 2).Value = "Categoría";
            ws.Cell(1, 3).Value = "Ventas 12m";
            ws.Cell(1, 4).Value = "Stock";
            ws.Cell(1, 5).Value = "Rotación";
            ws.Cell(1, 6).Value = "Valor Inmovilizado";
            ws.Cell(1, 7).Value = "Margen";
            ws.Cell(1, 8).Value = "Tendencia";
            ws.Cell(1, 9).Value = "Última Venta";
            ws.Cell(1, 10).Value = "Días sin Venta";
            ws.Cell(1, 11).Value = "Estado";
            ws.Cell(1, 12).Value = "Acción Sugerida";

            var headerRange = ws.Range("A1:L1");
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d2442");
            headerRange.Style.Font.FontColor = XLColor.White;

            // Datos
            int row = 2;
            foreach (var r in rotaciones)
            {
                ws.Cell(row, 1).Value = r.Nombre;
                ws.Cell(row, 2).Value = r.Categoria;
                ws.Cell(row, 3).Value = r.UnidadesVendidas12m;
                ws.Cell(row, 4).Value = r.StockActual;
                ws.Cell(row, 5).Value = (double)r.Rotacion;
                ws.Cell(row, 5).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 6).Value = (double)r.ValorInmovilizado;
                ws.Cell(row, 6).Style.NumberFormat.Format = "$ #,##0.00";
                ws.Cell(row, 7).Value = (double)r.MargenUnitario;
                ws.Cell(row, 7).Style.NumberFormat.Format = "0.0%";
                ws.Cell(row, 8).Value = r.Tendencia;
                ws.Cell(row, 9).Value = r.UltimaVenta.HasValue ? r.UltimaVenta.Value.ToString("dd/MM/yyyy") : "—";
                ws.Cell(row, 10).Value = r.DiasSinVenta;
                ws.Cell(row, 11).Value = r.EstadoRotacion;
                ws.Cell(row, 12).Value = r.AccionSugerida;

                // Color de fila por estado
                var rowRange = ws.Range(row, 1, row, 12);
                rowRange.Style.Fill.BackgroundColor = r.EstadoRotacion switch
                {
                    "Alta" => XLColor.FromHtml("#d4edda"),
                    "Media" => XLColor.FromHtml("#cce5ff"),
                    "Baja" => XLColor.FromHtml("#fff3cd"),
                    _ => XLColor.FromHtml("#f8d7da")
                };

                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
