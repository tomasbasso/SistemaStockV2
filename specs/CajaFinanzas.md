# Spec: Caja y Finanzas (Libro de Caja Digital)

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"Te voy a contar una historia. El negocio necesita un libro de caja digital para controlar el flujo de efectivo. Las ventas cobradas en efectivo y los cobros de deudas de clientes se registran automáticamente como ingresos. Pero también hay movimientos que el dueño necesita cargar manualmente: pago de servicios, retiros de efectivo, compras a proveedores, aportes de capital. Al final del día quiere ver un balance neto que resuma todo lo que entró y salió."

---

## 2. Objetivo

Proveer al dueño del negocio un libro de caja digital centralizado que registre todos los movimientos de efectivo — tanto los generados automáticamente por el sistema (ventas contado, cobros de cuenta corriente) como los ingresados a mano (gastos, retiros, aportes). El módulo permite visualizar el historial cronológico, filtrar y buscar movimientos, y consultar el balance neto acumulado histórico en todo momento.

---

## 3. Alcance

### Incluye
- Listado paginado de todos los `MovimientoFinanciero` con búsqueda por texto (concepto/descripción)
- Tarjetas KPI: Total Ingresos acumulado, Total Egresos acumulado, Balance Neto (Ingresos − Egresos)
- Creación manual de movimientos de tipo Ingreso o Egreso desde la pantalla (modal)
- Eliminación de movimientos manuales (sin VentaId, sin referencia a cobro automático) — acción destructiva con confirmación
- Visualización de la columna "Referencia" que indica si el movimiento proviene de una venta (`Venta Ref.`) o es manual (`-`)
- Integración como receptor de movimientos automáticos creados por PuntoDeVenta y por el módulo de Clientes (saldar deuda)
- Exportación del libro de caja a Excel via ReportService (columnas: Fecha y Hora, Concepto, Tipo, Ingreso(+)/Egreso(−), fila de Balance Neto al final)
- Provisión de los últimos 10 movimientos del día al Dashboard ("Actividad Reciente")

### No incluye (fuera de alcance)
- Filtros por rango de fechas o por tipo (Ingreso/Egreso) en esta versión — la búsqueda es solo por texto de descripción
- Edición de movimientos ya guardados (ni manuales ni automáticos)
- Balance por período (diario, semanal, mensual) — el balance mostrado es siempre histórico acumulado
- Multimoneda — el sistema opera en una sola moneda (ARS por defecto según ConfiguracionApp)
- Conciliación bancaria
- Gestión de caja chica con apertura/cierre de turno
- Notificaciones o alertas por balance negativo

---

## 4. Definiciones funcionales

### Origen de los movimientos

**Movimientos automáticos** — generados por otros módulos, sin intervención del usuario en Finanzas:
- **Venta contado:** al cerrar una venta con `IsFiado = false` en PuntoDeVenta, el sistema crea un `MovimientoFinanciero` de tipo Ingreso con descripción `"Venta #<NumeroVenta>"` y `VentaId` referenciado. Este movimiento queda vinculado a la venta.
- **Cobro de cuenta corriente:** al saldar la deuda de un cliente desde el módulo Clientes, se crea un `MovimientoFinanciero` de tipo Ingreso con descripción `"Cobro C/C - <NombreCliente>"` y sin `VentaId` (campo nulo).

**Movimientos manuales** — creados desde la pantalla `Finanzas.razor`:
- El usuario ingresa Concepto/Descripción (texto libre, obligatorio, máximo 500 caracteres) y Monto (decimal mayor a cero, obligatorio).
- El tipo (Ingreso o Egreso) se determina por el botón que abrió el modal ("Nuevo Ingreso" o "Nuevo Egreso"); no es editable dentro del formulario.
- Casos de uso típicos de Egreso: pago de servicios, compra de mercadería a proveedor, retiro de efectivo.
- Casos de uso típicos de Ingreso manual: aporte de capital, corrección manual.

### Balance neto
- Balance Neto = Suma de todos los ingresos − Suma de todos los egresos (scope: todos los registros históricos, sin filtro de fecha).
- El balance puede ser negativo (más egresos que ingresos); se muestra igual, en el mismo componente KPI, sin bloqueos ni alertas.
- Los tres KPIs (Balance Neto, Total Ingresos, Total Egresos) se recalculan en cada carga de la pantalla.

### Eliminación de movimientos manuales
- Solo se permite eliminar movimientos que **no** tienen `VentaId` populado y que fueron creados manualmente (es decir, no provienen de un cobro de cuenta corriente con referencia explícita).
- La eliminación es física (hard delete) — no hay soft delete para movimientos financieros.
- La interfaz debe pedir confirmación antes de eliminar. Actualmente la pantalla no expone botón de eliminación en la tabla; su implementación futura debe incluir confirmación modal o inline.
- Los movimientos generados automáticamente (ventas, cobros) **no** se pueden eliminar desde Finanzas; solo se anulan desde su módulo de origen.

### Venta anulada
- Cuando una venta es marcada como `IsDeleted = true` en el módulo de Ventas, el `MovimientoFinanciero` asociado (con ese `VentaId`) **no** se elimina automáticamente. Esto genera una inconsistencia que debe resolverse manualmente hasta que se implemente una compensación automática (ver sección 11).

### Búsqueda
- La búsqueda filtra por contenido del campo `Description` (case-insensitive, búsqueda por substring).
- El debounce es de 400 ms para evitar consultas por cada tecla.
- La búsqueda no filtra los KPIs de Balance, Ingresos y Egresos — esos siempre reflejan el total histórico sin importar el filtro activo.

### Paginación
- La lista usa paginación de 20 ítems por página (pageSize = 20).
- El orden por defecto es fecha descendente (más reciente primero).

---

## 5. Datos y modelo

### Entidad: `MovimientoFinanciero`

| Campo         | Tipo            | Restricciones                                          | Notas                                      |
|---------------|-----------------|--------------------------------------------------------|--------------------------------------------|
| `Id`          | `Guid`          | PK, generado automáticamente                           |                                            |
| `Type`        | `TipoMovimiento`| Enum: Ingreso / Egreso. Requerido.                     |                                            |
| `Amount`      | `decimal`       | Requerido. `Range(0.01, MaxValue)`. Almacenado como TEXT en SQLite. | Debe ser mayor a cero.           |
| `Date`        | `DateTime`      | Default: `DateTime.Now`. No editable post-creación.    | Almacenado con hora exacta.                |
| `Description` | `string`        | Requerido. `MaxLength(500)`.                           | Campo `Concepto` en la UI.                 |
| `VentaId`     | `Guid?`         | FK opcional a `Venta.Id`. Nulo para movimientos manuales y cobros C/C. | Indica origen automático de venta. |

### Enum: `TipoMovimiento`
```
Ingreso = 0
Egreso  = 1
```

### Entidad relacionada: `Venta`

| Campo        | Tipo      | Notas relevantes                         |
|--------------|-----------|------------------------------------------|
| `Id`         | `Guid`    |                                          |
| `NumeroVenta`| `int`     | Usado en la descripción del movimiento   |
| `IsFiado`    | `bool`    | Si es true, NO genera movimiento en Caja |
| `IsDeleted`  | `bool`    | Soft delete — el movimiento financiero NO se elimina en cascada |

### Entidad relacionada: `CuentaCorriente`

| Campo       | Tipo      | Notas relevantes                              |
|-------------|-----------|-----------------------------------------------|
| `ClienteId` | `Guid`    | FK a Cliente                                  |
| `Balance`   | `decimal` | Se reduce al saldar; el ingreso en caja es independiente |

### Persistencia
- Base de datos: SQLite vía EF Core (`StockDbContext`).
- El campo `Amount` se almacena como TEXT en SQLite por configuración explícita del `modelBuilder` — la conversión decimal↔string es manejada por EF Core.
- No existe un índice explícito sobre `Date` actualmente; para datasets grandes puede ser necesario (ver sección 11).

### Métodos del DataService relevantes

| Método | Descripción |
|---|---|
| `GetMovimientosPaginadosAsync(page, pageSize, searchTerm)` | Lista paginada, orden fecha desc, filtro por descripción |
| `GetTotalMovimientosAsync(searchTerm)` | Count total (para paginación) |
| `GetTotalesMovimientosAsync()` | Devuelve `(decimal Ingresos, decimal Egresos)` histórico |
| `AddMovimientoAsync(MovimientoFinanciero)` | Inserta un movimiento manual |
| `DeleteMovimientoAsync(Guid)` | Hard delete por Id |
| `GetDashboardDataAsync()` | Incluye últimos 10 movimientos del día para el Dashboard |

---

## 6. UX / Interfaz

### Pantalla principal: `/finanzas`

**Header:**
- Título: "Caja y Finanzas", subtítulo: "Historial de Ingresos y Egresos"
- Botones de acción: "Nuevo Egreso" (rojo, ícono flecha abajo) y "Nuevo Ingreso" (verde, ícono flecha arriba)

**Tarjetas KPI (grid 3 columnas en desktop, 1 columna en móvil):**
- Balance Neto (Histórico) — borde izquierdo color primario, texto blanco
- Total Ingresos (Histórico) — borde izquierdo verde, texto verde
- Total Egresos (Histórico) — borde izquierdo rojo, texto rojo
- Formato de moneda: `ToString("C")` con cultura configurada en la app

**Barra de búsqueda:**
- Input con ícono lupa, placeholder "Buscar movimientos..."
- Debounce 400 ms, reactivo (oninput), resetea a página 1 al buscar

**Estado vacío (sin movimientos):**
- Ícono billetera, título "Sin Movimientos", descripción "Aún no se ha registrado dinero en el sistema."

**Estado de carga:**
- Spinner centrado en contenedor de mínimo 400px de alto

**Tabla de movimientos:**

| Columna     | Contenido |
|-------------|-----------|
| Fecha       | `dd/MM/yyyy HH:mm` |
| Descripción | Ícono flecha (verde=Ingreso, rojo=Egreso) + texto del concepto |
| Referencia  | "Venta Ref." si tiene VentaId; "-" si es manual o cobro C/C |
| Monto       | "+$X.XX" en verde (Ingreso) o "-$X.XX" en rojo (Egreso) |

- Hover con fondo sutil en cada fila
- Paginación disponible (componente `AppPagination`, actualmente comentado — pendiente de activación)

**Modal de nuevo movimiento:**
- Título dinámico: "Registrar Ingreso" o "Registrar Egreso" según el botón que lo abrió
- Campos:
  - **Concepto / Descripción** (texto, autofocus, obligatorio, placeholder con ejemplos)
  - **Monto** (número decimal, obligatorio, mayor a cero)
- El tipo (Ingreso/Egreso) está prefijado por el contexto y no se muestra como campo editable
- Botones: "Cancelar" (cierra modal sin guardar) y "Guardar" (color según tipo)
- Validación con `DataAnnotationsValidator`; mensajes de error inline bajo cada campo
- Guardar deshabilitado si la validación falla (guard en `Save()`: descripción no vacía y monto > 0)

### Flujos principales

1. **Ver historial:** Usuario navega a `/finanzas` → se cargan KPIs y lista → visualiza movimientos cronológicos.
2. **Buscar:** Usuario escribe en el buscador → tras 400 ms se recarga la lista filtrada (KPIs no cambian).
3. **Registrar ingreso manual:** Clic "Nuevo Ingreso" → modal → ingresa concepto y monto → Guardar → notificación success → lista recargada.
4. **Registrar egreso manual:** Idem con "Nuevo Egreso".
5. **Exportar a Excel:** Desde módulo Reportes (fuera de esta pantalla), llama a `ReportService.GenerateFinancialReport()` con la lista completa.

---

## 7. Definiciones técnicas

- **Stack:** .NET MAUI Blazor Hybrid (C# / Razor), SQLite vía EF Core, Tailwind CSS + Bootstrap Icons
- **Componente principal:** `SistemaDeStockV3/Components/Pages/Finanzas.razor` (code-behind inline en bloque `@code`)
- **Servicios consumidos:**
  - `DataService` — toda la persistencia (inyección de `@inject DataService Data`)
  - `NotificationService` — feedback de éxito/error al usuario (`@inject NotificationService Notifications`)
- **Componentes compartidos usados:**
  - `AppModal` — modal genérico para el formulario de nuevo movimiento
  - `AppPagination` — paginación (actualmente comentada en el markup)
- **Patrón de búsqueda con debounce:** `CancellationTokenSource` + `Task.Delay(400)` — se cancela en cada nueva keystroke; implementado con `IDisposable`
- **Exportación:** `ReportService.GenerateFinancialReport(List<MovimientoFinanciero>)` — retorna `byte[]` de un archivo `.xlsx` generado con ClosedXML
- **Creación de movimientos automáticos:**
  - Venta contado: dentro de `DataService.AddVentaAsync()`, en una transacción EF Core — si la venta es al fiado, NO se crea movimiento
  - Cobro C/C: en `Clientes.razor`, llamando directamente a `Data.AddMovimientoAsync()` tras reducir el balance de `CuentaCorriente`
- **Guard de validación en Save():** El método `Save()` tiene una validación de corto circuito adicional a `DataAnnotations` (`Amount <= 0` → no guarda) para prevenir monto cero aunque la anotación `[Range(0.01...)]` debería capturarlo antes

---

## 8. Seguridad y permisos

- **Sistema single-user:** SistemaDeStockV3 es una aplicación de escritorio MAUI sin autenticación multiusuario. No existe sistema de roles ni sesiones.
- **Acceso:** Cualquier usuario que ejecute la aplicación tiene acceso completo a todas las funcionalidades, incluyendo la creación y eliminación de movimientos.
- **Integridad referencial:** La eliminación de movimientos con `VentaId` desde la UI debe ser prevenida a nivel de interfaz (no mostrar botón de eliminar en esas filas). No hay FK constraint a nivel de base de datos que lo impida en SQLite.
- **Validación de entrada:** Los campos de texto están limitados por `MaxLength` y los montos por `Range` vía DataAnnotations. El guard adicional en `Save()` protege contra montos ≤ 0.

---

## 9. Criterios de aceptación

- [ ] **Carga de pantalla:** Dado que hay movimientos registrados, cuando el usuario navega a `/finanzas`, entonces se muestran las tres tarjetas KPI con valores correctos y la tabla con los movimientos más recientes primero.

- [ ] **Estado vacío:** Dado que no existen movimientos en la base de datos, cuando el usuario navega a `/finanzas`, entonces se muestra el estado vacío con el ícono de billetera y el texto "Sin Movimientos".

- [ ] **KPIs históricos:** Dado que existen N ingresos y M egresos en la base de datos, cuando se carga la pantalla, entonces el Total Ingresos es la suma de todos los Ingresos, el Total Egresos es la suma de todos los Egresos, y el Balance Neto es Ingresos − Egresos (puede ser negativo).

- [ ] **Registrar ingreso manual:** Dado que el usuario hace clic en "Nuevo Ingreso", cuando completa Concepto y Monto (> 0) y hace clic en "Guardar", entonces se crea un `MovimientoFinanciero` de tipo Ingreso sin `VentaId`, la lista se recarga y aparece el movimiento al tope, el KPI de Total Ingresos y Balance Neto se actualiza correctamente, y se muestra una notificación de éxito.

- [ ] **Registrar egreso manual:** Dado que el usuario hace clic en "Nuevo Egreso", cuando completa Concepto y Monto (> 0) y hace clic en "Guardar", entonces se crea un `MovimientoFinanciero` de tipo Egreso, la lista muestra el monto con signo negativo en rojo, y el KPI de Total Egresos y Balance Neto se actualiza.

- [ ] **Validación: descripción vacía:** Dado el modal de nuevo movimiento abierto, cuando el usuario deja el campo Concepto vacío e intenta guardar, entonces aparece el mensaje de validación y no se crea ningún registro.

- [ ] **Validación: monto cero o negativo:** Dado el modal de nuevo movimiento abierto, cuando el usuario ingresa 0 o un valor negativo en Monto e intenta guardar, entonces aparece el mensaje de validación y no se crea ningún registro.

- [ ] **Movimiento automático por venta contado:** Dado que se cierra una venta con `IsFiado = false` en PuntoDeVenta, cuando el usuario navega a Finanzas, entonces aparece un movimiento de tipo Ingreso con descripción `"Venta #<N>"` y columna Referencia mostrando "Venta Ref."

- [ ] **Venta al fiado no genera movimiento:** Dado que se cierra una venta con `IsFiado = true`, cuando el usuario navega a Finanzas, entonces NO aparece ningún movimiento nuevo correspondiente a esa venta.

- [ ] **Movimiento automático por cobro C/C:** Dado que el usuario salda la deuda de un cliente desde el módulo Clientes, cuando navega a Finanzas, entonces aparece un movimiento Ingreso con descripción `"Cobro C/C - <NombreCliente>"` y Referencia "-".

- [ ] **Búsqueda por texto:** Dado que hay movimientos con distintas descripciones, cuando el usuario escribe un término en el buscador y espera 400 ms, entonces la lista muestra solo movimientos cuya descripción contiene el término (case-insensitive) y los KPIs no cambian.

- [ ] **Búsqueda sin resultados:** Dado una búsqueda con término sin coincidencias, cuando se aplica el filtro, entonces la tabla muestra el estado vacío (o lista vacía) sin errores.

- [ ] **Debounce de búsqueda:** Dado que el usuario escribe rápido varios caracteres, cuando cada tecla dispara la función, entonces solo se ejecuta la consulta a la base de datos una vez, 400 ms después de la última tecla.

- [ ] **Columna Referencia:** Dado un movimiento con `VentaId` populado, cuando se muestra en la tabla, entonces la columna Referencia muestra "Venta Ref.". Dado un movimiento sin `VentaId`, entonces muestra "-".

- [ ] **Balance negativo visible:** Dado que los egresos superan a los ingresos, cuando se carga la pantalla, entonces el Balance Neto muestra un valor negativo sin errores, con el mismo formato de moneda.

- [ ] **Cancelar modal:** Dado el modal abierto, cuando el usuario hace clic en "Cancelar" o fuera del modal, entonces el modal se cierra sin crear ningún registro y el formulario se resetea.

- [ ] **Exportación a Excel:** Dado que se exporta el libro de caja desde el módulo Reportes, entonces el archivo `.xlsx` contiene una fila por cada movimiento (columnas: Fecha y Hora, Concepto, Tipo, Ingreso/Egreso con signo), y la última fila contiene la fórmula de Balance Neto.

- [ ] **Dashboard - Actividad Reciente:** Dado que existen movimientos registrados hoy, cuando se carga el Dashboard, entonces se muestran hasta 10 movimientos del día actual en la sección "Actividad Reciente".

---

## 10. Casos borde y manejo de errores

### Monto igual a cero
- El atributo `[Range(0.01, double.MaxValue)]` sobre `Amount` en el modelo impide montos ≤ 0 a nivel de validación de formulario.
- El guard adicional en `Save()` (`nuevaTransaccion.Amount <= 0 → return`) actúa como segunda barrera.
- No se permite crear movimientos con monto cero. Si en el futuro se requieren movimientos de ajuste a cero, será necesario modificar ambas validaciones.

### Balance histórico negativo
- Es un estado válido del sistema (más egresos que ingresos acumulados).
- `BalanceTotal` es una propiedad calculada `TotalIngresos - TotalEgresos` que acepta valores negativos de tipo `decimal`.
- La UI muestra el valor sin alterar formato; no hay colores especiales ni alertas para el balance negativo en la implementación actual.
- Acción sugerida futura: mostrar el KPI de Balance en rojo cuando es negativo.

### Eliminación de movimiento manual
- `DeleteMovimientoAsync(Guid id)` hace un hard delete: si el Id no existe, el método retorna silenciosamente sin error (el `FindAsync` devuelve null y el bloque `if` no ejecuta).
- La pantalla actual no expone botón de eliminación. Si se implementa, debe:
  1. Mostrar el botón solo en filas donde `VentaId` es nulo.
  2. Pedir confirmación modal antes de ejecutar.
  3. Recalcular los KPIs tras la eliminación.
- No existe papelera o posibilidad de deshacer.

### Movimiento de venta anulada
- Si una `Venta` es anulada (marcada `IsDeleted = true`), el `MovimientoFinanciero` asociado con ese `VentaId` **permanece** en la base de datos y continúa sumando al balance.
- Esto crea una inconsistencia: el libro de caja refleja un ingreso por una venta que el sistema considera eliminada.
- Mitigación actual: ninguna. El dueño debe detectarlo manualmente y registrar un egreso compensatorio.
- Solución futura propuesta: al anular una venta, eliminar o compensar automáticamente el movimiento financiero vinculado (ver sección 11).

### Error al cargar datos
- Si `LoadData()` lanza una excepción, se captura en el `catch`, se loguea con `Debug.WriteLine` y se muestra una notificación de error via `NotificationService`. La lista queda en el estado anterior o vacía; los KPIs quedan en 0.
- No hay reintentos automáticos.

### Error al guardar movimiento
- Si `AddMovimientoAsync` lanza una excepción, el modal permanece abierto, se muestra notificación de error y el movimiento no se guarda. El usuario puede reintentar.

### Concurrencia
- Al ser una aplicación single-user de escritorio, no hay concurrencia real entre sesiones. Sin embargo, si se abre la pantalla en múltiples instancias de la app, los KPIs pueden quedar desactualizados hasta la próxima recarga manual.

### Descripción con caracteres especiales
- El campo acepta cualquier texto hasta 500 caracteres. No hay sanitización especial ya que la entrada no se renderiza como HTML (es Blazor Server en MAUI).

### Dataset grande (performance)
- `GetTotalesMovimientosAsync()` carga todos los montos de la tabla en memoria para sumarlos (`.ToListAsync().Sum()`). Con miles de registros esto puede ser lento. Alternativa futura: usar `SumAsync()` directamente en la query.

---

## 11. Preguntas abiertas

1. **Compensación por venta anulada:** ¿Cuándo se anula una venta desde el módulo de Ventas, debe eliminarse o revertirse automáticamente el `MovimientoFinanciero` vinculado? ¿O se crea un egreso compensatorio con descripción "Anulación Venta #N"? Definir comportamiento esperado.

2. **Eliminación de movimientos manuales:** ¿Se debe implementar el botón de eliminar en la tabla para movimientos sin `VentaId`? ¿Solo para movimientos creados manualmente o también para cobros C/C? Confirmar alcance y si se necesita auditoría (quién/cuándo eliminó).

3. **Filtros adicionales:** ¿Se requieren filtros por rango de fechas o por tipo (Ingreso/Egreso) en esta versión? Actualmente solo existe búsqueda por texto. Implementarlos requeriría extender `GetMovimientosPaginadosAsync` y agregar controles de UI.

4. **Activación de paginación:** El componente `AppPagination` está comentado en el markup (`@* <AppPagination ... /> *@`). ¿Debe activarse? La lógica de servidor (`GetMovimientosPaginadosAsync`) ya soporta paginación.

5. **Balance negativo — alertas:** ¿El sistema debe mostrar alguna alerta visual o notificación cuando el balance neto es negativo? Actualmente no hay diferenciación visual.

6. **Performance de totales:** `GetTotalesMovimientosAsync()` usa `.ToListAsync().Sum()` en lugar de `SumAsync()`. Para datasets de más de 10.000 registros puede degradar. ¿Se refactoriza ahora o se deja para cuando sea un problema real?

7. **Auditoría:** ¿Se requiere registrar quién creó o eliminó un movimiento? El sistema actual no tiene usuarios, pero si en el futuro se agrega multiusuario, será necesario agregar campos `CreadoPor` / `EliminadoPor` al modelo.

8. **Cobros de C/C con referencia:** Los cobros de cuenta corriente generan un movimiento con `VentaId = null`, por lo que la columna Referencia muestra "-". ¿Se desea agregar una referencia al cliente o a la cuenta corriente en estos movimientos (ej: campo `ClienteId` opcional en `MovimientoFinanciero`)?
