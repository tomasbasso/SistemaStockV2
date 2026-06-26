# Spec: Presupuestos

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"Te voy a contar una historia. Un cliente del negocio pide una cotización formal antes de decidir si compra. El vendedor necesita armar un presupuesto: busca y agrega productos, define cantidades, asigna al cliente destinatario y establece una fecha hasta la cual el presupuesto es válido. Al guardar, el sistema genera automáticamente un PDF formal en A4 con el desglose de ítems, el total y la aclaración de que los precios pueden cambiar. Los presupuestos vencidos se marcan visualmente para que el equipo sepa que ya no son vigentes."

---

## 2. Objetivo

Permitir al vendedor generar cotizaciones formales en PDF para los clientes del negocio, sin afectar el stock ni la contabilidad real. Resuelve la necesidad de emitir un documento profesional de referencia de precios antes de que el cliente tome la decisión de compra, y de llevar un registro histórico de los presupuestos emitidos con su estado de vigencia.

---

## 3. Alcance

### Incluye
- Pantalla de listado de todos los presupuestos guardados (`/presupuestos`), ordenados por fecha de emisión descendente.
- Modal de creación de nuevo presupuesto con interfaz dividida (catálogo de productos a la izquierda, planilla del presupuesto a la derecha).
- Buscador rápido de productos por nombre o SKU dentro del catálogo.
- Campos del encabezado: cliente destinatario (opcional), fecha de validez/vencimiento (opcional), observaciones (texto libre, opcional).
- Gestión de ítems: agregar, incrementar/decrementar cantidad, quitar producto; el precio unitario se toma del `Producto.Price` vigente en el momento de la creación.
- Total general calculado en tiempo real en la UI.
- Acción "Guardar y Descargar PDF": persiste en la base de datos y genera el PDF A4 en el mismo paso.
- Acción "Descargar PDF" desde el listado para regenerar el PDF de un presupuesto ya guardado (usando los precios almacenados en `PresupuestoDetalle.UnitPrice`).
- PDF A4 generado con QuestPDF que incluye: membrete del negocio, datos del cliente (si se asignó), número de presupuesto formateado, fecha de emisión, fecha de validez, tabla de ítems (cantidad, descripción, precio unitario, subtotal), total general, observaciones (si las hay) y cláusula informativa de no validez fiscal.
- Indicador visual en el listado: filas con `FechaVencimiento` anterior a la fecha actual se muestran en rojo con la leyenda "(vencido)".
- Eliminación de presupuestos con confirmación (soft delete: `IsDeleted = true`).
- Numeración automática y secuencial de presupuestos (`NumeroPresupuesto`, formato `D6`).

### No incluye (fuera de alcance)
- Descuento de stock al guardar un presupuesto (los presupuestos no afectan el inventario).
- Edición de presupuestos ya guardados (son inmutables una vez persistidos).
- Conversión directa de presupuesto a venta desde esta pantalla.
- Envío del PDF por correo electrónico o WhatsApp desde la aplicación.
- Aprobación o rechazo formal de presupuestos por parte del cliente.
- Paginación del listado (se cargan todos los presupuestos no eliminados).
- Filtros o búsqueda en el listado de presupuestos.
- Múltiples versiones del mismo presupuesto.
- Impresión directa desde la aplicación (el PDF se guarda en disco para impresión externa).

---

## 4. Definiciones funcionales

### 4.1 Creación del presupuesto
- Un presupuesto puede guardarse sin cliente asignado; en ese caso el PDF no incluye la sección de destinatario y el listado muestra "Sin cliente" en cursiva.
- Un presupuesto puede guardarse sin fecha de vencimiento; en ese caso el campo `FechaVencimiento` queda `null`, el PDF no muestra la línea de validez y el listado muestra "—" en la columna "Vence".
- Un presupuesto **no puede guardarse sin al menos un ítem**. El botón de guardar permanece deshabilitado mientras la lista de ítems esté vacía.
- El precio unitario de cada ítem (`PresupuestoDetalle.UnitPrice`) se toma del `Producto.Price` al momento de agregar el producto al presupuesto en construcción. Si el precio del producto cambia después de guardar, el PDF del presupuesto ya existente sigue reflejando el precio original almacenado en el detalle.
- Si se agrega el mismo producto más de una vez, se incrementa la cantidad del ítem existente en lugar de crear una fila duplicada.
- Al decrementar la cantidad de un ítem a cero, ese ítem se elimina de la planilla.
- El total del presupuesto (`Presupuesto.Total`) se calcula como la suma de `UnitPrice × Quantity` de todos los ítems y se persiste en la base de datos.

### 4.2 Numeración
- El número de presupuesto (`NumeroPresupuesto`) es autoincremental: `MAX(NumeroPresupuesto) + 1` sobre todos los presupuestos (incluyendo los eliminados con soft delete, ya que EF aplica `IgnoreQueryFilters` al calcular el máximo). Si no existen presupuestos, el primero es el número 1.
- En la UI y en el PDF el número se formatea con seis dígitos con ceros a la izquierda (ej: `#000001`).

### 4.3 Estado de vigencia
- Un presupuesto se considera **vencido** cuando `FechaVencimiento.HasValue && FechaVencimiento.Value.Date < DateTime.Today`.
- El estado de vigencia es solo visual; no bloquea ninguna acción (un presupuesto vencido se puede regenerar en PDF o eliminar).

### 4.4 Generación del PDF
- El PDF se genera en el momento del guardado (acción "Guardar y Descargar PDF") y también puede regenerarse desde el listado para cualquier presupuesto existente.
- Al regenerar desde el listado, los precios del PDF son los de `PresupuestoDetalle.UnitPrice` almacenados, no los precios actuales de los productos.
- El PDF usa los datos del negocio de `ConfiguracionApp` (nombre, dirección, teléfono).
- La cláusula fija al pie del contenido es: "Los precios indicados no incluyen IVA salvo indicación expresa. Este presupuesto no constituye factura."
- El nombre del archivo al guardar sigue el patrón: `Presupuesto_{NumeroPresupuesto:D6}_{Date:yyyyMMdd}.pdf`.
- Si la generación o el guardado del PDF falla, el presupuesto ya fue persistido en la base de datos; se muestra una notificación de error (toast) pero el presupuesto no se revierte.

### 4.5 Eliminación
- La eliminación establece `IsDeleted = true` en `Presupuesto` (soft delete). Los `PresupuestoDetalle` asociados se eliminan físicamente de la base de datos.
- El query filter de EF excluye automáticamente los presupuestos con `IsDeleted = true` del listado y de los contadores.
- La eliminación requiere confirmación explícita en un modal.

---

## 5. Datos y modelo

### Entidades principales

#### `Presupuesto`
| Campo | Tipo | Restricciones | Descripción |
|---|---|---|---|
| `Id` | `Guid` | PK | Identificador único |
| `NumeroPresupuesto` | `int` | NOT NULL, UNIQUE, autoincremental | Número legible formateado como D6 |
| `Date` | `DateTime` | NOT NULL, default `DateTime.Now` | Fecha y hora de emisión |
| `FechaVencimiento` | `DateTime?` | NULL permitido | Fecha hasta la cual el presupuesto es válido |
| `Total` | `decimal` | NOT NULL, almacenado como TEXT en SQLite | Suma de subtotales de todos los ítems |
| `ClienteId` | `Guid?` | NULL permitido, FK lógica a `Cliente` | Cliente destinatario (puede estar sin asignar) |
| `Notas` | `string` | MaxLength 500, default `""` | Observaciones libres |
| `IsDeleted` | `bool` | NOT NULL, default `false` | Soft delete |

#### `PresupuestoDetalle`
| Campo | Tipo | Restricciones | Descripción |
|---|---|---|---|
| `Id` | `Guid` | PK | Identificador único del ítem |
| `PresupuestoId` | `Guid` | NOT NULL, FK a `Presupuesto.Id` | Presupuesto al que pertenece el ítem |
| `ProductoId` | `Guid` | NOT NULL, FK lógica a `Producto.Id` | Producto cotizado |
| `Quantity` | `int` | Range(1, int.MaxValue) | Cantidad cotizada |
| `UnitPrice` | `decimal` | NOT NULL, almacenado como TEXT en SQLite | Precio unitario al momento de crear el presupuesto |

#### DTO `PresupuestoData` (sin persistencia, usado para generación de PDF)
| Campo | Tipo | Descripción |
|---|---|---|
| `Presupuesto` | `Presupuesto` | Cabecera del presupuesto |
| `Detalles` | `List<PresupuestoDetalle>` | Ítems del presupuesto |
| `NombreProductos` | `Dictionary<Guid, string>` | Mapa de `ProductoId → Nombre` para el PDF |
| `Cliente` | `Cliente?` | Datos del cliente (null si sin asignar) |
| `Config` | `ConfiguracionApp` | Datos del negocio para el membrete |

#### Clase interna de UI `ItemPresupuesto` (en `Presupuestos.razor`, sin persistencia)
| Campo | Tipo | Descripción |
|---|---|---|
| `ProductoId` | `Guid` | Id del producto |
| `Nombre` | `string` | Nombre del producto |
| `PrecioUnitario` | `decimal` | Precio al momento de agregar al carrito |
| `Cantidad` | `int` | Cantidad |
| `Subtotal` | `decimal` (computed) | `PrecioUnitario × Cantidad` |

### Relaciones
- `Presupuesto` → `Cliente`: relación opcional (0..1 a N). No hay FK declarada en EF, solo `ClienteId` nullable.
- `Presupuesto` → `PresupuestoDetalle`: relación 1 a N. Los detalles se eliminan físicamente al hacer soft delete del presupuesto padre.
- `PresupuestoDetalle` → `Producto`: relación lógica por `ProductoId`. No hay FK declarada en EF; el nombre se resuelve por diccionario al generar el PDF.

### Persistencia
- Motor: SQLite local (via EF Core).
- Los campos `decimal` se almacenan como `TEXT` en SQLite (convención del proyecto).
- Las tablas `Presupuestos` y `PresupuestoDetalles` se crean con `CREATE TABLE IF NOT EXISTS` en `InitializeDatabaseAsync` si no existen (migración manual).
- El query filter de EF en `Presupuesto` excluye registros con `IsDeleted = true`.

---

## 6. UX / Interfaz

### 6.1 Ruta y componente
- Ruta: `/presupuestos`
- Componente: `Presupuestos.razor` (en `Components/Pages/`)

### 6.2 Estados de la página principal (listado)

| Estado | Descripción visual |
|---|---|
| Cargando | Spinner centrado (`bi-arrow-repeat animate-spin`) mientras `isLoading = true` |
| Sin datos | Área vacía con ícono, mensaje "Sin presupuestos" y acceso directo a crear |
| Con datos | Tabla con todos los presupuestos no eliminados |

### 6.3 Tabla del listado
Columnas: N° (formato `#000000`), Fecha (dd/MM/yyyy), Cliente, Vence, Total, Acciones.

- **Fila vencida**: la celda "Vence" muestra la fecha en color rojo (`--color-danger`) con la leyenda "(vencido)" junto a la fecha.
- **Fila sin cliente**: la celda "Cliente" muestra "Sin cliente" en cursiva gris.
- **Fila sin fecha de vencimiento**: la celda "Vence" muestra "—".
- **Acciones**: los botones de "PDF" y "Eliminar" aparecen al hacer hover sobre la fila (`opacity-0 → opacity-100`).
- Botón "PDF" muestra spinner mientras genera (`downloadingId == p.Id`), se deshabilita durante la descarga.

### 6.4 Modal de creación de nuevo presupuesto
Abierto con el botón "Nuevo Presupuesto" del header.

**Encabezado (3 columnas en desktop):**
- Selector de cliente (desplegable de clientes registrados no eliminados, opción por defecto "— Sin cliente —").
- Input de fecha "Válido hasta" (date picker, opcional).
- Input de texto "Observaciones" (opcional, placeholder explicativo).

**Cuerpo dividido en 2 columnas:**

Columna izquierda — Catálogo de productos:
- Input de búsqueda (por nombre o SKU, filtrado en tiempo real con `oninput`).
- Lista scrollable (max-height 280px) con todos los productos activos ordenados por nombre.
- Cada ítem muestra: nombre, SKU y precio de venta. Click sobre el ítem lo agrega a la planilla.

Columna derecha — Planilla del presupuesto:
- Estado vacío: área con ícono y texto "Seleccioná productos del catálogo".
- Con ítems: lista scrollable (max-height 240px) de productos seleccionados.
  - Cada ítem muestra: nombre, precio unitario, cantidad y subtotal.
  - Controles de cantidad: botones "−" y "+" (decrementar a 0 elimina el ítem). 
  - Botón "×" para quitar el ítem directamente.
- Total general visible debajo de la lista de ítems.

**Footer del modal:**
- Botón "Cancelar" (cierra el modal sin guardar).
- Botón "Guardar y Descargar PDF" (submit): deshabilitado si no hay ítems o si `isSaving = true`. Muestra texto e ícono de carga mientras procesa.

### 6.5 Modal de confirmación de eliminación
- Muestra el número del presupuesto a eliminar.
- Advertencia en rojo: "Esta acción no se puede deshacer."
- Botones: "Cancelar" y "Eliminar" (color danger).

### 6.6 Notificaciones toast
- Éxito al guardar: "Presupuesto #XXXXXX guardado."
- Error en cualquier operación: mensaje descriptivo del error.
- El servicio `NotificationService` gestiona los toasts.

---

## 7. Definiciones técnicas

### 7.1 Stack
- Framework UI: .NET MAUI con Blazor Hybrid (`net8.0-windows`)
- Motor de base de datos: SQLite local via Entity Framework Core
- Generación de PDF: QuestPDF (licencia Community)
- Guardado de archivos: `CommunityToolkit.Maui.Storage.FileSaver`

### 7.2 Componente principal
- `Presupuestos.razor` (page component en `Components/Pages/`)
- Inyecta: `DataService`, `PdfService`, `NotificationService`

### 7.3 Capa de datos (`DataService`)
Métodos involucrados:
- `GetPresupuestosAsync()` — retorna todos los presupuestos no eliminados ordenados por fecha descendente.
- `GetPresupuestoDetallesAsync(Guid presupuestoId)` — retorna los detalles de un presupuesto.
- `SavePresupuestoAsync(Presupuesto, List<PresupuestoDetalle>)` — asigna el número secuencial, calcula el total, persiste cabecera y detalles en una transacción implícita de EF.
- `DeletePresupuestoAsync(Guid id)` — soft delete de la cabecera + eliminación física de los detalles.
- `GetProductosAsync()` — para poblar el catálogo (con query filter, excluye eliminados).
- `GetClientesAsync()` — para poblar el selector de clientes (con query filter, excluye eliminados).
- `GetConfiguracionAsync()` — para obtener los datos del negocio al generar el PDF.

### 7.4 Generación del PDF (`PdfService.GenerarPresupuesto`)
- Método: `byte[] GenerarPresupuesto(PresupuestoData data)`
- Tamaño de página: A4 (`QuestPDF.Helpers.PageSizes.A4`), margen 40pt, fuente Arial 10pt.
- Estructura del PDF:
  - **Header**: membrete (nombre del negocio, dirección, teléfono) a la izquierda; caja de documento con "PRESUPUESTO", número y fecha a la derecha (fondo teal `#0f766e`).
  - **Contenido**:
    - Banda de validez (fondo verde claro) con la fecha de vencimiento, solo si `FechaVencimiento` tiene valor.
    - Sección de destinatario (nombre, teléfono, dirección del cliente), solo si hay cliente asignado.
    - Tabla de ítems: columnas CANT, DESCRIPCIÓN, P. UNIT, SUBTOTAL. Filas con bandas alternadas.
    - Bloque de total (fondo teal, alineado a la derecha).
    - Sección de observaciones, solo si `Notas` tiene contenido.
    - Cláusula informativa en pie de contenido: "Los precios indicados no incluyen IVA salvo indicación expresa. Este presupuesto no constituye factura."
  - **Footer**: fecha de generación a la izquierda, nombre del negocio centrado, número de página a la derecha.
- El PDF se retorna como `byte[]`; el componente lo convierte a `MemoryStream` y llama a `FileSaver.SaveAsync` en el hilo principal (`MainThread.InvokeOnMainThreadAsync`).

### 7.5 Guardado del PDF
- Se invoca desde `MainThread.InvokeOnMainThreadAsync` para cumplir con los requisitos de MAUI en Windows.
- El nombre del archivo: `Presupuesto_{NumeroPresupuesto:D6}_{Date:yyyyMMdd}.pdf`.
- Si `FileSaver.Result.IsSuccessful == false`, se muestra notificación de error. El presupuesto ya fue guardado en DB; no se revierte.

### 7.6 Convenciones del proyecto
- Los `decimal` se almacenan como `TEXT` en SQLite (todos los campos `decimal` tienen `.HasColumnType("TEXT")` en `OnModelCreating`).
- Soft delete implementado con `IsDeleted bool` + `HasQueryFilter` en EF Core.
- IDs de tipo `Guid` generados en el cliente (`Guid.NewGuid()`).
- Las migraciones de esquema son manuales (scripts SQL en `InitializeDatabaseAsync`), sin uso de `dotnet ef migrations` por limitaciones de proyectos MAUI multi-target.

---

## 8. Seguridad y permisos

- La aplicación es de usuario único, offline, sin sistema de autenticación ni roles.
- No hay restricciones de acceso diferenciadas por rol para la funcionalidad de presupuestos.
- El acceso a la ruta `/presupuestos` es libre para cualquier usuario con la aplicación abierta.
- Los archivos PDF se guardan en la ubicación que el usuario elige mediante el diálogo nativo de `FileSaver`; la aplicación no persiste rutas ni tiene acceso irrestricto al sistema de archivos.

---

## 9. Criterios de aceptación

- [ ] Dado que no existen presupuestos, cuando el usuario accede a `/presupuestos`, entonces ve el estado vacío con el mensaje "Sin presupuestos" y el botón "Crear Presupuesto".

- [ ] Dado que el usuario abre el modal de nuevo presupuesto, cuando no ha agregado ningún producto, entonces el botón "Guardar y Descargar PDF" está deshabilitado.

- [ ] Dado que el usuario busca en el catálogo escribiendo texto en el buscador, cuando escribe un término, entonces la lista se filtra en tiempo real mostrando solo los productos cuyo nombre o SKU contengan ese texto (sin distinción de mayúsculas).

- [ ] Dado que el usuario hace click en un producto del catálogo, cuando ese producto no estaba en la planilla, entonces aparece como un nuevo ítem con cantidad 1 y el precio tomado de `Producto.Price` en ese momento.

- [ ] Dado que el usuario hace click en un producto que ya está en la planilla, cuando hace click nuevamente, entonces la cantidad de ese ítem se incrementa en 1 (no se crea una fila duplicada).

- [ ] Dado que el usuario decrementa la cantidad de un ítem a 0, cuando presiona "−", entonces el ítem se elimina de la planilla.

- [ ] Dado que hay ítems en la planilla, cuando el usuario modifica cantidades, entonces el total general se actualiza en tiempo real sin necesidad de guardar.

- [ ] Dado que el usuario completa el presupuesto y presiona "Guardar y Descargar PDF", cuando el guardado es exitoso, entonces:
  - El presupuesto se persiste en la tabla `Presupuestos` con `NumeroPresupuesto = MAX_PREVIO + 1`.
  - Cada ítem se persiste en `PresupuestoDetalles` con el `UnitPrice` al momento de la creación.
  - Se abre el diálogo de guardado de archivo con el PDF generado.
  - Aparece un toast de éxito con el número del presupuesto.
  - El modal se cierra y el listado se actualiza mostrando el nuevo presupuesto.

- [ ] Dado que el usuario guarda un presupuesto sin asignar cliente, cuando el presupuesto aparece en el listado, entonces la columna "Cliente" muestra "Sin cliente" en cursiva, y el PDF no incluye la sección de destinatario.

- [ ] Dado que el usuario guarda un presupuesto sin fecha de vencimiento, cuando el presupuesto aparece en el listado, entonces la columna "Vence" muestra "—", y el PDF no incluye la línea de validez.

- [ ] Dado que un presupuesto tiene una fecha de vencimiento anterior a la fecha actual, cuando aparece en el listado, entonces la fecha en la columna "Vence" se muestra en color rojo con la leyenda "(vencido)" junto a la fecha.

- [ ] Dado que existe un presupuesto guardado y el precio de alguno de sus productos cambió posteriormente, cuando el usuario regenera el PDF desde el listado, entonces el PDF muestra los precios originales almacenados en `PresupuestoDetalle.UnitPrice`, no los precios actuales del producto.

- [ ] Dado que el usuario presiona el botón "PDF" en el listado, cuando se genera el PDF, entonces el botón muestra un spinner y queda deshabilitado durante la generación, y vuelve a su estado normal al terminar.

- [ ] Dado que el PDF del presupuesto se genera correctamente, cuando el usuario lo abre, entonces contiene: membrete con datos del negocio de `ConfiguracionApp`, número formateado como `#000001`, fecha de emisión, tabla de ítems con cantidad/descripción/precio unitario/subtotal, total general y la cláusula "Los precios indicados no incluyen IVA salvo indicación expresa. Este presupuesto no constituye factura."

- [ ] Dado que el usuario presiona "Eliminar" en un presupuesto del listado, cuando confirma la eliminación, entonces:
  - `Presupuesto.IsDeleted` se establece en `true`.
  - Los `PresupuestoDetalle` asociados se eliminan físicamente.
  - El presupuesto desaparece del listado.
  - Aparece un toast de éxito.

- [ ] Dado que el guardado del presupuesto en DB es exitoso pero la generación o guardado del PDF falla, cuando ocurre el fallo, entonces aparece un toast de error, pero el presupuesto permanece en el listado (no se revierte la persistencia en DB).

- [ ] Dado que no hay presupuestos previos, cuando se guarda el primero, entonces su `NumeroPresupuesto` es 1.

---

## 10. Casos borde y manejo de errores

- **Presupuesto sin cliente**: permitido. No requiere cliente para guardar. En el listado se muestra "Sin cliente" en cursiva. El PDF omite la sección de destinatario.

- **Presupuesto sin fecha de vencimiento**: permitido. `FechaVencimiento` queda `null`. En el listado se muestra "—" en la columna "Vence". El PDF omite la línea de validez.

- **Intento de guardar con 0 ítems**: imposibilitado por la UI (botón deshabilitado). Si se llama directamente al método `GuardarYDescargarAsync`, el guard `if (!items.Any() || isSaving) return;` lo previene.

- **Precio del producto cambia después de guardar el presupuesto**: el PDF regenerado desde el listado siempre usa `PresupuestoDetalle.UnitPrice` (precio original). El precio actual del producto (`Producto.Price`) no se consulta al regenerar. El presupuesto es un snapshot inmutable de los precios en el momento de su creación.

- **Fallo en la generación del PDF**: `PdfSvc.GenerarPresupuesto` puede lanzar una excepción si QuestPDF falla. El bloque `try/catch` en `GuardarYDescargarAsync` captura la excepción, muestra un toast de error y ejecuta `finally { isSaving = false; }`. El presupuesto ya persistido en DB no se revierte (no hay rollback).

- **Fallo en el guardado del archivo PDF** (`FileSaver.Result.IsSuccessful == false`): se muestra toast de error "No se pudo guardar el PDF." El presupuesto sigue existiendo en la DB y puede regenerarse desde el listado.

- **Doble click en "Guardar y Descargar PDF"**: el guard `if (!items.Any() || isSaving) return;` y el estado `disabled` del botón mientras `isSaving = true` previenen la creación de duplicados.

- **Producto eliminado (soft delete) después de guardarse en un presupuesto**: el `ProductoId` en `PresupuestoDetalle` sigue siendo válido pero el producto ya no aparece en las consultas normales. Al regenerar el PDF, el `NombreProductos` se construye con `GetProductosAsync()` que aplica el query filter de productos activos; si el producto fue eliminado, el nombre del ítem en el PDF mostrará "Producto" (fallback del diccionario). Esto es comportamiento actual; si se requiere robustez, se podría almacenar el nombre del producto en `PresupuestoDetalle` (pendiente de decisión: ver sección 11).

- **Base de datos vacía / primer uso**: `SavePresupuestoAsync` maneja el caso donde `Presupuestos.AnyAsync()` es false y usa 0 como base, resultando en `NumeroPresupuesto = 1`.

- **Presupuesto con muchos ítems**: QuestPDF maneja automáticamente el salto de página si la tabla de ítems excede la altura de la página A4.

---

## 11. Preguntas abiertas

1. **Nombre del producto en `PresupuestoDetalle`**: actualmente el nombre del producto no se almacena en el detalle del presupuesto; se resuelve por diccionario en el momento de generar el PDF usando los productos activos. Si un producto es eliminado (soft delete), el PDF mostrará "Producto" como descripción. ¿Se debe agregar un campo `ProductoNombre string` a `PresupuestoDetalle` para snapshot completo del ítem?

2. **Descripción adicional por ítem**: ¿los ítems del presupuesto necesitan un campo de descripción libre (ej: aclaraciones sobre el producto, medidas, color) además del nombre del producto?

3. **Descuentos**: ¿se requiere la posibilidad de aplicar un descuento (porcentual o fijo) a nivel de ítem o a nivel del total del presupuesto?

4. **Paginación y filtros en el listado**: con el crecimiento del historial de presupuestos, ¿se requiere paginación o filtros (por cliente, por rango de fechas, por estado vencido/vigente)?

5. **Conversión a venta**: ¿se va a implementar en el futuro la posibilidad de "convertir" un presupuesto aceptado directamente en una venta desde esta pantalla?
