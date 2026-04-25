using Microsoft.EntityFrameworkCore;
using Sistema_de_Stock.Data;
using Sistema_de_Stock.Models;
using System;
using System.Data;
using System.Globalization;

namespace Sistema_de_Stock.Services
{
    /// <summary>
    /// Servicio principal de acceso a datos, ahora basado en EF Core / SQLite.
    /// Mantiene el mismo contrato pÃºblico que antes para compatibilidad con los componentes Blazor.
    /// </summary>
    public class DataService
    {
        private readonly StockDbContext _db;

        public DataService(StockDbContext db)
        {
            _db = db;
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // INICIALIZACIÃ“N
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Inicializa la base de datos y aplica migraciones pendientes.
        /// Se llama desde MauiProgram o App.xaml.cs al iniciar.
        /// </summary>
        public async Task InitializeAsync()
        {
            await _db.InitializeDatabaseAsync();
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // CONFIGURACIÃ“N
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                // Verificar que el código no esté ya usado por otro producto
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

        /// <summary>
        /// Importa productos desde un stream de Excel (.xlsx).
        /// Columnas esperadas: SKU, Nombre, Precio (en ese orden o por nombre de cabecera).
        /// Si ya existe un producto con el mismo SKU, actualiza precio y nombre.
        /// Devuelve Result con (importados, actualizados, errores).
        /// </summary>
        /// 
        public async Task<Result<(int Importados, int Actualizados, List<string> Errores)>> ImportarProductosDesdeExcelAsync(Stream stream, Guid categoriaDefaultId)
        {
            int importados = 0, actualizados = 0;
            var errores = new List<string>();

            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = workbook.Worksheets.First();

            // Detectar si la primera fila es cabecera (usamos GetValue<string>() para seguridad)
            var firstCell = ws.Cell(1, 1).GetValue<string>()?.Trim().ToLower() ?? "";
            bool esEncabezado = firstCell.Contains("sku") || firstCell.Contains("cod") || firstCell.Contains("nombre") || firstCell.Contains("producto");
            int startRow = esEncabezado ? 2 : 1;

            // Detectar Ã­ndices de columnas por cabecera (si hay)
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
                    var precioStr = rawPrecio.Trim().Replace("$", "").Replace(".", "").Replace(",", ".");

                    if (string.IsNullOrWhiteSpace(nombre)) continue;

                    if (!decimal.TryParse(precioStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal precio) || precio < 0)
                    {
                        errores.Add($"Fila {row}: precio invÃ¡lido '{rawPrecio}'");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(sku))
                        sku = $"IMP-{row:D4}";

                    if (skusExistentes.TryGetValue(sku.ToLower(), out var existing))
                    {
                        existing.Name = nombre;
                        var precioAnterior = existing.Price;
                        existing.Price = precio;
                        
                        // Recalcular margen hacia atrás basado en el costo actual
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
                            Margen = 100m, // Margen del 100% por defecto si no hay costo
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

        // Presupuestos 
        public async Task<List<Presupuesto>> GetPresupuestosAsync()
            => await _db.Presupuestos.OrderByDescending(p => p.Date).ToListAsync();

        public async Task<List<PresupuestoDetalle>> GetPresupuestoDetallesAsync(Guid presupuestoId)
            => await _db.PresupuestoDetalles.Where(d => d.PresupuestoId == presupuestoId).ToListAsync();

        public async Task<Presupuesto> SavePresupuestoAsync(Presupuesto presupuesto, List<PresupuestoDetalle> detalles)
        {
            // NÃºmero secuencial
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
                // Eliminar detalles de presupuesto
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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // CLIENTES & CUENTAS CORRIENTES
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                // Crear cuenta corriente automÃ¡ticamente al crear un cliente nuevo
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
                // Eliminar cuenta corriente asociada
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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // MOVIMIENTOS FINANCIEROS
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
            var ingresos = (await _db.MovimientosFinancieros
                .Where(m => m.Type == TipoMovimiento.Ingreso)
                .Select(m => m.Amount)
                .ToListAsync())
                .Sum();

            var egresos = (await _db.MovimientosFinancieros
                .Where(m => m.Type == TipoMovimiento.Egreso)
                .Select(m => m.Amount)
                .ToListAsync())
                .Sum();
                
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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // VENTAS
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public async Task<double> CalcularRotacionAnualAsync()
        {
            try 
            {
                await EnsureSoftDeleteColumnsAsync();

                var haceUnAnio = DateTime.Today.AddYears(-1);

                var connection = _db.Database.GetDbConnection();
                bool wasClosed = connection.State == ConnectionState.Closed;
                if (wasClosed) await connection.OpenAsync();

                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT
                        COALESCE((
                            SELECT SUM(CAST(vd.Quantity AS REAL))
                            FROM VentaDetalles vd
                            JOIN Ventas v ON v.Id = vd.VentaId
                            WHERE (v.IsDeleted = 0 OR v.IsDeleted IS NULL)
                              AND v.Date >= @Desde
                        ), 0) AS UnidadesVendidas,
                        COALESCE((
                            SELECT SUM(CAST(p.Stock AS REAL))
                            FROM Productos p
                            WHERE (p.IsDeleted = 0 OR p.IsDeleted IS NULL)
                        ), 0) AS StockActual;";

                var param = command.CreateParameter();
                param.ParameterName = "@Desde";
                param.Value = haceUnAnio;
                command.Parameters.Add(param);

                double unidadesVendidas = 0;
                double stockActual = 0;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        unidadesVendidas = reader.IsDBNull(0) ? 0 : reader.GetDouble(0);
                        stockActual = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                    }
                }

                if (wasClosed) await connection.CloseAsync();

                if (stockActual == 0) return 0;

                var resultado = unidadesVendidas / stockActual;
                return Math.Round(resultado, 2);
            }
            catch (Exception ex)
            {
                // TODO: Migrar a ILogger cuando se implemente logging centralizado
                System.Diagnostics.Debug.WriteLine($"Error calculando rotaciÃ³n: {ex.Message}");
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
                
                // Buscamos IDs de clientes que coincidan con el nombre
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

        /// <summary>
        /// Obtiene el historial de ventas en cuenta corriente de un cliente (sÃ³lo fiado) 
        /// junto con sus detalles proyectados a texto para armar el PDF.
        /// </summary>
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

        /// <summary>
        /// Procesa una venta de forma completamente atÃ³mica usando una transacciÃ³n EF Core.
        /// Si cualquier paso falla, todos los cambios se revierten (rollback automÃ¡tico).
        /// </summary>
        public async Task<bool> ProcesarVentaAsync(Venta venta, List<VentaDetalle> detalles)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                // 1. Validar stock de todos los productos antes de hacer ningÃºn cambio
                foreach (var d in detalles)
                {
                    var producto = await _db.Productos.FindAsync(d.ProductoId)
                        ?? throw new InvalidOperationException($"Producto con ID {d.ProductoId} no encontrado.");

                    if (producto.Stock < d.Quantity)
                        throw new InvalidOperationException($"Stock insuficiente para \"{producto.Name}\". Disponible: {producto.Stock}, solicitado: {d.Quantity}.");
                }

                // 2. Descontar stock
                foreach (var d in detalles)
                {
                    var producto = await _db.Productos.FindAsync(d.ProductoId)!;
                    producto!.Stock -= d.Quantity;
                }

                // 3. Asignar nÃºmero de venta secuencial
                int maxNumero = await _db.Ventas.AnyAsync()
                    ? await _db.Ventas.MaxAsync(v => v.NumeroVenta)
                    : 0;
                venta.NumeroVenta = maxNumero + 1;

                _db.Ventas.Add(venta);

                // 4. Manejo de pago: Fiado vs Contado
                if (venta.IsFiado && venta.ClienteId.HasValue)
                {
                    var cc = await _db.CuentasCorrientes.FirstOrDefaultAsync(x => x.ClienteId == venta.ClienteId.Value)
                        ?? throw new InvalidOperationException("El cliente no tiene cuenta corriente asociada.");
                    cc.Balance += venta.Total;
                }
                else
                {
                    // Pago contado â†’ registrar ingreso financiero
                    _db.MovimientosFinancieros.Add(new MovimientoFinanciero
                    {
                        Type = TipoMovimiento.Ingreso,
                        Amount = venta.Total,
                        Description = $"Venta #{venta.NumeroVenta}",
                        VentaId = venta.Id
                    });
                }

                // 5. Guardar detalles de venta
                foreach (var d in detalles)
                {
                    d.VentaId = venta.Id;
                    _db.VentaDetalles.Add(d);
                }

                // 6. Commit atÃ³mico
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw; // Re-lanzar para que el componente muestre el error al usuario
            }
        }
        public async Task<List<HistorialPrecio>> GetHistorialPreciosAsync()
            => await _db.HistorialPrecios.OrderByDescending(h => h.FechaModificacion).ToListAsync();

        /// <summary>
        /// Registra un cambio de precio en el historial sólo cuando hay variación.
        /// </summary>
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
            await EnsureSoftDeleteColumnsAsync();
            var (umbralBaja, umbralMedia, diasSinVentaCfg) = await GetUmbralesAsync();

            var connection = _db.Database.GetDbConnection();
            bool wasClosed = connection.State == System.Data.ConnectionState.Closed;
            if (wasClosed) await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
WITH ventas12 AS (
    SELECT vd.ProductoId, SUM(vd.Quantity) AS ventas12
    FROM VentaDetalles vd
    JOIN Ventas v ON v.Id = vd.VentaId
    WHERE (v.IsDeleted = 0 OR v.IsDeleted IS NULL)
      AND v.Date >= @Desde12
    GROUP BY vd.ProductoId
),
ventas3 AS (
    SELECT vd.ProductoId, SUM(vd.Quantity) AS ventas3
    FROM VentaDetalles vd
    JOIN Ventas v ON v.Id = vd.VentaId
    WHERE (v.IsDeleted = 0 OR v.IsDeleted IS NULL)
      AND v.Date >= @Desde3
    GROUP BY vd.ProductoId
),
ventasPrev3 AS (
    SELECT vd.ProductoId, SUM(vd.Quantity) AS ventasPrev3
    FROM VentaDetalles vd
    JOIN Ventas v ON v.Id = vd.VentaId
    WHERE (v.IsDeleted = 0 OR v.IsDeleted IS NULL)
      AND v.Date < @Desde3 AND v.Date >= @Desde6
    GROUP BY vd.ProductoId
),
ultimaVenta AS (
    SELECT vd.ProductoId, MAX(v.Date) AS ultima
    FROM VentaDetalles vd
    JOIN Ventas v ON v.Id = vd.VentaId
    WHERE (v.IsDeleted = 0 OR v.IsDeleted IS NULL)
    GROUP BY vd.ProductoId
)
SELECT p.Id,
       p.Name,
       c.Name AS Categoria,
       IFNULL(v12.ventas12, 0) AS Ventas12m,
       CAST(p.Stock AS REAL) AS StockActual,
       IFNULL(uv.ultima, NULL) AS UltimaVenta,
       IFNULL(v3.ventas3, 0) AS Ventas3,
       IFNULL(vp3.ventasPrev3, 0) AS VentasPrev3,
       p.Price,
       p.PrecioCosto
FROM Productos p
LEFT JOIN Categorias c ON c.Id = p.CategoryId
LEFT JOIN ventas12 v12 ON v12.ProductoId = p.Id
LEFT JOIN ventas3 v3 ON v3.ProductoId = p.Id
LEFT JOIN ventasPrev3 vp3 ON vp3.ProductoId = p.Id
LEFT JOIN ultimaVenta uv ON uv.ProductoId = p.Id
WHERE (p.IsDeleted = 0 OR p.IsDeleted IS NULL)
";

            var desde12 = DateTime.Today.AddYears(-1);
            var desde3 = DateTime.Today.AddMonths(-3);
            var desde6 = DateTime.Today.AddMonths(-6);

            void AddParam(string name, object? value)
            {
                var p = command.CreateParameter();
                p.ParameterName = name;
                p.Value = value ?? DBNull.Value;
                command.Parameters.Add(p);
            }

            AddParam("@Desde12", desde12);
            AddParam("@Desde3", desde3);
            AddParam("@Desde6", desde6);

            var results = new List<RotacionProductoDto>();
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var stock = reader.IsDBNull(4) ? 0 : (decimal)reader.GetDouble(4);
                    var ventas12 = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                    var price = reader.IsDBNull(8) ? 0 : (decimal)reader.GetDouble(8);
                    var costo = reader.IsDBNull(9) ? 0 : (decimal)reader.GetDouble(9);
                    DateTime? ultimaVenta = reader.IsDBNull(5) ? null : reader.GetDateTime(5);

                    var rotacion = stock > 0 ? (decimal)ventas12 / Math.Max(1, stock) : 0;
                    var diasSinVenta = ultimaVenta.HasValue ? (int)(DateTime.Today - ultimaVenta.Value.Date).TotalDays : 9999;
                    var valorInmovilizado = stock * price;
                    var margenUnitario = price > 0 ? (price - costo) / price : 0;

                    var ventas3 = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                    var ventasPrev3 = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
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
                        ProductoId = reader.GetGuid(0),
                        Nombre = reader.GetString(1),
                        Categoria = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        UnidadesVendidas12m = (int)ventas12,
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
            }

            if (wasClosed) await connection.CloseAsync();

            var query = results.AsEnumerable();
            if (categoriaId.HasValue)
            {
                var categoria = await _db.Categorias.FirstOrDefaultAsync(c => c.Id == categoriaId.Value);
                if (categoria != null)
                    query = query.Where(r => string.Equals(r.Categoria, categoria.Name, StringComparison.OrdinalIgnoreCase));
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

        /// <summary>
        /// Asegura que las columnas de soft delete existan en BD antiguas antes de usar filtros.
        /// </summary>
        private async Task EnsureSoftDeleteColumnsAsync()
        {
            await EnsureColumnAsync("Ventas", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
            await EnsureColumnAsync("Productos", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        }

        private async Task EnsureColumnAsync(string table, string column, string definitionSql)
        {
            var connection = _db.Database.GetDbConnection();
            bool wasClosed = connection.State == System.Data.ConnectionState.Closed;
            if (wasClosed) await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({table});";
            using var reader = await command.ExecuteReaderAsync();
            bool found = false;
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }
            await reader.CloseAsync();

            if (!found)
            {
                command.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definitionSql};";
                await command.ExecuteNonQueryAsync();
            }

            if (wasClosed) await connection.CloseAsync();
        }
    }
}
