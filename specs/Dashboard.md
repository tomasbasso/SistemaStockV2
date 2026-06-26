# Spec: Dashboard / Panel Principal

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El dueño del negocio abre la aplicación SistemaDeStockV3 y lo primero que ve es el Dashboard — su centro de control. Necesita ver de un vistazo el estado del negocio: cuánto vendió hoy, qué clientes le deben, cuánto vale su inventario, si hay productos con stock crítico. También necesita accesos rápidos para las operaciones más frecuentes (nueva venta, nuevo producto, registrar un gasto), y herramientas de análisis como el gráfico de ventas de los últimos 7 días, los productos más lentos en rotar y las alertas de stock mínimo."

---

## 2. Objetivo

El Dashboard es la pantalla de bienvenida de SistemaDeStockV3: provee al dueño del negocio un resumen ejecutivo del estado operativo y financiero actual, sin que deba navegar a otras secciones para obtener la información crítica del día. Reduce el tiempo de decisión al centralizar KPIs, alertas y accesos rápidos en una única vista, actuando como punto de entrada tanto para la consulta como para las operaciones más frecuentes (ventas, productos y gastos).

---

## 3. Alcance

### Incluye

- KPI de **Ventas del Día**: monto total y cantidad de transacciones del día calendario actual.
- KPI de **Deuda de Clientes**: suma de balances de cuentas corrientes (capital pendiente de cobro).
- KPI de **Valor de Inventario**: suma de `Stock × Precio público` para todos los productos activos.
- KPI de **Productos y Alertas**: total de productos activos + badge semáforo (rojo, amarillo, verde) según estado del stock.
- Botones de acceso rápido: Nueva Venta, Nuevo Producto, Gasto Manual.
- Panel **Evolución de Ingresos**: gráfico SVG de ventas de los últimos 7 días con tasas porcentuales de crecimiento diario (componente `DashboardChart.razor`).
- Panel **Top 5 de Baja Rotación**: productos con nula/baja rotación anual, calculados con datos de `VariacionPrecios`.
- Panel **Alertas de Stock**: tabla con scroll de productos cuyo stock actual ≤ stock mínimo configurado.
- Panel **Actividad Reciente**: últimos 10 movimientos financieros (ventas, cobros de CC, gastos) en orden cronológico descendente.
- **Buscador Global** (`GlobalSearchModal.razor`): activado por clic o `Ctrl+K`, debounce 500 ms, hasta 5 resultados con SKU/stock/precio, navegación con ↑↓ y Enter, cierre con Esc.
- Estados de carga, vacío y error para cada panel y KPI.
- Respeto del soft delete en todas las consultas (filtro global EF Core).

### No incluye (fuera de alcance)

- Edición de productos, clientes o movimientos directamente desde el Dashboard (se redirige a la sección correspondiente).
- Exportación o impresión del Dashboard.
- Configuración de umbrales (UmbralRotacionBaja, UmbralRotacionMedia, DiasAlertaSinVenta) desde esta pantalla; se gestionan en ConfiguracionApp.
- Notificaciones push o alertas en background fuera de la sesión activa.
- Multi-usuario o filtros por sucursal (sistema monousuario local).
- Actualización automática en tiempo real (polling/WebSocket); los datos se cargan al montar el componente y al navegar de regreso al Dashboard.
- Gráficos interactivos con zoom, filtro de fechas o exportación de imagen.

---

## 4. Definiciones funcionales

### 4.1 KPIs principales

**Ventas del Día**
- Se calcula como la suma de los importes totales de todas las transacciones de venta cuya fecha sea igual a la fecha calendario actual (hora local del dispositivo).
- Se muestran dos valores: el monto total en pesos (con formato de moneda argentina: `$ #.##0,00`) y la cantidad de transacciones (número entero).
- Si no hubo ventas en el día, ambos valores se muestran en cero (`$0,00` y `0 transacciones`) sin ocultar el KPI.

**Deuda de Clientes**
- Es la suma de los campos de balance de cuenta corriente de todos los clientes activos que tienen saldo pendiente (balance > 0).
- Clientes con balance = 0 o negativo no se computan.
- Se muestra como monto total en pesos. Si ningún cliente tiene deuda, se muestra `$0,00`.

**Valor de Inventario**
- Es la suma de `StockActual × PrecioPúblico` para cada producto activo (no eliminado por soft delete, StockActual ≥ 0).
- Se muestra en pesos con formato de moneda argentina.
- Productos con StockActual = 0 contribuyen con $0 y se incluyen en el cómputo (no alteran el total pero son válidos).

**Productos y Alertas (badge semáforo)**
- Se muestra el total de productos activos.
- El badge semáforo se calcula así:
  - **Rojo**: al menos un producto tiene `StockActual ≤ StockMínimo` (igual o menor al mínimo, incluyendo stock = 0 si el mínimo es 0 o mayor).
  - **Amarillo**: N/A — el semáforo tiene solo dos estados desde el punto de vista del badge en el KPI (rojo o verde). El panel de Alertas de Stock muestra el detalle con distinción de criticidad.
  - **Verde** ("Stock saludable"): ningún producto activo tiene `StockActual ≤ StockMínimo`.
- El badge muestra además la cantidad de productos en alerta cuando el estado es rojo (ej: "3 en alerta").

### 4.2 Accesos rápidos

| Botón | Acción |
|---|---|
| Nueva Venta | Navega a la pantalla `PuntoDeVenta` |
| Nuevo Producto | Abre el modal de creación dentro de la sección `Productos` |
| Gasto Manual | Registra un egreso directo en caja; abre un modal o formulario inline de registro de gasto |

- Los tres botones son visibles siempre, independientemente del estado de los datos.
- Gasto Manual requiere: descripción (texto libre, obligatorio), monto (decimal positivo, obligatorio), categoría (selección de lista, obligatoria) y fecha (por defecto hoy, editable).

### 4.3 Evolución de Ingresos

- Muestra las ventas de los últimos 7 días calendario (día actual inclusive) como puntos o barras en un SVG generado por `DashboardChart.razor`.
- Cada día muestra su monto total de ventas.
- Entre cada par de días consecutivos se calcula y muestra la tasa de crecimiento porcentual: `((día N - día N-1) / día N-1) × 100`. Si el día anterior tuvo monto $0, la tasa se muestra como "N/A" en lugar de un error de división por cero.
- Los días sin ventas se grafican con valor $0 (el punto/barra existe, no se omite).
- Si todos los días tienen ventas $0, el gráfico se muestra igualmente (línea plana en 0) con el mensaje "Sin ventas en los últimos 7 días" superpuesto.

### 4.4 Top 5 de Baja Rotación

- Lista los 5 productos activos con la rotación más baja (o nula) en el período anual, priorizando por menor rotación calculada con datos de `VariacionPrecios`.
- El índice de rotación se computa como: `UnidadesVendidasÚltimoAño / StockPromedio`. Si StockPromedio = 0, se trata como rotación 0.
- Los productos se consideran de "baja rotación" cuando su índice es ≤ `UmbralRotacionBaja` (por defecto 1.0 desde `ConfiguracionApp`).
- Se muestran: nombre del producto, SKU, stock actual, índice de rotación (1 decimal) y días sin venta.
- Un producto sin ninguna venta en el período se muestra con rotación = 0 y días sin venta = `DiasAlertaSinVenta` (90) o el valor real si se puede calcular.
- Si hay menos de 5 productos con baja rotación, se muestran solo los disponibles. Si no hay ninguno, se muestra el estado vacío: "Todos los productos tienen buena rotación".

### 4.5 Alertas de Stock

- Tabla con scroll vertical que lista todos los productos activos con `StockActual ≤ StockMínimo`.
- Columnas: Producto (nombre), SKU, Stock Actual, Stock Mínimo, Diferencia (StockMínimo − StockActual).
- Las filas se ordenan por mayor diferencia descendente (los más críticos primero).
- Si no hay productos en alerta, se muestra el mensaje "Todos los productos tienen stock saludable" con ícono verde.
- La tabla tiene altura máxima fija con scroll; no pagina.

### 4.6 Actividad Reciente

- Muestra los últimos 10 movimientos financieros en orden cronológico descendente (más reciente primero).
- Tipos de movimiento incluidos: ventas, cobros de cuentas corrientes, gastos manuales.
- Cada ítem muestra: tipo de movimiento (ícono + label), descripción/referencia, monto (positivo en verde para ingresos, negativo en rojo para egresos), fecha y hora.
- Si no hay actividad registrada, se muestra "Sin actividad reciente".

### 4.7 Buscador Global

- Se activa con clic en el campo de búsqueda en el header o con el atajo `Ctrl+K`.
- Abre `GlobalSearchModal.razor` como modal sobre el Dashboard.
- El usuario escribe texto; tras 500 ms de inactividad (debounce), se ejecuta la búsqueda.
- Busca en: nombre de producto, SKU, nombre de cliente.
- Muestra hasta 5 resultados con: tipo (Producto/Cliente), nombre, SKU (si aplica), stock actual (si aplica) y precio público (si aplica).
- Navegación: ↑↓ para moverse entre resultados, Enter para abrir el resultado seleccionado en su sección correspondiente, Esc para cerrar el modal.
- Si la búsqueda no devuelve resultados, se muestra "No se encontraron resultados para '[texto]'".
- El campo de búsqueda se limpia al cerrar el modal.

### 4.8 Configuración de umbrales

| Parámetro | Valor por defecto | Descripción |
|---|---|---|
| `UmbralRotacionBaja` | 1.0 | Índice de rotación por debajo del cual un producto se considera de baja rotación |
| `UmbralRotacionMedia` | 4.0 | Límite superior de rotación media (referencia para futuros análisis) |
| `DiasAlertaSinVenta` | 90 | Días sin ventas a partir de los cuales se considera un producto sin rotación |

Los umbrales se leen de `ConfiguracionApp` al montar el componente. Si la lectura falla, se usan los valores por defecto hardcodeados.

---

## 5. Datos y modelo

### Entidades involucradas

| Entidad | Campos clave utilizados | Notas |
|---|---|---|
| `Venta` | `Id`, `FechaVenta`, `Total`, `IsDeleted` | Filtro: `FechaVenta.Date == hoy` para KPI diario |
| `DetalleVenta` | `VentaId`, `ProductoId`, `Cantidad`, `Precio` | Para rotación anual |
| `Cliente` | `Id`, `Nombre`, `BalanceCuentaCorriente`, `IsDeleted` | Filtro: `BalanceCuentaCorriente > 0` |
| `MovimientoCuentaCorriente` | `Id`, `ClienteId`, `Monto`, `Fecha`, `Tipo`, `IsDeleted` | Para Actividad Reciente (cobros CC) |
| `Producto` | `Id`, `Nombre`, `SKU`, `StockActual`, `StockMinimo`, `PrecioPúblico`, `IsDeleted` | Soft delete activo |
| `VariacionPrecios` | `ProductoId`, `Fecha`, `StockAnterior`, `StockNuevo` | Para calcular rotación |
| `Gasto` | `Id`, `Descripcion`, `Monto`, `Categoria`, `Fecha`, `IsDeleted` | Para Actividad Reciente y KPI de deuda |
| `ConfiguracionApp` | `UmbralRotacionBaja`, `UmbralRotacionMedia`, `DiasAlertaSinVenta` | Singleton |

### Restricciones de datos

- Todas las consultas aplican el filtro global de EF Core (`IsDeleted == false`); no se necesita filtro explícito en cada query del Dashboard.
- `StockActual` puede ser 0 pero nunca negativo según las reglas de negocio (validación en capa de servicio).
- `PrecioPúblico` es siempre ≥ 0; si es 0, el producto contribuye $0 al Valor de Inventario.
- Los balances de cuentas corrientes pueden ser negativos (crédito a favor del cliente); solo se suman los positivos para la Deuda de Clientes.

### Cálculos derivados

- **Rotación anual**: `SUM(DetalleVenta.Cantidad WHERE FechaVenta >= HOY-365) / AVG(StockActual)`. El promedio de stock se aproxima con el valor actual por limitaciones del modelo; en versiones futuras podría usarse la media de `VariacionPrecios.StockNuevo`.
- **Tasa de crecimiento diario**: `((ventasDíaActual - ventasDíaAnterior) / ventasDíaAnterior) * 100`. Si `ventasDíaAnterior == 0`, resultado = "N/A".

---

## 6. UX / Interfaz

### Pantalla principal: `Home.razor`

#### Layout general

- Pantalla completa con scroll vertical.
- Header fijo con título "Dashboard", fecha actual y campo de buscador global (lupa + label "Buscar... Ctrl+K").
- Grilla de KPIs en la parte superior (4 tarjetas en fila en tablet/desktop; 2×2 o apiladas en móvil).
- Sección de accesos rápidos debajo de los KPIs (3 botones en fila).
- Paneles informativos en la mitad inferior: gráfico a ancho completo, luego grilla 2 columnas (Baja Rotación + Alertas de Stock lado a lado en pantallas anchas, apiladas en móvil), y Actividad Reciente debajo a ancho completo.

#### Estados por componente

**KPIs (todos)**
- **Cargando**: placeholder de esqueleto animado (shimmer) durante la consulta inicial.
- **Con datos**: valor numérico formateado con label descriptivo.
- **Sin datos (vacío)**: valor en cero con formato correcto (no se oculta el KPI).
- **Error**: texto "Error al cargar" + ícono de alerta. Sin reintentos automáticos; el usuario puede refrescar navegando y volviendo.

**Evolución de Ingresos**
- **Cargando**: placeholder de altura fija con shimmer.
- **Con datos**: gráfico SVG renderizado por `DashboardChart.razor`.
- **Todos los días en $0**: gráfico con línea plana + mensaje superpuesto "Sin ventas en los últimos 7 días".
- **Error**: "No se pudo cargar el gráfico de ventas."

**Top 5 de Baja Rotación**
- **Cargando**: 5 filas con shimmer.
- **Con resultados**: lista de hasta 5 ítems.
- **Sin productos de baja rotación**: "Todos los productos tienen buena rotación" + ícono verde de check.
- **Sin productos activos**: "No hay productos registrados."
- **Error**: "No se pudo calcular la rotación."

**Alertas de Stock**
- **Cargando**: shimmer de tabla.
- **Sin alertas**: "Todos los productos tienen stock saludable" + ícono de check verde. Badge del KPI en verde.
- **Con alertas**: tabla con scroll, badge del KPI en rojo con contador.
- **Error**: "No se pudieron cargar las alertas de stock."

**Actividad Reciente**
- **Cargando**: 10 filas con shimmer.
- **Sin actividad**: "Sin actividad reciente."
- **Con datos**: lista de hasta 10 ítems.
- **Error**: "No se pudo cargar la actividad reciente."

**Buscador Global (`GlobalSearchModal.razor`)**
- **Cerrado**: solo el campo en el header, no se muestra modal.
- **Abierto sin texto**: modal visible, campo vacío, sin resultados.
- **Buscando (debounce activo)**: spinner o indicador de carga sutil.
- **Con resultados**: hasta 5 ítems con highlight del término buscado.
- **Sin resultados**: "No se encontraron resultados para '[texto buscado]'".
- **Error de búsqueda**: "Error al buscar. Intentá de nuevo."

#### Flujos de usuario principales

1. El usuario abre la app → ve el Dashboard con KPIs en estado de carga → los paneles se van completando a medida que las queries terminan (carga independiente por panel).
2. El usuario detecta un producto con stock crítico en las Alertas → hace clic en el producto → navega a la sección Productos con ese producto seleccionado.
3. El usuario hace clic en "Nueva Venta" → el sistema navega a `PuntoDeVenta`.
4. El usuario presiona `Ctrl+K` → se abre `GlobalSearchModal` → escribe "lecha" → tras 500 ms aparecen hasta 5 resultados que contienen "lecha" → presiona ↓ para seleccionar → presiona Enter → navega al producto.
5. El usuario hace clic en "Gasto Manual" → se abre el modal de registro → completa descripción, monto y categoría → confirma → el gasto se registra y la Actividad Reciente se actualiza.

---

## 7. Definiciones técnicas

### Stack

| Capa | Tecnología |
|---|---|
| Frontend / UI | .NET 8 MAUI + Blazor Hybrid |
| Estilos | Tailwind CSS v4, tema Navy, dark mode |
| Gráficos | SVG generado en Razor (`DashboardChart.razor`) |
| ORM / Datos | Entity Framework Core con filtro global de soft delete |
| Base de datos | SQLite local (archivo en dispositivo) |

### Componentes Razor involucrados

| Componente | Responsabilidad |
|---|---|
| `Home.razor` | Pantalla principal del Dashboard; orquesta todos los paneles y KPIs |
| `DashboardChart.razor` | Renderiza el gráfico SVG de evolución de ingresos de los últimos 7 días |
| `GlobalSearchModal.razor` | Modal de búsqueda global con debounce y navegación por teclado |

### Carga de datos

- Cada panel carga sus datos de forma independiente al montar `Home.razor` (no hay un único request que bloquee toda la pantalla).
- Se usan estados booleanos por panel (`isLoading`, `hasError`) para controlar el render.
- No hay polling ni actualización automática; los datos se refrescan al abandonar y volver al Dashboard.
- El debounce del buscador es de 500 ms implementado con `CancellationToken` o `Task.Delay` en la capa de UI.

### Navegación

- `Nueva Venta` → `NavigationManager.NavigateTo("/punto-de-venta")` (o ruta equivalente).
- `Nuevo Producto` → `NavigationManager.NavigateTo("/productos?openModal=true")` (o parámetro de query que dispare el modal en el componente de Productos).
- Clic en producto de Alertas de Stock → `NavigationManager.NavigateTo($"/productos/{producto.Id}")`.
- Resultado del buscador → navega a la entidad seleccionada según su tipo (Producto o Cliente).

### Filtros de datos

- Todas las queries del Dashboard usan el filtro global EF Core de soft delete: no se agrega `.Where(x => !x.IsDeleted)` explícitamente en cada consulta del Dashboard.
- Las consultas de ventas del día filtran por `VentaId.FechaVenta.Date == DateTime.Today`.
- Las consultas de rotación filtran por `FechaVenta >= DateTime.Today.AddDays(-365)`.

---

## 8. Seguridad y permisos

- El sistema es de **uso monousuario local**: no existe autenticación ni gestión de sesiones; cualquier persona con acceso físico al dispositivo puede usar la app.
- No se implementan roles ni restricciones de acceso por funcionalidad en el Dashboard.
- Los datos residen en una base de datos SQLite local en el dispositivo; la seguridad física del dispositivo es responsabilidad del propietario.
- El Buscador Global no expone datos a servicios externos; toda la búsqueda es local.
- No se transmiten datos a servidores remotos desde el Dashboard.

---

## 9. Criterios de aceptación

### KPI — Ventas del Día

- [ ] Dado que existen ventas con fecha igual a hoy, cuando se carga el Dashboard, entonces el KPI muestra la suma correcta en pesos y la cantidad de transacciones.
- [ ] Dado que no hubo ventas hoy, cuando se carga el Dashboard, entonces el KPI muestra "$0,00" y "0 transacciones" (sin ocultar el KPI).
- [ ] Dado que hay un error al consultar ventas, cuando se carga el Dashboard, entonces el KPI muestra "Error al cargar" con ícono de alerta.
- [ ] Dado que el Dashboard está cargando, cuando las queries están en vuelo, entonces el KPI muestra un placeholder con shimmer animado.

### KPI — Deuda de Clientes

- [ ] Dado que existen clientes con BalanceCuentaCorriente > 0, cuando se carga el Dashboard, entonces el KPI muestra la suma de esos balances correctamente formateada.
- [ ] Dado que ningún cliente tiene saldo positivo, cuando se carga el Dashboard, entonces el KPI muestra "$0,00".
- [ ] Dado que un cliente tiene balance negativo (crédito a favor), cuando se calcula la deuda, entonces ese cliente no se incluye en la suma.

### KPI — Valor de Inventario

- [ ] Dado que existen productos activos con stock y precio, cuando se carga el Dashboard, entonces el KPI muestra la suma de `StockActual × PrecioPúblico` formateada en pesos.
- [ ] Dado que un producto tiene PrecioPúblico = 0 o StockActual = 0, cuando se calcula el inventario, entonces ese producto contribuye $0 sin causar errores.
- [ ] Dado que no hay productos activos, cuando se carga el Dashboard, entonces el KPI muestra "$0,00".

### KPI — Productos y Alertas (semáforo)

- [ ] Dado que al menos un producto activo tiene `StockActual ≤ StockMínimo`, cuando se carga el Dashboard, entonces el badge es rojo y muestra la cantidad de productos en alerta (ej: "3 en alerta").
- [ ] Dado que ningún producto tiene `StockActual ≤ StockMínimo`, cuando se carga el Dashboard, entonces el badge es verde y muestra "Stock saludable".
- [ ] Dado que el Dashboard está cargando, cuando las queries están en vuelo, entonces el badge muestra un estado neutral sin color definitivo.

### Accesos rápidos

- [ ] Dado que el usuario hace clic en "Nueva Venta", cuando el botón responde, entonces la app navega a la pantalla PuntoDeVenta.
- [ ] Dado que el usuario hace clic en "Nuevo Producto", cuando el botón responde, entonces la app navega a la sección Productos con el modal de creación abierto.
- [ ] Dado que el usuario hace clic en "Gasto Manual", cuando el botón responde, entonces se abre el formulario/modal de registro de gasto.
- [ ] Dado que el usuario completa y confirma un Gasto Manual (descripción, monto, categoría), cuando lo guarda, entonces el gasto se persiste y aparece en Actividad Reciente.
- [ ] Dado que el usuario intenta guardar un Gasto Manual con monto vacío o 0, cuando envía el formulario, entonces se muestra un error de validación y no se guarda.

### Evolución de Ingresos

- [ ] Dado que existen ventas en los últimos 7 días, cuando se carga el panel, entonces el gráfico SVG muestra un punto/barra por cada día con el monto correcto.
- [ ] Dado que un día no tuvo ventas, cuando se renderiza el gráfico, entonces ese día aparece con valor $0 (no se omite del eje).
- [ ] Dado que el día anterior tuvo ventas $0 y el día actual tuvo ventas, cuando se muestra la tasa de crecimiento, entonces el porcentaje entre esos dos días muestra "N/A" en lugar de un error.
- [ ] Dado que todos los días tienen ventas $0, cuando se renderiza el gráfico, entonces se muestra la línea plana con el mensaje "Sin ventas en los últimos 7 días".
- [ ] Dado que hay un error al cargar los datos del gráfico, cuando se renderiza el panel, entonces se muestra "No se pudo cargar el gráfico de ventas".

### Top 5 de Baja Rotación

- [ ] Dado que existen productos con índice de rotación ≤ UmbralRotacionBaja, cuando se carga el panel, entonces se muestran hasta 5 productos ordenados de menor a mayor rotación.
- [ ] Dado que ningún producto tiene baja rotación, cuando se carga el panel, entonces se muestra "Todos los productos tienen buena rotación" con ícono verde.
- [ ] Dado que un producto no tuvo ventas en el último año, cuando se calcula su rotación, entonces se muestra con rotación = 0.
- [ ] Dado que hay un error al calcular la rotación, cuando se carga el panel, entonces se muestra "No se pudo calcular la rotación".

### Alertas de Stock

- [ ] Dado que existen productos con `StockActual ≤ StockMínimo`, cuando se carga el panel, entonces la tabla los muestra ordenados por mayor diferencia (StockMínimo − StockActual) de forma descendente.
- [ ] Dado que no hay productos en alerta, cuando se carga el panel, entonces se muestra "Todos los productos tienen stock saludable" y el badge del KPI es verde.
- [ ] Dado que la tabla de alertas tiene más ítems de los visibles, cuando el usuario hace scroll dentro del panel, entonces puede ver todos los productos en alerta.
- [ ] Dado que hay un error al cargar las alertas, cuando se carga el panel, entonces se muestra "No se pudieron cargar las alertas de stock".

### Actividad Reciente

- [ ] Dado que existen movimientos financieros (ventas, cobros CC, gastos), cuando se carga el panel, entonces se muestran los últimos 10 en orden cronológico descendente.
- [ ] Dado que no hay movimientos registrados, cuando se carga el panel, entonces se muestra "Sin actividad reciente".
- [ ] Dado que un movimiento es un ingreso (venta, cobro), cuando se muestra en el panel, entonces el monto aparece en verde con signo positivo.
- [ ] Dado que un movimiento es un egreso (gasto), cuando se muestra en el panel, entonces el monto aparece en rojo con signo negativo.

### Buscador Global

- [ ] Dado que el usuario presiona `Ctrl+K` o hace clic en el campo de búsqueda del header, cuando el evento se procesa, entonces `GlobalSearchModal.razor` se abre y el foco va al campo de texto.
- [ ] Dado que el usuario escribe texto en el buscador, cuando pasan 500 ms sin nuevas teclas, entonces se ejecuta la búsqueda y se muestran hasta 5 resultados.
- [ ] Dado que el texto cambia antes de que pasen los 500 ms, cuando se vuelve a tipear, entonces el debounce se reinicia y la búsqueda anterior se cancela.
- [ ] Dado que la búsqueda devuelve resultados, cuando el usuario presiona ↓, entonces el foco se mueve al primer resultado; presionando ↓ nuevamente pasa al segundo, y así sucesivamente.
- [ ] Dado que un resultado está seleccionado y el usuario presiona Enter, cuando se procesa el evento, entonces la app navega a la entidad correspondiente y el modal se cierra.
- [ ] Dado que el usuario presiona Esc con el modal abierto, cuando se procesa el evento, entonces el modal se cierra y el campo de texto se limpia.
- [ ] Dado que la búsqueda no devuelve resultados, cuando se renderizan los resultados, entonces se muestra "No se encontraron resultados para '[texto]'".
- [ ] Dado que hay un error en la búsqueda, cuando ocurre la excepción, entonces se muestra "Error al buscar. Intentá de nuevo."

---

## 10. Casos borde y manejo de errores

- **Día sin ventas**: el KPI de Ventas del Día muestra $0,00 y "0 transacciones". No se oculta ni reemplaza por mensaje alternativo.
- **Sin clientes con deuda**: el KPI muestra $0,00. No requiere estado especial.
- **Sin productos activos**: los KPIs de inventario y alertas muestran $0,00 y badge verde. El Top 5 muestra "No hay productos registrados."
- **Producto con StockMínimo = 0 y StockActual = 0**: `StockActual (0) ≤ StockMínimo (0)` es verdadero, por lo que el producto aparece en alertas. Esto es comportamiento esperado.
- **División por cero en tasa de crecimiento**: si el día anterior tuvo ventas $0, la tasa se muestra como "N/A", nunca como `∞` ni error.
- **División por cero en rotación**: si el StockPromedio calculado es 0, la rotación se trata como 0.
- **UmbralRotacionBaja no configurado**: se usa el valor por defecto 1.0 sin lanzar excepción.
- **Base de datos bloqueada o corrupta**: cada panel muestra su estado de error individual. El Dashboard no rompe en cascada; los paneles que sí cargan correctamente se muestran normales.
- **Gasto Manual con monto = 0**: se rechaza en validación front-end con mensaje "El monto debe ser mayor a cero."
- **Gasto Manual sin categoría**: se rechaza con "Seleccioná una categoría."
- **Buscador con entrada vacía**: no se ejecuta la búsqueda; no se muestran resultados ni mensaje de error.
- **Buscador con solo espacios en blanco**: se trata como entrada vacía; no se ejecuta búsqueda.
- **Resultado del buscador apunta a entidad eliminada (soft delete)**: el filtro global de EF Core ya excluye estas entidades; no puede ocurrir en condiciones normales.
- **Timeout de query larga**: si una consulta supera un umbral razonable (ej: 10 segundos), el panel muestra estado de error sin bloquear los demás paneles.
- **Navegación a "Nuevo Producto" cuando Productos no está listo**: si el componente Productos no puede abrir el modal, la app navega de todas formas a la sección; la apertura del modal es best-effort.
- **Carga parcial de paneles**: si algunos paneles cargan y otros fallan, el Dashboard muestra los cargados normalmente y los fallidos con su estado de error individual.

---

## 11. Preguntas abiertas

1. **Refresco de datos**: ¿cuándo exactamente se recarga el Dashboard? ¿Solo al navegar de regreso, o también hay un botón de refresco manual? ¿Se actualiza si la app estuvo en background y vuelve al frente?
2. **Categorías de gasto**: ¿la lista de categorías para el formulario de Gasto Manual es fija (hardcodeada) o proviene de una tabla configurable en la base de datos?
3. **Comportamiento del semáforo amarillo**: el KPI de Productos muestra solo rojo/verde, pero `UmbralRotacionMedia` existe como parámetro. ¿Se usa el amarillo en algún futuro panel del Dashboard o es exclusivo para otra sección?
4. **Navegación desde Alertas de Stock**: ¿al hacer clic en un producto de la tabla de alertas, el sistema navega al detalle del producto en la sección Productos? ¿Ese comportamiento está ya implementado o es parte de este spec?
5. **Cantidad máxima visible en Alertas de Stock**: ¿la tabla tiene una altura fija con scroll o crece hasta llenar la pantalla? ¿Hay un límite de ítems cargados (ej: máximo 50 aunque haya más)?
6. **Formato de moneda**: ¿los KPIs usan símbolo `$` con separador de miles punto y decimal coma (estilo argentino: `$ 1.234,56`), o hay una configuración regional?
7. **Atribución de cobros de CC a Actividad Reciente**: ¿un cobro de cuenta corriente aparece como ingreso positivo? ¿El movimiento referencia al cliente?
8. **Performance con muchos productos**: si la base de datos tiene miles de productos, ¿la carga del Dashboard puede generar lentitud notable? ¿Se planea algún tipo de caché o precómputo?
