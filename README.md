# SistemaDeStockV3
Sistema Híbrido multiplataforma desarrollado con .NET MAUI y Blazor, orientado a facilitar el control de inventario, finanzas, clientes y ventas offline o en red local.

## Características Principales
- **Dashboard:** Visor general de métricas clave del negocio, como movimientos recientes y balances.
- **Inventario:** Control completo de stock de productos, categorías, alertas de stock mínimo automático y paginación rápida para grandes catálogos.
- **Punto de Venta:** Facturación ágil para clientes, manejando efectivo o cuenta corriente. Emisión de comprobantes PDF.
- **Cuentas Corrientes:** Cobro de deuda atrasada (Venta Fiada).
- **Finanzas:** Registro de ingresos y egresos para mantener un flujo de caja claro (integrado a punto de venta y balance general). Reportes PDF de movimientos del día.
- **Configuración:** Administración del perfil del negocio (nombre, dirección, teléfono, logo).

### Backup y Seguridad
- **Respaldo Inteligente (NUEVO):** Generación manual de copias de seguridad de todos los datos en un solo archivo físico (`stock.db`).
- **Exportación Nactiva:** Integración de FileSaver de MAUI en Windows y Android. El usuario puede guardar el archivo en su dispositivo o compartirlo a la nube.
- **Restauración en 1 clic:** Utilidad para seleccionar un backup previo `.db` para restaurarlo en reemplazo de la base actual.

## Requisitos Técnicos
- .NET 8 SDK
- Visual Studio 2022 / VS Code (con extensión .NET MAUI)
- `CommunityToolkit.Maui` (v9.0.2+)
- `SQLite` para Storage Offline Local.

## Cómo Utilizar
1. Clonar el repositorio.
2. Restaurar paquetes NuGet (incluído el de CommunityToolkit, QuestPDF para reportes, y SQLite para EF Core).
3. Compilar hacia destino (Windows Machine o Android Emulator).
4. El sistema autogenerará la base de datos `stock.db` en el Sandbox AppData directory del dispositivo al arrancar.
