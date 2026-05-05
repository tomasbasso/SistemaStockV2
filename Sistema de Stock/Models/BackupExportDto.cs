using System;
using System.Collections.Generic;

namespace Sistema_de_Stock.Models
{
    public class BackupExportDto
    {
        public DateTime ExportDate { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "2.0";
        public Guid TenantId { get; set; }

        public List<ConfiguracionApp> Configuraciones { get; set; } = new();
        public List<Categoria> Categorias { get; set; } = new();
        public List<Producto> Productos { get; set; } = new();
        public List<Cliente> Clientes { get; set; } = new();
        public List<CuentaCorriente> CuentasCorrientes { get; set; } = new();
        public List<MovimientoFinanciero> MovimientosFinancieros { get; set; } = new();
        public List<Venta> Ventas { get; set; } = new();
        public List<VentaDetalle> VentaDetalles { get; set; } = new();
        public List<Presupuesto> Presupuestos { get; set; } = new();
        public List<PresupuestoDetalle> PresupuestoDetalles { get; set; } = new();
        public List<HistorialPrecio> HistorialPrecios { get; set; } = new();
    }
}
