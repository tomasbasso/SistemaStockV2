# Spec: Punto de Venta (POS)

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El vendedor del negocio necesita registrar ventas de forma rápida. Puede escanear el código de barras de un producto con un lector físico o buscarlo por nombre/SKU, agregarlo al carrito, ajustar cantidades, y cuando termina de armar el pedido, cobra: puede cobrar en efectivo (contado) o fiarlo a la cuenta corriente de un cliente registrado. También puede aplicar descuentos. Al finalizar, el sistema genera automáticamente un remito en PDF que el vendedor puede imprimir o guardar."

---

## 2. Objetivo

Proveer al vendedor una pantalla unificada para registrar ventas de forma ágil, minimizando la fricción entre la carga de productos, el cobro y la emisión del comprobante. El sistema debe garantizar la integridad del stock y de los movimientos financieros en todo momento, incluso ante condiciones concurrentes, y generar automáticamente un remito PDF en formato A5 al completar cada operación.

---

## 3. Alcance

### Incluye

- Búsqueda de productos por nombre, SKU y código de barras (EAN y otros formatos)
- Compatibilidad con lectores de código de barras físicos (modo HID / keyboard wedge)
- Carrito de compra con controles de cantidad incrementales y edición directa
- Validación de stock disponible al agregar ítems y al confirmar la venta
- Modal de checkout con selección de cliente, método de pago (contado o fiado), y aplicación de descuento porcentual o monto fijo
- Procesamiento de la venta dentro de una transacción EF Core (BeginTransactionAsync / CommitAsync / RollbackAsync)
- Reducción de stock al confirmar la venta
- Generación de MovimientoFinanciero de tipo Ingreso para ventas contado
- Incremento del Balance de CuentaCorriente para ventas fiadas
- Generación de remito PDF en tamaño A5 usando QuestPDF, con opción de guardar localmente mediante FileSaver nativo
- Número de venta auto-incremental (NumeroVenta)
- Soft delete en la entidad Venta (IsDeleted = true para anulaciones futuras, no implementado en este alcance)

### No incluye (fuera de alcance)

- Anulación o devolución de ventas (requiere spec separada)
- Impresión directa a impresora térmica o de red (solo guardar PDF localmente)
- Emisión de comprobantes fiscales (factura A/B/C, tique fiscal); el remito no tiene validez fiscal
- Integración con medios de pago electrónicos (tarjeta, QR, transferencia)
- Descuentos por cliente o listas de precios diferenciadas
- Múltiples métodos de pago en una misma venta (pago mixto contado + fiado)
- Modificación de precios desde el POS
- Gestión de turnos de caja o cierre de caja
- Sincronización multi-terminal en tiempo real (la validación de stock se hace en la transacción, no hay WebSocket)

---

## 4. Definiciones funcionales

### Búsqueda y selección de productos

- Al cargar la página (`OnAfterRenderAsync`, primer render), el cursor se enfoca automáticamente en el campo de búsqueda.
- El buscador filtra en tiempo real (evento `oninput`) por nombre, SKU y código de barras del producto, usando comparación case-insensitive.
- Al presionar Enter en el buscador:
  1. Se busca **coincidencia exacta** de código de barras o SKU con el texto ingresado.
     - Si hay coincidencia exacta y el producto tiene stock > 0 → se agrega al carrito, se muestra notificación de éxito y se limpia el buscador.
     - Si hay coincidencia exacta pero stock = 0 → se muestra notificación de advertencia "Sin stock: [nombre]". No se agrega.
  2. Si no hay coincidencia exacta, se evalúa el listado filtrado actual:
     - Si hay exactamente 1 resultado en la grilla y tiene stock > 0 → se agrega automáticamente al carrito y se limpia el buscador.
     - Si hay exactamente 1 resultado pero stock = 0 → no se agrega.
     - Si hay 0 o más de 1 resultado → no ocurre nada automático; el vendedor puede hacer clic en la tarjeta.
- Los productos con stock = 0 se muestran con opacidad reducida (`opacity-50`) y el clic está bloqueado (`cursor-not-allowed`).
- Los productos con stock > 0 muestran un botón "+" en hover que también agrega al carrito.

### Carrito de compra

- Agregar un producto ya presente en el carrito incrementa la cantidad en 1, siempre que no se exceda el stock disponible (campo `Stock` del modelo).
- La cantidad de cada ítem se puede ajustar con botones "+" / "−" o escribiendo directamente en el input numérico.
- Al decrementar a 0 con el botón "−", el ítem se elimina del carrito.
- Al editar la cantidad directamente: si el valor ingresado supera el stock disponible, se ajusta al máximo disponible y se muestra advertencia. Si el valor es <= 0 o no es un número válido, no se actualiza.
- Cada ítem muestra: nombre, precio unitario, controles de cantidad, y botón de eliminar (icono papelera).
- El total del carrito (subtotal) se recalcula reactivamente en cada cambio.
- El botón "Cancelar Venta" limpia todos los ítems del carrito.
- El botón "Cobrar" está deshabilitado si el carrito está vacío.

### Modal de checkout

- Al abrir el modal, los campos se resetean: cliente = Consumidor Final, método de pago = Contado, descuento = 0.
- **Selección de cliente:** selector con opción por defecto "Consumidor Final" (Guid.Empty) más todos los clientes registrados activos, ordenados alfabéticamente.
- **Método de pago:**
  - **Contado:** disponible siempre. Genera un `MovimientoFinanciero` de tipo `Ingreso` con monto igual al total final (descontado) y la descripción "Venta #[NumeroVenta]", asociado a la venta mediante `VentaId`.
  - **Fiado (Cuenta Corriente):** solo disponible cuando hay un cliente seleccionado que no sea Consumidor Final. Si se selecciona Consumidor Final mientras el método es Fiado, se revierte automáticamente a Contado. Incrementa el `Balance` de la `CuentaCorriente` del cliente en el monto total final.
- **Descuento:**
  - Tipo porcentual (%): el valor ingresado se interpreta como porcentaje entre 0 y 100. El monto de descuento = `round(CartTotal * clamp(valor, 0, 100) / 100, 2)`.
  - Tipo monto fijo ($): el monto de descuento = `clamp(valor, 0, CartTotal)`. No puede superar el total del carrito.
  - El total final (`FinalTotal`) = CartTotal − DiscountAmount, calculado reactivamente.
  - Si el descuento resulta en total = 0, la venta se puede confirmar con total $0.
  - El descuento se guarda implícitamente: el campo `Venta.Total` almacena el `FinalTotal`. La diferencia entre la suma de los `VentaDetalle` (subtotal de ítems) y `Venta.Total` permite al remito inferir el descuento.
- Mientras se procesa la venta (`isProcessingSale = true`), el botón "Confirmar Venta" muestra spinner y está deshabilitado para evitar doble envío.

### Procesamiento de la venta (transacción)

El método `DataService.ProcesarVentaAsync` ejecuta las siguientes operaciones dentro de una única transacción EF Core:

1. **Validación de stock (pre-reducción):** para cada detalle, se verifica que el stock del producto sea suficiente. Si cualquier producto tiene stock insuficiente, se lanza excepción y se hace rollback.
2. **Reducción de stock:** se resta la cantidad vendida del stock de cada producto.
3. **Asignación de NumeroVenta:** se toma `MAX(NumeroVenta)` de la tabla Ventas y se suma 1. Si no hay ventas previas, se inicia en 1.
4. **Inserción de la Venta** en la base de datos.
5. **Efecto financiero** según método de pago:
   - Fiado: se busca la `CuentaCorriente` del cliente y se suma el total al `Balance`.
   - Contado: se inserta un `MovimientoFinanciero` de tipo `Ingreso`.
6. **Inserción de los VentaDetalle** con referencia al `VentaId`.
7. **Commit** de la transacción.

Si cualquier paso falla, se ejecuta `RollbackAsync` y se re-lanza la excepción. El componente captura el error y muestra una notificación al usuario, cerrando el modal de checkout.

### Finalización y remito PDF

- Tras un procesamiento exitoso:
  1. Se cierra el modal de checkout.
  2. Se limpia el carrito.
  3. Se recarga el catálogo de productos (para reflejar el stock actualizado).
  4. Se abre el modal de éxito mostrando el número de venta formateado como D6.
- Desde el modal de éxito, el vendedor puede:
  - **Imprimir Remito PDF:** genera el PDF y abre el diálogo nativo de guardado (`FileSaver.Default.SaveAsync`). El nombre del archivo tiene el formato `Remito_[NumeroVenta:D6]_[Fecha:yyyyMMdd].pdf`.
  - **Nueva Venta:** cierra el modal de éxito y deja el POS listo para la siguiente operación (buscador ya enfocado tras el re-render).
- El remito PDF (tamaño A5, generado por QuestPDF) incluye:
  - **Encabezado:** nombre del negocio, dirección y teléfono (de `ConfiguracionApp`), bloque "REMITO" con número D6 y fecha.
  - **Datos del cliente** (si no es Consumidor Final): nombre, teléfono, dirección, CUIT. Si la venta es fiada, se muestra la etiqueta "FIADO" en rojo.
  - **Tabla de ítems:** columnas Cantidad, Descripción, Precio Unitario, Subtotal.
  - **Sección de descuento** (solo si `subtotalItems > Venta.Total`): fila de subtotal + fila de descuento en amarillo.
  - **Total final** con etiqueta "TOTAL (Contado)" o "TOTAL (Cuenta Corriente)" según método de pago.
  - **Bloque de firma y aclaración** al pie del contenido.
  - **Pie de página:** fecha y hora de generación + leyenda "No válido como comprobante fiscal".
- Si el guardado del remito falla (usuario cancela el diálogo o error de escritura), se muestra notificación de error pero la venta ya fue confirmada y no se revierte.

---

## 5. Datos y modelo

### Entidades involucradas

| Entidad | Campos clave usados en POS | Notas |
|---|---|---|
| `Producto` | `Id`, `Name`, `SKU`, `CodigoBarras`, `Stock`, `Price` | `IsDeleted` filtrado globalmente por EF Core |
| `Cliente` | `Id`, `Name`, `Phone`, `Address`, `CUIT` | Solo clientes activos (`IsDeleted = false`) |
| `CuentaCorriente` | `ClienteId`, `Balance` | Balance positivo = deuda del cliente. Se crea automáticamente al dar de alta un cliente |
| `Venta` | `Id`, `NumeroVenta`, `Date`, `Total`, `ClienteId`, `IsFiado`, `IsDeleted` | `Total` almacena el monto final ya descontado |
| `VentaDetalle` | `Id`, `VentaId`, `ProductoId`, `Quantity`, `UnitPrice` | `UnitPrice` es el precio al momento de la venta; no se actualiza si el precio del producto cambia después |
| `MovimientoFinanciero` | `Id`, `Type`, `Amount`, `Description`, `VentaId`, `Date` | Solo se crea para ventas contado; `Type = TipoMovimiento.Ingreso` |
| `ConfiguracionApp` | `NombreNegocio`, `DireccionNegocio`, `Telefono` | Usada para el encabezado del remito PDF |

### Tipos y restricciones

- `Venta.Total`: `decimal`, debe ser >= 0 (puede ser 0 si el descuento cubre el total).
- `CuentaCorriente.Balance`: `decimal` almacenado en SQLite. Balance positivo = deuda; negativo = saldo a favor del cliente.
- `Producto.Stock`: `int`, nunca puede quedar negativo (la transacción falla antes).
- `VentaDetalle.UnitPrice`: captura el precio de venta en el momento de la transacción; es inmutable una vez guardado.
- `Venta.NumeroVenta`: `int` auto-incremental dentro de la transacción (MAX + 1); no usa secuencia de base de datos.

### DTO de remito (sin persistencia)

```csharp
public class RemitoVentaData
{
    public Venta Venta { get; set; }
    public List<VentaDetalle> Detalles { get; set; }
    public Dictionary<Guid, string> NombreProductos { get; set; }
    public Cliente? Cliente { get; set; }
    public ConfiguracionApp Config { get; set; }
}
```

---

## 6. UX / Interfaz

### Layout general

La página (`/pos` o `/ventas`) se divide en dos columnas (responsiva):

- **Columna izquierda (flexible):** catálogo de productos con buscador y grilla de tarjetas.
- **Columna derecha (ancho fijo ~384px en escritorio):** carrito de la venta actual, resumen de total y botones de acción.

La altura total ocupa el viewport disponible (`calc(100vh - 80px)`) sin scroll de página; cada columna tiene scroll interno independiente.

### Estados de la pantalla principal

| Estado | Descripción visual |
|---|---|
| Carga inicial | Spinner centrado en la columna de productos |
| Sin resultados en búsqueda | Placeholder con ícono de lupa y texto "No se encontraron productos. Prueba con otra búsqueda." |
| Carrito vacío | Placeholder con ícono de carrito y texto "Agrega productos para comenzar" |
| Producto sin stock | Tarjeta con opacidad 50%, badge rojo con stock, cursor bloqueado |
| Producto con stock | Tarjeta activa, badge verde, botón "+" visible en hover |

### Flujo principal del vendedor

1. El vendedor llega al POS: cursor en buscador.
2. Escanea un código de barras (el lector envía el código como texto + Enter) o tipea y presiona Enter.
3. El producto se agrega al carrito con notificación toast de éxito.
4. Repite para todos los productos.
5. Ajusta cantidades si es necesario.
6. Hace clic en "Cobrar $[total]".
7. En el modal de checkout: (optativo) selecciona cliente → elige método de pago → (optativo) aplica descuento.
8. Hace clic en "Confirmar Venta".
9. Modal de éxito aparece con número de venta.
10. Hace clic en "Imprimir Remito PDF" → diálogo de guardado → guarda el archivo.
11. Hace clic en "Nueva Venta" para volver al POS limpio.

### Modal de checkout — estados

- **Contado seleccionado, sin cliente:** estado normal por defecto.
- **Fiado deshabilitado:** cuando el selector está en Consumidor Final, el botón de Fiado aparece con opacidad 50% y no es seleccionable.
- **Fiado habilitado:** cuando hay un cliente real seleccionado.
- **Procesando:** botón "Confirmar Venta" reemplazado por spinner + texto "Procesando..."; deshabilitado.

### Modal de éxito — estados

- **Remito no generado aún:** botón "Imprimir Remito PDF" activo.
- **Generando remito:** botón reemplazado por spinner + texto "Generando..."; deshabilitado.
- **Remito generado / error:** vuelve al estado normal (el resultado se comunica mediante toast).

---

## 7. Definiciones técnicas

### Stack

- **Framework:** .NET 8 MAUI + Blazor Hybrid
- **ORM:** Entity Framework Core con SQLite (acceso directo a `StockDbContext`)
- **Generación de PDF:** QuestPDF (licencia Community), tamaño A5
- **Guardado de archivos:** `CommunityToolkit.Maui.Storage.FileSaver` (invocado en `MainThread`)
- **Notificaciones:** `NotificationService` (toast de éxito, advertencia y error)

### Componente principal

- Archivo: `SistemaDeStockV3/Components/Pages/PuntoDeVenta.razor`
- Rutas: `/pos` y `/ventas`
- Servicios inyectados: `DataService`, `PdfService`, `NotificationService`

### Patrón de transacción

```csharp
await using var transaction = await _db.Database.BeginTransactionAsync();
try
{
    // 1. Validar stock de todos los ítems
    // 2. Reducir stock
    // 3. Asignar NumeroVenta (MAX + 1)
    // 4. Insertar Venta
    // 5. Efecto financiero (Fiado: Balance++ | Contado: MovimientoFinanciero)
    // 6. Insertar VentaDetalles
    await _db.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### Filtrado de productos eliminados

EF Core aplica el filtro `IsDeleted = false` globalmente para `Producto`. El POS no necesita lógica adicional: los productos eliminados nunca aparecen en el catálogo.

### Búsqueda en memoria

Los productos se cargan todos en memoria al inicializar (`GetProductosAsync`). El filtrado del buscador es LINQ en memoria (no query adicional a SQLite). Esto es correcto para catálogos de tamaño habitual en un negocio pequeño/mediano (< 10.000 productos).

### Generación del remito

El método `PdfService.GenerarRemitoVenta(RemitoVentaData)` devuelve `byte[]`. El componente crea un `MemoryStream` y lo pasa a `FileSaver.Default.SaveAsync`. La operación de guardado se ejecuta en `MainThread` para compatibilidad con MAUI.

### Inferencia del descuento en el remito

El remito no almacena el tipo ni el valor del descuento. Infiere si hubo descuento comparando:
`descuento = sum(VentaDetalle.UnitPrice * Quantity) - Venta.Total`
Si `descuento > 0`, muestra la fila de subtotal y la fila de descuento.

---

## 8. Seguridad y permisos

- El sistema no implementa autenticación de usuario en la versión actual; es una aplicación de escritorio/local de uso individual o con acceso físico controlado.
- No hay roles diferenciados en el POS: cualquier operador con acceso a la aplicación puede registrar ventas, aplicar descuentos y generar remitos.
- El acceso a la cuenta corriente de un cliente desde el POS está restringido funcionalmente: solo es posible si hay un cliente seleccionado (no Consumidor Final); la validación ocurre tanto en la UI (botón deshabilitado) como en el servicio (`throw InvalidOperationException` si no hay `CuentaCorriente`).
- Los archivos PDF se guardan en la ruta elegida por el usuario mediante el diálogo nativo del sistema operativo; la aplicación no tiene acceso al sistema de archivos fuera de ese diálogo.

---

## 9. Criterios de aceptación

### Búsqueda y carga de productos

- [ ] Dado que la página cargó, cuando el renderizado inicial termina, entonces el cursor está posicionado en el campo de búsqueda sin acción del usuario.
- [ ] Dado que hay un producto con código de barras "7790001234567" y stock > 0, cuando el vendedor escanea ese código y el lector envía el texto + Enter, entonces el producto se agrega al carrito, aparece una notificación de éxito y el campo de búsqueda queda vacío.
- [ ] Dado que hay un producto con código de barras "7790001234567" y stock = 0, cuando el vendedor escanea ese código, entonces no se agrega al carrito y aparece una notificación de advertencia "Sin stock: [nombre]".
- [ ] Dado que la búsqueda filtra exactamente 1 producto con stock > 0, cuando el vendedor presiona Enter, entonces ese producto se agrega al carrito.
- [ ] Dado que la búsqueda filtra 2 o más productos, cuando el vendedor presiona Enter, entonces no se agrega ninguno automáticamente y la grilla permanece visible.
- [ ] Dado que un producto tiene stock = 0, cuando aparece en la grilla, entonces su tarjeta está con opacidad reducida, el clic no produce ningún efecto y el badge de stock es rojo.

### Carrito

- [ ] Dado que el producto X ya está en el carrito con cantidad 3 y su stock disponible es 5, cuando el vendedor lo agrega nuevamente (clic en tarjeta o Enter), entonces la cantidad pasa a 4.
- [ ] Dado que el producto X está en el carrito con cantidad igual a su stock disponible, cuando el vendedor intenta incrementar la cantidad, entonces la cantidad no aumenta y no se agrega una nueva unidad.
- [ ] Dado que hay un ítem en el carrito con cantidad 1, cuando el vendedor presiona "−", entonces el ítem se elimina del carrito.
- [ ] Dado que el vendedor ingresa manualmente una cantidad mayor al stock disponible, cuando confirma la entrada, entonces la cantidad se ajusta al máximo de stock y se muestra una advertencia.
- [ ] Dado que el carrito está vacío, entonces el botón "Cobrar" está deshabilitado.
- [ ] Dado que hay ítems en el carrito, cuando el vendedor hace clic en "Cancelar Venta", entonces el carrito queda vacío.

### Checkout — método de pago

- [ ] Dado que el selector de cliente está en "Consumidor Final", entonces el botón "Fiado (C/C)" está deshabilitado y no es seleccionable.
- [ ] Dado que se selecciona un cliente real y luego se vuelve a "Consumidor Final", entonces el método de pago se revierte automáticamente a "Contado".
- [ ] Dado que el método de pago es "Contado" y la venta se confirma, entonces se crea un `MovimientoFinanciero` de tipo `Ingreso` con el monto igual al total final y la descripción "Venta #[NumeroVenta]".
- [ ] Dado que el método de pago es "Fiado" con un cliente válido y la venta se confirma, entonces el `Balance` de la `CuentaCorriente` del cliente se incrementa en el monto total final y no se crea ningún `MovimientoFinanciero`.

### Checkout — descuento

- [ ] Dado que el tipo de descuento es "%" y el vendedor ingresa 10 con un carrito total de $1000, entonces el descuento mostrado es $100 y el total final es $900.
- [ ] Dado que el tipo de descuento es "%" y el vendedor ingresa 150, entonces el descuento se calcula como si fuera 100% y el total final es $0.
- [ ] Dado que el tipo de descuento es "$" y el vendedor ingresa $200 con un carrito total de $1000, entonces el descuento es $200 y el total final es $800.
- [ ] Dado que el tipo de descuento es "$" y el vendedor ingresa un monto mayor al total del carrito, entonces el descuento se clampa al total y el total final es $0.
- [ ] Dado que el descuento resulta en total $0, cuando el vendedor confirma la venta, entonces la venta se registra correctamente con `Venta.Total = 0`.

### Procesamiento de la venta

- [ ] Dado que todos los productos del carrito tienen stock suficiente, cuando el vendedor confirma la venta, entonces el stock de cada producto se reduce en la cantidad vendida, la venta se guarda con el `NumeroVenta` correcto y los detalles quedan registrados.
- [ ] Dado que un producto en el carrito tiene stock = 2 pero se intenta vender cantidad 3, cuando se confirma la venta, entonces la transacción hace rollback, el stock no cambia, y el usuario ve una notificación de error con el nombre del producto y el stock disponible.
- [ ] Dado que se hace clic en "Confirmar Venta", entonces el botón queda deshabilitado con spinner hasta que la operación concluye (éxito o error).
- [ ] Dado que la venta se confirma exitosamente, entonces se abre el modal de éxito con el número de venta en formato D6 y el modal de checkout se cierra.

### Remito PDF

- [ ] Dado que la venta fue exitosa y el vendedor hace clic en "Imprimir Remito PDF", entonces se genera un PDF en formato A5 y se abre el diálogo nativo de guardado con el nombre `Remito_[D6]_[yyyyMMdd].pdf`.
- [ ] Dado que el remito se generó para una venta con descuento, entonces el PDF contiene la fila de subtotal, la fila de descuento en negativo y el total final.
- [ ] Dado que la venta fue contado, entonces el pie del total en el remito dice "TOTAL (Contado)".
- [ ] Dado que la venta fue fiada, entonces el pie del total en el remito dice "TOTAL (Cuenta Corriente)" y aparece la etiqueta "FIADO" en el bloque del cliente.
- [ ] Dado que el remito no tiene cliente asociado (Consumidor Final), entonces el bloque de datos del cliente no aparece en el PDF.
- [ ] Dado que el usuario cancela el diálogo de guardado, entonces aparece una notificación de error pero la venta no se revierte.
- [ ] Dado que el vendedor hace clic en "Nueva Venta" desde el modal de éxito, entonces el modal se cierra y el POS queda con el carrito vacío y el buscador enfocado.

---

## 10. Casos borde y manejo de errores

| Escenario | Comportamiento esperado |
|---|---|
| **Stock agotado durante la transacción** (otro terminal redujo el stock entre que el vendedor armó el carrito y confirmó la venta) | La validación dentro de `ProcesarVentaAsync` detecta el stock insuficiente → `RollbackAsync` → excepción re-lanzada → el componente muestra toast de error con el nombre del producto y el stock disponible al momento del error. El carrito permanece intacto para que el vendedor pueda ajustar las cantidades. |
| **Cliente seleccionado para fiado pero sin CuentaCorriente** | `ProcesarVentaAsync` lanza `InvalidOperationException("El cliente no tiene cuenta corriente asociada.")` → rollback → toast de error. Esto no debería ocurrir en uso normal (la cuenta se crea al dar de alta el cliente), pero se maneja explícitamente. |
| **Descuento mayor al total del carrito (tipo $)** | La fórmula `Math.Clamp(discountValue, 0, CartTotal)` impide que el descuento supere el total. El total final queda en $0. La venta se puede confirmar. |
| **Descuento porcentual > 100%** | La fórmula `Math.Clamp(discountValue, 0, 100)` lo limita a 100%. El total final es $0. La venta se puede confirmar. |
| **Carrito vacío al intentar cobrar** | El botón "Cobrar" está deshabilitado (`disabled="@(!cartItems.Any())"`) y el método `ConfirmSale` tiene guardia `if (!cartItems.Any()) return`. Doble protección. |
| **Fallo en la generación del PDF** (excepción en QuestPDF o error de E/S) | Se captura en el bloque `catch` de `ImprimirRemitoAsync` → toast de error "Error al generar remito: [mensaje]". La venta ya fue confirmada y persistida; no se revierte. |
| **Usuario cancela el diálogo de guardado del PDF** | `FileSaver.Default.SaveAsync` devuelve `result.IsSuccessful = false` → toast de error "No se pudo guardar el remito." La venta no se revierte. |
| **Doble clic en "Confirmar Venta"** | La guardia `if (isProcessingSale) return` y el atributo `disabled` del botón previenen el doble envío. |
| **Producto eliminado (soft delete) aparece en un carrito existente** | El catálogo solo carga productos activos. Si un producto se elimina mientras el vendedor tiene el POS abierto, seguirá visible en su carrito hasta el próximo `LoadData()`. Al confirmar la venta, `ProcesarVentaAsync` lo encontrará por `Id` en la DB (EF Core usa `IgnoreQueryFilters` implícitamente en `FindAsync`); si el stock es 0, fallará con error de stock insuficiente. |
| **Búsqueda con campo vacío al presionar Enter** | `HandleSearchKeydown` verifica `if (string.IsNullOrWhiteSpace(searchQuery)) return`. No ocurre ninguna acción. |

---

## 11. Preguntas abiertas

- **Anulación de ventas:** ¿se implementará en este mismo componente (botón en el historial) o en una pantalla separada? ¿La anulación revierte el stock y el movimiento financiero/cuenta corriente?
- **Numeración de ventas:** el algoritmo `MAX(NumeroVenta) + 1` puede generar números duplicados en un escenario de alta concurrencia con múltiples instancias simultáneas. ¿Se acepta este riesgo o se migra a una secuencia de SQLite / AUTOINCREMENT?
- **Impresión directa:** ¿se evaluará en el futuro la impresión directa a impresora térmica (sin diálogo de guardado) para acelerar el flujo en cajas de alto volumen?
- **Descuento en el modelo de datos:** actualmente el descuento no se persiste como campo propio en `Venta` (solo se infiere por diferencia). ¿Se agregarán campos `DescuentoTipo` y `DescuentoValor` a la entidad para facilitar reportes futuros?
- **Refresco de stock con POS abierto:** si el vendedor deja el POS abierto mucho tiempo, el stock en memoria puede quedar desactualizado. ¿Se implementará un refresco periódico automático o se agregará un botón de actualización manual?
