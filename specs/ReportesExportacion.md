# Spec: Reportes y Exportación

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"Te voy a contar una historia. El contador del negocio necesita los datos del inventario, las ventas y las finanzas en formato Excel para analizarlos en su propia herramienta. El dueño también necesita poder reimprimir el comprobante de una venta pasada si un cliente lo pide. Todos estos reportes deben descargarse desde un lugar centralizado."

---

## 2. Objetivo

Proveer a los operadores del sistema (dueño y contador) un único punto de acceso desde donde descargar tres reportes en formato Excel (inventario, ventas, finanzas) y reimprimir el comprobante PDF de cualquier venta histórica. Esto elimina la necesidad de extraer datos manualmente de la pantalla y garantiza que el contador reciba siempre la información estructurada y lista para analizar en su propia herramienta, sin necesidad de exportaciones manuales ni acceso directo a la base de datos.

---

## 3. Alcance

### Incluye

- Exportación a `.xlsx` del reporte de Inventario (estado actual de productos, precios y stock valorizado).
- Exportación a `.xlsx` del reporte de Ventas (cronología completa de facturación con sumatoria total al pie).
- Exportación a `.xlsx` del reporte de Finanzas / Libro Diario (movimientos financieros con balance neto al pie).
- Tabla paginada de historial de ventas con buscador por número de venta o nombre de cliente, y filtro por rango de fecha (todo, última semana, último mes).
- Reimpresión (descarga) del remito PDF de cualquier venta histórica, regenerado en el mismo formato A5 con los datos originales de la venta.
- Página centralizada `/reportes` (`Reportes.razor`) accesible desde el menú de navegación principal.

### No incluye (fuera de alcance)

- Envío de reportes por email u otros canales (la descarga es siempre local).
- Filtrado por rango de fechas para los tres reportes Excel (exportan la totalidad de los datos históricos sin acotamiento temporal).
- Edición o modificación de datos desde esta pantalla.
- Generación de factura electrónica ni comprobante con validez fiscal ante la AFIP.
- Reimpresión de presupuestos (pertenece al módulo `Presupuestos`).
- Reportes globales en formato PDF (el PDF solo aplica al remito de ventas individuales).
- Programación automática de exportaciones o envío periódico de reportes.
- Filtros adicionales dentro de los reportes Excel (por categoría, por cliente, por tipo de movimiento, etc.).

---

## 4. Definiciones funcionales

### 4.1 Pantalla centralizada

Los tres botones de exportación Excel y la tabla de historial de remitos conviven en la misma página `/reportes`. No existe navegación separada: el acceso a cualquier reporte o remito parte siempre de esta pantalla.

### 4.2 Comportamiento general de los botones de exportación

- Cada botón dispara la generación del archivo en memoria y abre el diálogo nativo del OS para elegir ubicación de descarga (`FileSaver.Default.SaveAsync`).
- Durante la generación, el botón correspondiente muestra "Generando..." con spinner y todos los botones de exportación quedan deshabilitados (`isExporting = true`). No puede haber dos exportaciones simultáneas.
- Si la generación es exitosa, se muestra un toast de éxito.
- Si el usuario cancela el diálogo de guardado sin elegir ubicación, no se muestra ningún mensaje (comportamiento silencioso).
- Si ocurre un error durante la generación, se muestra un banner de error inline debajo de las tarjetas; los botones vuelven a habilitarse.

### 4.3 Reporte de Inventario

- **Columnas exportadas:** SKU, Producto, Categoría, Precio (precio de venta `Producto.Price`), Stock, Valor Total ARS (calculado como `Stock × Price`).
- Los productos se ordenan alfabéticamente por nombre antes de exportar.
- Los productos con `Stock ≤ StockMinimo` tienen la celda de Stock con fuente en rojo y negrita.
- Los productos con `IsDeleted = true` no se incluyen (filtro global EF Core de soft delete).
- Si no hay productos, el Excel se descarga con solo la fila de encabezado y sin filas de datos.

### 4.4 Reporte de Ventas

- **Columnas exportadas:** Nro. Venta, Fecha y Hora, Cliente (nombre del cliente; "Consumidor Final" si `ClienteId` es nulo), Tipo Pago ("Contado" o "Fiado (C/C)"), Total (ARS).
- Las ventas se ordenan de más reciente a más antigua.
- Al pie de la columna Total se incluye una fórmula Excel `=SUM(E2:En)` con la etiqueta "TOTAL VENTAS:" en la celda adyacente.
- Las ventas con `IsDeleted = true` no se incluyen.
- Si no hay ventas, el Excel se descarga con encabezado y fila de total en cero.

### 4.5 Reporte de Finanzas (Libro Diario)

- **Columnas exportadas:** Fecha y Hora, Concepto (`MovimientoFinanciero.Description`), Tipo ("Ingreso" o "Egreso"), Ingreso (+) / Egreso (−) (los egresos se expresan como valor negativo en la celda).
- Los movimientos se ordenan de más reciente a más antiguo.
- Al pie de la columna de monto se incluye una fórmula Excel `=SUM(D2:Dn)` con la etiqueta "BALANCE NETO:" en la celda adyacente. Los valores negativos se formatean en rojo automáticamente por el formato numérico de la celda.
- Si no hay movimientos, el Excel se descarga con encabezado y balance neto en cero.

### 4.6 Historial de Ventas (tabla de remitos)

- La tabla se muestra debajo de las tarjetas de exportación, dentro de la misma página.
- Paginación de 15 ventas por página, con el componente `AppPagination` al pie.
- Buscador en tiempo real con debounce de 400 ms; busca por número de venta (numérico) o por nombre de cliente (parcial, sin distinción de mayúsculas). Cada cambio de búsqueda reinicia la paginación a la página 1.
- Filtro de rango de fecha con tres opciones: "Todo el historial", "Última Semana" (últimos 7 días), "Último Mes" (últimos 30 días). Cambiar el filtro reinicia a la página 1.
- Las ventas con `IsDeleted = true` no aparecen.
- Si no hay resultados (ya sea porque no hay ventas o porque la búsqueda no produce coincidencias), se renderiza el estado vacío con ícono y texto "No hay ventas registradas."

### 4.7 Reimpresión de remito

- Al pulsar "Remito PDF" en una fila, el sistema carga los detalles de esa venta (`VentaDetalle`), resuelve los nombres de productos desde el diccionario activo, carga la configuración del negocio, y regenera el PDF con `PdfService.GenerarRemitoVenta`.
- El PDF resultante es idéntico en formato y diseño al remito generado en el módulo de Punto de Venta: tamaño A5, encabezado con datos del negocio, tabla de ítems (cantidad, descripción, precio unitario, subtotal), descuento si aplica, total resaltado y pie no fiscal.
- Los precios de cada ítem se toman de `VentaDetalle.UnitPrice` (precio al momento de la venta), nunca de `Producto.Price` vigente.
- Si el producto fue dado de baja con soft delete, su nombre se resuelve desde el diccionario de productos activos; si no está en el diccionario, se usa el fallback `"Producto"`. La generación del PDF no se interrumpe.
- El nombre del archivo descargado sigue el patrón `Remito_{NumeroVenta:D6}_{Date:yyyyMMdd}.pdf`.
- Mientras se genera el PDF de una fila, solo el botón de esa fila queda deshabilitado con spinner; los demás botones de la tabla no se ven afectados.

---

## 5. Datos y modelo

### Entidades involucradas

| Entidad | Campos usados | Notas |
|---|---|---|
| `Producto` | `Id`, `Name`, `SKU`, `CategoryId`, `Stock`, `StockMinimo`, `Price`, `IsDeleted` | `Price` = precio de venta. Filtro global EF Core excluye `IsDeleted = true`. |
| `Categoria` | `Id`, `Name` | Join por `CategoryId` para obtener nombre de categoría en el Excel de inventario. |
| `Venta` | `Id`, `NumeroVenta`, `Date`, `Total`, `ClienteId`, `IsFiado`, `IsDeleted` | `IsDeleted = true` excluye la venta de todos los reportes y del historial. |
| `VentaDetalle` | `Id`, `VentaId`, `ProductoId`, `Quantity`, `UnitPrice` | `UnitPrice` es el precio histórico al momento de la venta; se usa siempre en el remito. |
| `Cliente` | `Id`, `Name`, `Phone`, `Address`, `CUIT` | Opcional; puede ser `null` (Consumidor Final / Sin cliente). |
| `MovimientoFinanciero` | `Id`, `Type`, `Amount`, `Date`, `Description` | `Type`: `TipoMovimiento.Ingreso` o `TipoMovimiento.Egreso`. |
| `ConfiguracionApp` | `NombreNegocio`, `DireccionNegocio`, `Telefono` | Usada en el encabezado del remito PDF. |

### DTOs y modelos de soporte

- `RemitoVentaData`: agrupa `Venta`, `List<VentaDetalle>`, `Dictionary<Guid, string> NombreProductos`, `Cliente?`, `ConfiguracionApp`. No se persiste; se construye en memoria para cada reimpresión.
- No se crean nuevas entidades persistidas para esta funcionalidad.

### Restricciones de datos

- Los reportes Excel exportan la totalidad de los registros activos (sin filtro de fecha).
- El soft delete de `Venta` y `Producto` se aplica mediante filtro global EF Core; no requiere lógica adicional en los servicios.
- Los precios en el remito corresponden a `VentaDetalle.UnitPrice` y son inmutables respecto al momento de la venta.

---

## 6. UX / Interfaz

### Pantalla `/reportes`

**Estado de carga inicial:** spinner centrado mientras se inicializa la pantalla (carga de clientes y primera página de ventas).

**Sección superior — Tres tarjetas de exportación:**
- Grilla de 3 columnas en desktop, colapsa a 1 columna en mobile.
- Cada tarjeta: ícono temático grande, título (Inventario / Historial de Ventas / Movimientos Financieros), descripción breve, botón "Exportar Excel".
- Durante exportación activa: botón propio en estado "Generando..." con spinner; los tres botones deshabilitados.

**Mensajes de estado:**
- Éxito: toast del sistema vía `NotificationService.Success(...)`.
- Error de generación: banner inline con ícono de advertencia, debajo de las tarjetas. Se autodestruye a los 5 segundos.
- Cancelación del diálogo: sin mensaje.

**Sección inferior — Historial de Ventas:**
- Encabezado con título "Historial de Ventas", subtítulo y controles de filtro alineados a la derecha (selector de rango + campo de búsqueda con ícono de lupa).
- Estado de carga de la tabla: spinner centrado en la tarjeta.
- Estado vacío: tarjeta con ícono de recibo y texto "No hay ventas registradas."
- Tabla con columnas: N° Venta (monoespaciado, formato `#000001`), Fecha y Hora (`dd/MM/yyyy HH:mm`), Cliente (nombre o "Sin cliente" en itálica), Tipo (badge coloreado "Fiado" en amarillo o "Contado" en verde), Total (alineado a la derecha), Remito (botón por fila).
- Paginación con `AppPagination` al pie de la tabla.

### Flujo de descarga Excel (cualquiera de los tres reportes)

1. Clic en "Exportar Excel".
2. Generación en memoria (se muestra "Generando...").
3. Diálogo nativo del OS para elegir ubicación y nombre de archivo.
4a. Usuario elige ubicación → archivo guardado → toast de éxito.
4b. Usuario cancela → sin mensaje.
5. Botones vuelven a estado activo.

### Flujo de reimpresión de remito

1. Usuario localiza la venta (buscador, filtro de rango o paginación).
2. Clic en "Remito PDF" en la fila correspondiente.
3. Sistema carga detalles, regenera PDF A5.
4. Diálogo nativo del OS para elegir ubicación.
5a. Usuario elige ubicación → PDF guardado → toast de éxito.
5b. Error → toast de error; botón vuelve a estado normal.

---

## 7. Definiciones técnicas

### Stack y librerías

- **Framework:** .NET 8 MAUI Blazor Hybrid.
- **Componente Razor:** `Components/Pages/Reportes.razor`.
- **Generación Excel:** `ClosedXML` → `ReportService` (inyectado como `@inject ReportService Reports`).
- **Generación PDF:** `QuestPDF` (licencia Community) → `PdfService` (inyectado como `@inject PdfService PdfSvc`).
- **Descarga de archivos:** `CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync`, siempre invocado en el hilo principal mediante `MainThread.InvokeOnMainThreadAsync`.
- **Acceso a datos:** `DataService` (inyectado como `@inject DataService Data`) con EF Core; filtro global de soft delete configurado a nivel de `DbContext`.
- **Notificaciones:** `NotificationService` (inyectado como `@inject NotificationService Notifications`) para toasts.

### Generación de archivos en memoria

```csharp
// Patrón común para los tres reportes Excel
byte[] bytes = Reports.Generate*Report(...);
using var stream = new MemoryStream(bytes);
var result = await MainThread.InvokeOnMainThreadAsync(async () =>
    await FileSaver.Default.SaveAsync(fileName, stream));

// Patrón para remito PDF
var remitoData = new RemitoVentaData { Venta = v, Detalles = detalles,
    NombreProductos = nombreProductos, Cliente = cliente, Config = config };
byte[] pdfBytes = PdfSvc.GenerarRemitoVenta(remitoData);
```

### Resolución de nombre de producto en remito

```csharp
// Se construye un diccionario desde los productos activos
var nombreProductos = productos.ToDictionary(p => p.Id, p => p.Name);
// Uso en PdfService (fallback a "Producto" si el ID no existe)
var nombre = data.NombreProductos.TryGetValue(d.ProductoId, out var n) ? n : "Producto";
```

### Paginación y búsqueda del historial

- `DataService.GetTotalVentasAsync(search, dateRange)` → total de registros para `AppPagination`.
- `DataService.GetVentasPaginadasAsync(page, pageSize, search, dateRange)` → página actual.
- Debounce: `System.Timers.Timer` de 400 ms; se reinicia con cada keystroke. Al disparar, resetea `currentPage = 1` y llama a `LoadCurrentPage()`.
- Cambio de selector de rango: resetea `currentPage = 1` y llama a `LoadCurrentPage()`.

### Nombres de archivo generados

| Reporte | Patrón de nombre |
|---|---|
| Inventario | `Reporte_Inventario_yyyyMMdd_HHmm.xlsx` |
| Ventas | `Reporte_Ventas_yyyyMMdd_HHmm.xlsx` |
| Finanzas | `Reporte_Finanzas_yyyyMMdd_HHmm.xlsx` |
| Remito PDF | `Remito_{NumeroVenta:D6}_{Date:yyyyMMdd}.pdf` |

### Formato visual de celdas Excel

| Reporte | Celda / rango | Formato aplicado |
|---|---|---|
| Inventario | Precio, Valor Total | `$ #,##0.00` |
| Inventario | Stock con `Stock ≤ StockMinimo` | Fuente roja + negrita |
| Inventario | Encabezado A1:F1 | Fondo azul AirForce, fuente blanca, negrita |
| Ventas | Total (columna E) | `$ #,##0.00` |
| Ventas | Encabezado A1:E1 | Fondo verde oscuro, fuente blanca, negrita |
| Finanzas | Monto (columna D) | `$ #,##0.00;[Red]-$ #,##0.00` |
| Finanzas | Encabezado A1:D1 | Fondo azul oscuro, fuente blanca, negrita |

---

## 8. Seguridad y permisos

- El sistema no implementa autenticación multiusuario en esta versión; cualquier usuario con acceso a la aplicación instalada puede acceder a la pantalla de reportes y descargar cualquier archivo.
- No existe distinción de roles entre "dueño" y "contador" dentro de la aplicación: ambos acceden a los mismos datos y funcionalidades.
- Los archivos se generan en memoria y se descargan localmente en el equipo donde corre la aplicación; no se transmiten a servidores externos.
- Las ventas y productos con `IsDeleted = true` son excluidos a nivel de consulta EF Core (filtro global) y no son accesibles desde ninguna exportación ni desde el historial de remitos.
- Los remitos PDF regenerados usan datos históricos de la DB local; no existe exposición de datos a servicios externos.

---

## 9. Criterios de aceptación

### Exportación de Inventario

- [ ] Dado que hay productos activos registrados, cuando el usuario hace clic en "Exportar Excel" de Inventario, entonces se abre el diálogo de guardado del OS con un archivo `.xlsx` válido que contiene exactamente las columnas SKU, Producto, Categoría, Precio, Stock y Valor Total ARS.
- [ ] Dado que un producto tiene `Stock ≤ StockMinimo`, cuando se abre el Excel exportado, entonces la celda de Stock de ese producto muestra el valor con fuente roja y negrita.
- [ ] Dado que no hay productos activos (o todos están con `IsDeleted = true`), cuando el usuario exporta el inventario, entonces el archivo descargado contiene solo la fila de encabezado sin filas de datos y la operación finaliza sin error.
- [ ] Dado que el usuario hace clic en "Exportar Excel" de Inventario, cuando la generación está en progreso, entonces el botón muestra "Generando..." con spinner y los tres botones de exportación están deshabilitados hasta que la operación finaliza.

### Exportación de Ventas

- [ ] Dado que hay ventas activas registradas, cuando el usuario exporta el reporte de Ventas, entonces el Excel contiene las columnas Nro. Venta, Fecha y Hora, Cliente, Tipo Pago, Total (ARS) y una fila al pie con la fórmula `=SUM(...)` rotulada "TOTAL VENTAS:".
- [ ] Dado que una venta no tiene cliente asignado, cuando aparece en el Excel de Ventas, entonces la columna Cliente muestra "Consumidor Final".
- [ ] Dado que una venta tiene `IsDeleted = true`, cuando el usuario exporta el reporte de Ventas, entonces esa venta no aparece en el archivo Excel.
- [ ] Dado que no hay ventas activas, cuando el usuario exporta el reporte de Ventas, entonces el archivo se descarga con encabezado y fila de total en cero, sin filas de datos.

### Exportación de Finanzas

- [ ] Dado que hay movimientos financieros registrados, cuando el usuario exporta el reporte de Finanzas, entonces el Excel contiene las columnas Fecha y Hora, Concepto, Tipo, Ingreso (+) / Egreso (−) y una fila al pie con la fórmula `=SUM(...)` rotulada "BALANCE NETO:".
- [ ] Dado que un movimiento es de tipo Egreso, cuando se abre el Excel exportado, entonces su valor en la columna de monto es negativo y se muestra en formato rojo.
- [ ] Dado que no hay movimientos registrados, cuando el usuario exporta el reporte de Finanzas, entonces el archivo se descarga con encabezado y balance neto en cero, sin filas de datos.

### Historial de Ventas

- [ ] Dado que hay ventas activas, cuando el usuario accede a `/reportes`, entonces la tabla del historial muestra las ventas paginadas de a 15 con las columnas N° Venta, Fecha y Hora, Cliente, Tipo, Total y botón "Remito PDF".
- [ ] Dado que el usuario escribe texto en el buscador, cuando transcurren 400 ms desde el último keystroke, entonces la tabla muestra solo las ventas cuyo número de venta o nombre de cliente contienen el texto ingresado, y la paginación se reinicia a la página 1.
- [ ] Dado que la búsqueda activa no produce coincidencias, cuando termina el debounce, entonces la tabla muestra el estado vacío con ícono de recibo y texto "No hay ventas registradas."
- [ ] Dado que el usuario selecciona "Última Semana" en el filtro de rango, cuando se aplica el filtro, entonces solo aparecen ventas con `Date` dentro de los últimos 7 días y la paginación se reinicia a la página 1.
- [ ] Dado que una venta tiene `IsDeleted = true`, cuando el usuario navega el historial o realiza una búsqueda, entonces esa venta no aparece en ninguna página ni en los resultados.

### Reimpresión de Remito

- [ ] Dado que el usuario hace clic en "Remito PDF" de una venta, cuando la generación concluye, entonces se descarga un PDF en formato A5 con encabezado del negocio, tabla de ítems (cantidad, descripción, precio unitario, subtotal), total resaltado y pie con la leyenda "No válido como comprobante fiscal".
- [ ] Dado que el PDF se genera para una venta histórica, cuando se abre el archivo descargado, entonces el precio unitario de cada ítem corresponde a `VentaDetalle.UnitPrice` (precio al momento de la venta) y no al precio de lista vigente del producto.
- [ ] Dado que un producto incluido en una venta histórica fue eliminado con soft delete, cuando se genera el remito de esa venta, entonces el ítem aparece en el PDF (con nombre desde el diccionario de activos o con el literal "Producto" como fallback) y el PDF se genera sin arrojar excepción.
- [ ] Dado que el usuario hace clic en "Remito PDF", cuando la generación está en progreso, entonces solo el botón de esa fila muestra spinner y queda deshabilitado; los demás botones "Remito PDF" de la tabla permanecen activos.
- [ ] Dado que la generación del PDF falla por una excepción, cuando ocurre el error, entonces se muestra un toast de error con el mensaje correspondiente y el botón de la fila vuelve a su estado normal.
- [ ] Dado que el nombre del archivo descargado sigue el patrón `Remito_NNNNNN_yyyyMMdd.pdf`, cuando el usuario guarda el archivo, entonces el nombre pre-cargado en el diálogo del OS corresponde al número de venta con 6 dígitos y la fecha de la venta.

---

## 10. Casos borde y manejo de errores

- **Excel sin datos:** si no hay registros activos para la entidad consultada, el archivo se descarga igualmente con solo la fila de encabezado (más la fila de total/balance en cero para ventas y finanzas). No se bloquea la descarga ni se muestra error al usuario.

- **Fallo en generación de archivo Excel:** la excepción se captura en el bloque `catch` de `ExportarInventario` / `ExportarVentas` / `ExportarFinanzas`. Se llama a `ShowError(ex.Message)` que muestra el banner inline, y el bloque `finally` garantiza que `isExporting = false` y `currentExport = ""` se restauran siempre, re-habilitando los botones.

- **Fallo en generación del PDF de remito:** la excepción se captura en `ImprimirRemitoAsync`. Se llama a `Notifications.Error(...)` con el mensaje. El bloque `finally` garantiza que `printingVentaId = null` se restaura siempre, re-habilitando el botón de esa fila.

- **Producto eliminado en venta histórica:** el diccionario `NombreProductos` se construye desde los productos activos. Si el `ProductoId` del `VentaDetalle` no existe en el diccionario, `TryGetValue` devuelve `false` y se usa la cadena `"Producto"` como fallback. El PDF se genera sin excepción; la información es parcial pero el documento es funcional y descargable.

- **Venta sin cliente:** en la tabla del historial se muestra "Sin cliente" en itálica y color gris. En el Excel de ventas la columna Cliente muestra "Consumidor Final". En el remito PDF la sección de cliente no se renderiza.

- **Búsqueda sin resultados:** `totalVentas == 0` con filtro activo → se renderiza el estado vacío. No es un error; no se muestra ningún banner de error.

- **Cancelación del diálogo nativo de guardado:** `result.IsSuccessful == false` sin excepción (el usuario canceló sin elegir ubicación) → sin mensaje de error ni de éxito. El usuario simplemente no obtuvo el archivo.

- **Doble clic en exportar:** el botón queda deshabilitado con `disabled="@isExporting"` durante toda la exportación activa. No puede dispararse una segunda exportación simultánea.

- **Venta fiada en remito:** el remito muestra el badge "FIADO" en rojo junto a los datos del cliente, y la fila de total indica "TOTAL (Cuenta Corriente)" en lugar de "TOTAL (Contado)".

- **Descuento en remito:** si `Detalles.Sum(UnitPrice × Quantity) > Venta.Total`, el remito muestra automáticamente una fila de "Subtotal" y una fila de "Descuento" antes del total final.

---

## 11. Preguntas abiertas

- **Valor inmovilizado en el reporte de inventario:** la historia de usuario especifica `Stock × PrecioCosto` (costo de adquisición), pero la implementación actual en `ReportService.GenerateInventoryReport` calcula `Stock × Price` (precio de venta). ¿Se debe corregir para que refleje el costo real de inventario o se mantiene el precio de venta como base del cálculo? Esta decisión impacta directamente en la utilidad del reporte para el contador.

- **Nombre del producto en remito cuando el producto fue eliminado:** el fallback actual `"Producto"` puede resultar poco informativo si el contador necesita auditar esa venta. ¿Se debería persistir el nombre del producto directamente en `VentaDetalle` al momento de la venta para preservarlo indefinidamente, independientemente del soft delete?

- **Filtro de fechas para reportes Excel:** actualmente los tres reportes exportan todo el historial. ¿Se requerirá en el futuro un selector de rango de fechas para acotar el período exportado (ej: "exportar ventas de enero 2026")?

- **Permisos diferenciados:** la historia menciona dos actores con necesidades distintas (contador y dueño). ¿Se planea implementar autenticación o roles en versiones futuras que restrinjan el acceso a ciertos reportes según el perfil del usuario?
