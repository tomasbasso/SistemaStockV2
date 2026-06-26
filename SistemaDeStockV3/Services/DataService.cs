using Microsoft.EntityFrameworkCore;
using SistemaDeStockV3.Data;
using SistemaDeStockV3.Models;
using System;
using System.Data;
using System.Globalization;
using System.Linq;

namespace SistemaDeStockV3.Services
{
    /// <summary>
    /// Servicio principal de acceso a datos.
    /// Utiliza directamente StockDbContext con SQLite local puro.
    /// </summary>
    public class DataService
    {
        private readonly StockDbContext _db;

        public DataService(StockDbContext db)
        {
            _db = db;
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // INICIALIZACIÓN
        // ──────────────────────────────────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            await _db.InitializeDatabaseAsync();
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // CONFIGURACIÓN
        // ──────────────────────────────────────────────────────────────────────────────────

        public async Task<ConfiguracionApp?> GetConfiguracionAsync()
            => await _db.Configuraciones.FirstOrDefaultAsync();

        public async Task CambiarStockAsync(Guid productoId, int variacion)
        {
            var p = await _db.Productos.FindAsync(productoId);
            if (p != null)
            {
                p.Stock += variacion;
                if (p.Stock < 0) p.Stock = 0;
                await _db.SaveChangesAsync();
            }
        }

        public async Task SaveConfiguracionAsync(ConfiguracionApp config)
        {
            var existing = await _db.Configuraciones.FindAsync(config.Id);
            if (existing == null)
                _db.Configuraciones.Add(config);
            else
            {
                existing.NombreNegocio = config.NombreNegocio;
                existing.Moneda = config.Moneda;
                existing.DireccionNegocio = config.DireccionNegocio;
                existing.Telefono = config.Telefono;
                existing.UmbralRotacionBaja = config.UmbralRotacionBaja;
                existing.UmbralRotacionMedia = config.UmbralRotacionMedia;
                existing.DiasAlertaSinVenta = config.DiasAlertaSinVenta;
            }
            await _db.SaveChangesAsync();
        }

        // CATEGORIAS
        
        public async Task<List<Categoria>> GetCategoriasAsync()
            => await _db.Categorias.OrderBy(c => c.Name).ToListAsync();

        public async Task SaveCategoriaAsync(Categoria c)
        {
            var existing = await _db.Categorias.FindAsync(c.Id);
            if (existing == null)
                _db.Categorias.Add(c);
            else
                existing.Name = c.Name;

            await _db.SaveChangesAsync();
        }

        public async Task DeleteCategoriaAsync(Guid id)
        {
            var entity = await _db.Categorias.FindAsync(id);
            if (entity != null)
            {
                _db.Categorias.Remove(entity);
                await _db.SaveChangesAsync();
            }
        }

        // PRODUCTOS
        
        public async Task<List<Producto>> GetProductosAsync()
            => await _db.Productos.OrderBy(p => p.Name).ToListAsync();

        public async Task<int> GetTotalProductosAsync(string searchTerm = "")
        {
            var query = _db.Productos.AsQueryable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var words = searchTerm.ToLower()
                                      .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    var w = word; // captura local para la lambda
                    query = query.Where(p =>
                        p.Name.ToLower().Contains(w) ||
                        (p.SKU != null && p.SKU.ToLower().Contains(w)));
                }
            }
            return await query.CountAsync();
        }

        public async Task<List<Producto>> GetProductosPaginadosAsync(int page, int pageSize, string searchTerm = "")
        {
            var query = _db.Productos.AsQueryable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var words = searchTerm.ToLower()
                                      .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in words)
                {
                    var w = word; // captura local para la lambda
                    query = query.Where(p =>
                        p.Name.ToLower().Contains(w) ||
                        (p.SKU != null && p.SKU.ToLower().Contains(w)));
                }
            }
            return await query
                .OrderBy(p => p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task SaveProductoAsync(Producto p)
        {
            var existing = await _db.Productos.FindAsync(p.Id);
            if (existing == null)
                _db.Productos.Add(p);
            else
            {
                var precioAnterior = existing.Price;
                existing.Name = p.Name;
                existing.SKU = p.SKU;
                existing.CategoryId = p.CategoryId;
                existing.Stock = p.Stock;
                existing.StockMinimo = p.StockMinimo;
                existing.Price = p.Price;
                existing.PrecioCosto = p.PrecioCosto;
                existing.Margen = p.Margen;
                existing.UnidadMedida = p.UnidadMedida;
                existing.Ubicacion = p.Ubicacion;

                RegistrarHistorialPrecio(existing, precioAnterior, existing.Price);
            }
            await _db.SaveChangesAsync();
        }

        public async Task<Producto?> GetProductoPorCodigoBarrasAsync(string codigoBarras)
            => await _db.Productos
                .FirstOrDefaultAsync(p => p.CodigoBarras != null && p.CodigoBarras == codigoBarras);

        public async Task AsignarCodigoBarrasAsync(Guid productoId, string codigoBarras)
        {
            var existente = await _db.Productos
                .FirstOrDefaultAsync(p => p.CodigoBarras == codigoBarras && p.Id != productoId);
            if (existente != null)
                throw new InvalidOperationException($"El código '{codigoBarras}' ya está asignado a '{existente.Name}'.");

            var producto = await _db.Productos.FindAsync(productoId)
                ?? throw new InvalidOperationException("Producto no encontrado.");

            producto.CodigoBarras = codigoBarras;
            await _db.SaveChangesAsync();
        }

        public async Task AjustarPreciosPorcentajeAsync(List<Guid> productoIds, decimal porcentaje)
        {
            var productos = await _db.Productos.Where(p => productoIds.Contains(p.Id)).ToListAsync();
            foreach (var p in productos)
            {
                var precioAnterior = p.Price;
                p.Price = Math.Max(0, Math.Round(p.Price * (1 + porcentaje / 100), 2));
                RegistrarHistorialPrecio(p, precioAnterior, p.Price);
            }
            await _db.SaveChangesAsync();
        }

        public async Task<Result<(int Importados, int Actualizados, List<string> Errores)>> ImportarProductosDesdeExcelAsync(Stream stream, Guid categoriaDefaultId)
        {
            try
            {
            int importados = 0, actualizados = 0;
            var errores = new List<string>();

            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = workbook.Worksheets.First();

            var firstCell = ws.Cell(1, 1).GetValue<string>()?.Trim().ToLower() ?? "";
            bool esEncabezado = firstCell.Contains("sku") || firstCell.Contains("cod") || firstCell.Contains("nombre") || firstCell.Contains("producto");
            int startRow = esEncabezado ? 2 : 1;

            int colSku = 1, colNombre = 2, colPrecio = 3;
            if (startRow == 2)
            {
                for (int c = 1; c <= ws.LastColumnUsed().ColumnNumber(); c++)
                {
                    var h = ws.Cell(1, c).GetValue<string>()?.Trim().ToLower() ?? "";
                    if (h.Contains("sku") || h.Contains("cod")) colSku = c;
                    else if (h.Contains("nombre") || h.Contains("producto") || h.Contains("descrip")) colNombre = c;
                    else if (h.Contains("precio") || h.Contains("price") || h.Contains("costo") || h.Contains("valor")) colPrecio = c;
                }
            }

            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            var skusExistentes = await _db.Productos.ToDictionaryAsync(p => (p.SKU ?? "").ToLower(), p => p);

            for (int row = startRow; row <= lastRow; row++)
            {
                try
                {
                    var sku = ws.Cell(row, colSku).GetValue<string>()?.Trim() ?? "";
                    var nombre = ws.Cell(row, colNombre).GetValue<string>()?.Trim() ?? "";
                    var rawPrecio = ws.Cell(row, colPrecio).GetValue<string>() ?? "";
                    var rawStr = rawPrecio.Trim().Replace("$", "").Replace(" ", "");
                    string precioStr;
                    if (rawStr.Contains(","))
                        precioStr = rawStr.Replace(".", "").Replace(",", ".");
                    else
                        precioStr = rawStr;

                    if (string.IsNullOrWhiteSpace(nombre)) continue;

                    if (!decimal.TryParse(precioStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal precio) || precio < 0)
                    {
                        errores.Add($"Fila {row}: precio inválido '{rawPrecio}'");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(sku))
                        sku = $"IMP-{row:D4}";

                    if (skusExistentes.TryGetValue(sku.ToLower(), out var existing))
                    {
                        existing.Name = nombre;
                        var precioAnterior = existing.Price;
                        existing.Price = precio;
                        
                        existing.Margen = existing.PrecioCosto > 0 
                            ? Math.Round(((precio / existing.PrecioCosto) - 1) * 100m, 2) 
                            : 100m;

                        if (precioAnterior != existing.Price)
                            RegistrarHistorialPrecio(existing, precioAnterior, existing.Price);
                        actualizados++;
                    }
                    else
                    {
                        var nuevo = new Producto
                        {
                            Name = nombre,
                            SKU = sku,
                            Price = precio,
                            PrecioCosto = 0,
                            Margen = 100m,
                            CategoryId = categoriaDefaultId,
                            Stock = 5,
                            StockMinimo = 0,
                            UnidadMedida = "u."
                        };
                        _db.Productos.Add(nuevo);
                        importados++;
                    }
                }
                catch (Exception ex)
                {
                    errores.Add($"Fila {row}: {ex.Message}");
                }
            }

            await _db.SaveChangesAsync();
            return Result<(int, int, List<string>)>.Ok((importados, actualizados, errores));
            }
            catch (Exception ex)
            {
                return Result<(int, int, List<string>)>.Fail($"Error al procesar el archivo: {ex.Message}");
            }
        }

        // Presupuestos 
        public async Task<List<Presupuesto>> GetPresupuestosAsync()
            => await _db.Presupuestos.OrderByDescending(p => p.Date).ToListAsync();

        public async Task<List<PresupuestoDetalle>> GetPresupuestoDetallesAsync(Guid presupuestoId)
            => await _db.PresupuestoDetalles.Where(d => d.PresupuestoId == presupuestoId).ToListAsync();

        public async Task<Presupuesto> SavePresupuestoAsync(Presupuesto presupuesto, List<PresupuestoDetalle> detalles)
        {
            int maxNum = await _db.Presupuestos.AnyAsync()
                ? await _db.Presupuestos.MaxAsync(p => p.NumeroPresupuesto)
                : 0;
            presupuesto.NumeroPresupuesto = maxNum + 1;
            presupuesto.Total = detalles.Sum(d => d.UnitPrice * d.Quantity);

            _db.Presupuestos.Add(presupuesto);
            foreach (var d in detalles)
            {
                d.PresupuestoId = presupuesto.Id;
                _db.PresupuestoDetalles.Add(d);
            }
            await _db.SaveChangesAsync();
            return presupuesto;
        }

        public async Task DeletePresupuestoAsync(Guid id)
        {
            var entity = await _db.Presupuestos.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
            if (entity != null)
            {
                entity.IsDeleted = true;
                var detalles = await _db.PresupuestoDetalles.Where(d => d.PresupuestoId == id).ToListAsync();
                _db.PresupuestoDetalles.RemoveRange(detalles);
                await _db.SaveChangesAsync();
            }
        }

        public async Task DeleteProductoAsync(Guid id)
        {
            var entity = await _db.Productos.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
            if (entity != null)
            {
                entity.IsDeleted = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task DeleteProductosAsync(List<Guid> ids)
        {
            var entities = await _db.Productos.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ToListAsync();
            foreach (var entity in entities)
            {
                entity.IsDeleted = true;
            }
            if (entities.Any())
            {
                await _db.SaveChangesAsync();
            }
        }

        public async Task<bool> ExisteProductoPorSKUAsync(string sku, Guid excludeId)
        {
            return await _db.Productos.AnyAsync(p => p.SKU == sku && p.Id != excludeId);
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // CLIENTES & CUENTAS CORRIENTES
        // ──────────────────────────────────────────────────────────────────────────────────

        public async Task<List<Cliente>> GetClientesAsync()
            => await _db.Clientes.OrderBy(c => c.Name).ToListAsync();

        public async Task SaveClienteAsync(Cliente c)
        {
            c.Phone ??= string.Empty;
            c.Address ??= string.Empty;
            c.CUIT ??= string.Empty;
            c.Email ??= string.Empty;

            var existing = await _db.Clientes.FindAsync(c.Id);
            if (existing == null)
            {
                _db.Clientes.Add(c);
                _db.CuentasCorrientes.Add(new CuentaCorriente { ClienteId = c.Id });
            }
            else
            {
                existing.Name = c.Name;
                existing.Phone = c.Phone;
                existing.Address = c.Address;
                existing.CUIT = c.CUIT;
                existing.Email = c.Email;
                existing.CondicionIva = c.CondicionIva;
            }
            await _db.SaveChangesAsync();
        }

        public async Task DeleteClienteAsync(Guid id)
        {
            var entity = await _db.Clientes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
            if (entity != null)
            {
                entity.IsDeleted = true;
                var cc = await _db.CuentasCorrientes.FirstOrDefaultAsync(x => x.ClienteId == id);
                if (cc != null) _db.CuentasCorrientes.Remove(cc);

                await _db.SaveChangesAsync();
            }
        }

        public async Task<CuentaCorriente?> GetCuentaCorrienteAsync(Guid clienteId)
            => await _db.CuentasCorrientes.FirstOrDefaultAsync(x => x.ClienteId == clienteId);

        public async Task<List<CuentaCorriente>> GetCuentasCorrientesAsync()
            => await _db.CuentasCorrientes.ToListAsync();

        public async Task SaveCuentaCorrienteAsync(CuentaCorriente cc)
        {
            var existing = await _db.CuentasCorrientes.FindAsync(cc.Id);
            if (existing == null)
                _db.CuentasCorrientes.Add(cc);
            else
                existing.Balance = cc.Balance;

            await _db.SaveChangesAsync();
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // MOVIMIENTOS FINANCIEROS
        // ──────────────────────────────────────────────────────────────────────────────────

        public async Task<List<MovimientoFinanciero>> GetMovimientosAsync()
            => await _db.MovimientosFinancieros.OrderByDescending(m => m.Date).ToListAsync();

        public async Task<int> GetTotalMovimientosAsync(string searchTerm = "")
        {
            var query = _db.MovimientosFinancieros.AsQueryable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(m => m.Description != null && m.Description.ToLower().Contains(term));
            }
            return await query.CountAsync();
        }

        public async Task<(decimal Ingresos, decimal Egresos)> GetTotalesMovimientosAsync()
        {
            var ingresos = await _db.MovimientosFinancieros
                .Where(m => m.Type == TipoMovimiento.Ingreso)
                .SumAsync(m => m.Amount);

            var egresos = await _db.MovimientosFinancieros
                .Where(m => m.Type == TipoMovimiento.Egreso)
                .SumAsync(m => m.Amount);

            return (ingresos, egresos);
        }

        public async Task<(decimal TotalVentas, int CantidadVentas, decimal TotalDeuda, decimal ValorInventario, int TotalProductos, List<Producto> BajoStock, List<MovimientoFinanciero> UltimosMovimientos)> GetDashboardDataAsync()
        {
            var hoyInicio = DateTime.Today;
            var hoyFin = DateTime.Today.AddDays(1).AddTicks(-1);

            var ventasHoy = await _db.Ventas
                .Where(v => v.Date >= hoyInicio && v.Date <= hoyFin)
                .Select(v => new { v.Total })
                .ToListAsync();

            var totalVentas = ventasHoy.Sum(v => v.Total);
            var cantidadVentas = ventasHoy.Count;

            var totalDeuda = (await _db.CuentasCorrientes
                .Select(c => c.Balance)
                .ToListAsync())
                .Sum();

            var productosData = await _db.Productos
                .Select(p => new { p.Price, p.Stock, p.StockMinimo })
                .ToListAsync();

            var valorInventario = productosData.Sum(p => p.Price * Math.Max(0, p.Stock));
            var totalProductos = productosData.Count;

            var bajoStock = await _db.Productos
                .Where(p => p.Stock <= p.StockMinimo)
                .OrderBy(p => p.Stock)
                .ToListAsync();

            var ultimosMovimientos = await _db.MovimientosFinancieros
                .Where(m => m.Date >= hoyInicio && m.Date <= hoyFin)
                .OrderByDescending(m => m.Date)
                .Take(10)
                .ToListAsync();

            return (totalVentas, cantidadVentas, totalDeuda, valorInventario, totalProductos, bajoStock, ultimosMovimientos);
        }

        public async Task<List<MovimientoFinanciero>> GetMovimientosPaginadosAsync(int page, int pageSize, string searchTerm = "")
        {
            var query = _db.MovimientosFinancieros.AsQueryable();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(m => m.Description != null && m.Description.ToLower().Contains(term));
            }
            return await query.OrderByDescending(m => m.Date).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        }

        public async Task AddMovimientoAsync(MovimientoFinanciero m)
        {
            _db.MovimientosFinancieros.Add(m);
            await _db.SaveChangesAsync();
        }

        public async Task DeleteMovimientoAsync(Guid id)
        {
            var entity = await _db.MovimientosFinancieros.FindAsync(id);
            if (entity != null)
            {
                _db.MovimientosFinancieros.Remove(entity);
                await _db.SaveChangesAsync();
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────────
        // VENTAS
        // ──────────────────────────────────────────────────────────────────────────────────

        public async Task<double> CalcularRotacionAnualAsync()
        {
            try 
            {
                var haceUnAnio = DateTime.Today.AddYears(-1);

                // LINQ puro para rotación
                var unidadesVendidas = await _db.VentaDetalles
                    .Join(_db.Ventas.Where(v => !v.IsDeleted && v.Date >= haceUnAnio),
                        vd => vd.VentaId,
                        v => v.Id,
                        (vd, v) => vd.Quantity)
                    .SumAsync(q => (double)q);

                var stockActual = await _db.Productos
                    .Where(p => !p.IsDeleted)
                    .SumAsync(p => (double)p.Stock);

                if (stockActual <= 0) return 0;

                var resultado = unidadesVendidas / stockActual;
                return Math.Round(resultado, 2);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculando rotación: {ex.Message}");
                return 0;
            }
        }

        public async Task<List<Venta>> GetVentasAsync()
            => await _db.Ventas.OrderByDescending(v => v.Date).ToListAsync();

        public async Task<int> GetTotalVentasAsync(string searchTerm = "", string range = "all")
        {
            var query = _db.Ventas.AsQueryable();
            query = ApplyVentasFilters(query, searchTerm, range);
            return await query.CountAsync();
        }

        public async Task<List<Venta>> GetVentasPaginadasAsync(int page, int pageSize, string searchTerm = "", string range = "all")
        {
            var query = _db.Ventas.AsQueryable();
            query = ApplyVentasFilters(query, searchTerm, range);
            
            return await query
                .OrderByDescending(v => v.Date)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        private IQueryable<Venta> ApplyVentasFilters(IQueryable<Venta> query, string searchTerm, string range)
        {
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                var clientIds = _db.Clientes
                    .Where(c => c.Name.ToLower().Contains(term))
                    .Select(c => c.Id);

                query = query.Where(v => v.NumeroVenta.ToString().Contains(term) || (v.ClienteId.HasValue && clientIds.Contains(v.ClienteId.Value)));
            }

            if (range == "week")
            {
                var date = DateTime.Today.AddDays(-7);
                query = query.Where(v => v.Date >= date);
            }
            else if (range == "month")
            {
                var date = DateTime.Today.AddMonths(-1);
                query = query.Where(v => v.Date >= date);
            }

            return query;
        }

        public async Task<List<VentaDetalle>> GetVentaDetallesAsync(Guid ventaId)
            => await _db.VentaDetalles.Where(d => d.VentaId == ventaId).ToListAsync();

        public async Task<List<VentaFiadaDetalle>> GetVentasFiadasPorClienteAsync(Guid clienteId)
        {
            var ventas = await _db.Ventas
                .Where(v => v.ClienteId == clienteId && v.IsFiado)
                .OrderByDescending(v => v.Date)
                .ToListAsync();

            var r = new List<VentaFiadaDetalle>();
            foreach (var v in ventas)
            {
                var detalles = await _db.VentaDetalles
                    .Where(d => d.VentaId == v.Id)
                    .Join(_db.Productos, d => d.ProductoId, p => p.Id, (d, p) => $"{d.Quantity}x {p.Name} ({d.UnitPrice:C})")
                    .ToListAsync();

                r.Add(new VentaFiadaDetalle
                {
                    NumeroVenta = v.NumeroVenta,
                    Fecha = v.Date,
                    Total = v.Total,
                    Items = detalles
                });
            }
            return r;
        }

        public async Task<bool> ProcesarVentaAsync(Venta venta, List<VentaDetalle> detalles)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                foreach (var d in detalles)
                {
                    var producto = await _db.Productos.FindAsync(d.ProductoId)
                        ?? throw new InvalidOperationException($"Producto con ID {d.ProductoId} no encontrado.");

                    if (producto.Stock < d.Quantity)
                        throw new InvalidOperationException($"Stock insuficiente para \"{producto.Name}\". Disponible: {producto.Stock}, solicitado: {d.Quantity}.");
                }

                foreach (var d in detalles)
                {
                    var producto = await _db.Productos.FindAsync(d.ProductoId)!;
                    producto!.Stock -= d.Quantity;
                }

                int maxNumero = await _db.Ventas.AnyAsync()
                    ? await _db.Ventas.MaxAsync(v => v.NumeroVenta)
                    : 0;
                venta.NumeroVenta = maxNumero + 1;

                _db.Ventas.Add(venta);

                if (venta.IsFiado && venta.ClienteId.HasValue)
                {
                    var cc = await _db.CuentasCorrientes.FirstOrDefaultAsync(x => x.ClienteId == venta.ClienteId.Value)
                        ?? throw new InvalidOperationException("El cliente no tiene cuenta corriente asociada.");
                    cc.Balance += venta.Total;
                }
                else
                {
                    _db.MovimientosFinancieros.Add(new MovimientoFinanciero
                    {
                        Type = TipoMovimiento.Ingreso,
                        Amount = venta.Total,
                        Description = $"Venta #{venta.NumeroVenta}",
                        VentaId = venta.Id
                    });
                }

                foreach (var d in detalles)
                {
                    d.VentaId = venta.Id;
                    _db.VentaDetalles.Add(d);
                }

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task AnularVentaAsync(Guid ventaId)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var venta = await _db.Ventas.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(v => v.Id == ventaId)
                    ?? throw new InvalidOperationException("Venta no encontrada.");

                if (venta.IsDeleted)
                    throw new InvalidOperationException("La venta ya fue anulada.");

                var detalles = await _db.VentaDetalles.Where(d => d.VentaId == ventaId).ToListAsync();
                foreach (var d in detalles)
                {
                    var producto = await _db.Productos.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(p => p.Id == d.ProductoId);
                    if (producto != null)
                        producto.Stock += d.Quantity;
                }

                if (!venta.IsFiado)
                {
                    var movimiento = await _db.MovimientosFinancieros
                        .FirstOrDefaultAsync(m => m.VentaId == ventaId);
                    if (movimiento != null)
                        _db.MovimientosFinancieros.Remove(movimiento);
                }
                else if (venta.ClienteId.HasValue)
                {
                    var cc = await _db.CuentasCorrientes
                        .FirstOrDefaultAsync(x => x.ClienteId == venta.ClienteId.Value);
                    if (cc != null)
                        cc.Balance -= venta.Total;
                }

                venta.IsDeleted = true;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task PagarFiadoAsync(Guid clienteId, decimal amount)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var cc = await _db.CuentasCorrientes
                    .FirstOrDefaultAsync(x => x.ClienteId == clienteId)
                    ?? throw new InvalidOperationException("Cuenta corriente no encontrada.");

                if (amount <= 0 || amount > cc.Balance)
                    throw new InvalidOperationException("Monto de pago inválido.");

                var cliente = await _db.Clientes.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == clienteId);

                _db.MovimientosFinancieros.Add(new MovimientoFinanciero
                {
                    Type = TipoMovimiento.Ingreso,
                    Amount = amount,
                    Description = $"Cobro C/C - {cliente?.Name ?? "Cliente"}"
                });

                cc.Balance -= amount;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<List<HistorialPrecio>> GetHistorialPreciosAsync()
            => await _db.HistorialPrecios.OrderByDescending(h => h.FechaModificacion).ToListAsync();

        private void RegistrarHistorialPrecio(Producto producto, decimal precioAnterior, decimal precioNuevo)
        {
            if (precioAnterior == precioNuevo) return;

            _db.HistorialPrecios.Add(new HistorialPrecio
            {
                ProductoId = producto.Id,
                ProductoNombre = producto.Name,
                FechaModificacion = DateTime.Now,
                PrecioAnterior = precioAnterior,
                PrecioNuevo = precioNuevo
            });
        }

        private async Task<(decimal UmbralBaja, decimal UmbralMedia, int DiasSinVenta)> GetUmbralesAsync()
        {
            var config = await _db.Configuraciones.FirstOrDefaultAsync();
            return (
                config?.UmbralRotacionBaja ?? 1.0m,
                config?.UmbralRotacionMedia ?? 4.0m,
                config?.DiasAlertaSinVenta ?? 90
            );
        }

        public async Task<List<RotacionProductoDto>> GetRotacionProductosAsync(Guid? categoriaId = null, string? search = null, bool soloBaja = false, int take = 200)
        {
            var (umbralBaja, umbralMedia, diasSinVentaCfg) = await GetUmbralesAsync();

            var desde12 = DateTime.Today.AddYears(-1);
            var desde3 = DateTime.Today.AddMonths(-3);
            var desde6 = DateTime.Today.AddMonths(-6);

            // Descargar los productos y ventas a memoria para calcular la rotacion. 
            // EF Core a veces se queja de LEFT JOINs muy complejos si no es SQL raw.
            // Para mantener compatibilidad con SQLite pura, usamos LINQ.

            var productos = await _db.Productos.Where(p => !p.IsDeleted).ToListAsync();
            var categorias = await _db.Categorias.ToDictionaryAsync(c => c.Id, c => c.Name);
            
            var ventasRecientes = await _db.VentaDetalles
                .Join(_db.Ventas.Where(v => !v.IsDeleted && v.Date >= desde12),
                    vd => vd.VentaId,
                    v => v.Id,
                    (vd, v) => new { vd.ProductoId, vd.Quantity, v.Date })
                .ToListAsync();

            var results = new List<RotacionProductoDto>();

            foreach (var p in productos)
            {
                var ventas12 = ventasRecientes.Where(v => v.ProductoId == p.Id).Sum(v => v.Quantity);
                var ventas3 = ventasRecientes.Where(v => v.ProductoId == p.Id && v.Date >= desde3).Sum(v => v.Quantity);
                var ventasPrev3 = ventasRecientes.Where(v => v.ProductoId == p.Id && v.Date >= desde6 && v.Date < desde3).Sum(v => v.Quantity);
                var ultimaVenta = ventasRecientes.Where(v => v.ProductoId == p.Id).OrderByDescending(v => v.Date).FirstOrDefault()?.Date;

                var stock = Math.Max(0, p.Stock);
                var rotacion = stock > 0 ? (decimal)ventas12 / Math.Max(1, stock) : 0;
                var diasSinVenta = ultimaVenta.HasValue ? (int)(DateTime.Today - ultimaVenta.Value.Date).TotalDays : 9999;
                var valorInmovilizado = stock * p.Price;
                var margenUnitario = p.Price > 0 ? (p.Price - p.PrecioCosto) / p.Price : 0;

                string tendencia = "→";
                if (ventas3 > ventasPrev3) tendencia = "↗";
                else if (ventas3 < ventasPrev3) tendencia = "↘";

                string estado = "Sin rotación";
                if (rotacion == 0) estado = "Sin rotación";
                else if (rotacion < umbralBaja) estado = "Baja";
                else if (rotacion < umbralMedia) estado = "Media";
                else estado = "Alta";

                string accion = estado switch
                {
                    "Sin rotación" => "Descontinuar / limpiar stock",
                    "Baja" => "Promocionar o ajustar precio",
                    "Media" => "Monitorear",
                    _ => "Mantener"
                };

                results.Add(new RotacionProductoDto
                {
                    ProductoId = p.Id,
                    Nombre = p.Name,
                    Categoria = categorias.TryGetValue(p.CategoryId, out var cName) ? cName : "",
                    UnidadesVendidas12m = ventas12,
                    StockActual = stock,
                    Rotacion = Math.Round(rotacion, 2),
                    UltimaVenta = ultimaVenta,
                    DiasSinVenta = diasSinVenta,
                    ValorInmovilizado = Math.Round(valorInmovilizado, 2),
                    MargenUnitario = Math.Round(margenUnitario, 2),
                    Tendencia = tendencia,
                    EstadoRotacion = estado,
                    AccionSugerida = accion
                });
            }

            var query = results.AsEnumerable();
            if (categoriaId.HasValue)
            {
                var categoria = categorias.TryGetValue(categoriaId.Value, out var catName) ? catName : null;
                if (categoria != null)
                    query = query.Where(r => string.Equals(r.Categoria, categoria, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.ToLowerInvariant();
                query = query.Where(r => r.Nombre.ToLower().Contains(s));
            }
            if (soloBaja)
                query = query.Where(r => r.EstadoRotacion == "Sin rotación" || r.EstadoRotacion == "Baja");

            return query
                .OrderBy(r => r.Rotacion)
                .ThenByDescending(r => r.DiasSinVenta)
                .Take(take)
                .ToList();
        }
    }
}
