# Spec: Análisis de Rotación e Historial de Precios

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El dueño del negocio quiere entender qué productos se venden bien y cuáles son capital inmovilizado. Necesita ver un análisis de rotación anual de cada producto: cuántas veces 'da la vuelta' el inventario en un año, en qué tendencia va (subiendo o bajando), cuánto margen deja, cuánto dinero tiene dormido en ese producto y cuántos días lleva sin venderse. Con esa información, el sistema le recomienda qué hacer: liquidar, promocionar o mantener. También necesita ver el historial de todos los cambios de precios que se hicieron en el pasado como auditoría."

## 2. Objetivo

Brindar al dueño del negocio una vista unificada del rendimiento comercial de cada producto del inventario, combinando métricas de rotación anual, tendencia de ventas, margen y capital inmovilizado con una recomendación de acción automática. Complementariamente, ofrece una bitácora de auditoría de todos los cambios de precio históricos registrados en el sistema. El objetivo es que el usuario pueda tomar decisiones de liquidación, promoción o mantenimiento sin necesidad de exportar datos ni hacer cálculos manuales.

## 3. Alcance

### Incluye
- Cálculo y visualización de la rotación anual del inventario global (tarjeta resumen).
- Tabla de rotación por producto con los campos: nombre, categoría, unidades vendidas en 12 meses, stock actual, rotación anual, valor inmovilizado, margen unitario, tendencia, última venta, días sin venta, estado semántico y acción sugerida.
- Clasificación automática de cada producto en uno de cuatro estados: Alta, Media, Baja o Sin rotación, usando umbrales configurables.
- Indicador de tendencia de ventas basado en comparación trimestral (últimos 3 meses vs. trimestre anterior).
- Recomendación comercial automática por producto derivada del estado de rotación.
- Filtros de la tabla de rotación: búsqueda por nombre, filtro por categoría, checkbox "Solo baja rotación".
- Alerta visual cuando `DiasSinVenta > DiasAlertaSinVenta` (default 90 días), configurable en `ConfiguracionApp`.
- Exportación de la tabla de rotación a Excel (.xlsx) con 12 columnas y filas coloreadas por nivel de riesgo.
- Historial de variación de precios: bitácora de todos los cambios de precio registrados, con fecha, producto afectado, precio anterior, precio nuevo y porcentaje de variación con indicadores ↑/↓.
- Búsqueda en el historial por nombre de producto o por fecha (formato dd/mm/aaaa).
- La pantalla se implementa en el componente `VariacionPrecios.razor` bajo la ruta `/variacion-precios`.

### No incluye (fuera de alcance)
- Modificación de precios desde esta pantalla (es solo lectura en ambas secciones para el historial; las acciones comerciales son notificaciones informativas, no actualizan la base de datos).
- Edición o eliminación de registros del historial de precios.
- Configuración de los umbrales directamente desde esta pantalla (se gestiona en `Configuracion.razor`).
- Proyecciones o modelos predictivos de ventas futuras.
- Paginación del historial de precios (se muestra todo el historial filtrado).
- Paginación de la tabla de rotación (actualmente comentada en la UI; el límite por defecto es 200 registros para la vista y 1000 para la exportación).
- Notificaciones push o alertas automáticas por correo sobre productos sin rotación.

## 4. Definiciones funcionales

### Rotación anual del inventario global
- Se calcula como: `Σ(unidades vendidas en los últimos 365 días) / Σ(stock actual de todos los productos no eliminados)`.
- Si el stock global es 0 o negativo, el resultado es 0 (no hay error; se muestra "0.00 veces/año").
- Se muestra redondeado a 2 decimales en la tarjeta de resumen.

### Rotación por producto
- Fórmula: `Rotación = UnidadesVendidas12m / StockActual`. Cuando `StockActual <= 0`, la rotación del producto es 0 (no se divide por cero; se usa `Math.Max(1, stock)` solo cuando el stock es positivo).
- El período de análisis son exactamente los 12 meses previos a la fecha actual (`DateTime.Today.AddYears(-1)`).
- El stock considerado es `Max(0, p.Stock)` para evitar valores negativos.
- Se excluyen los productos marcados como eliminados (`IsDeleted = true`).

### Estados semánticos de rotación
- **Alta:** `Rotación >= UmbralRotacionMedia` (default 4.0) — producto con alta demanda.
- **Media:** `UmbralRotacionBaja <= Rotación < UmbralRotacionMedia` (default 1.0 a 4.0) — rotación aceptable.
- **Baja:** `0 < Rotación < UmbralRotacionBaja` (default < 1.0) — capital inmovilizado.
- **Sin rotación:** `Rotación = 0` (sin ventas en los últimos 12 meses o stock = 0).
- Los umbrales son configurables en `ConfiguracionApp` (`UmbralRotacionBaja`, `UmbralRotacionMedia`). Si ambos umbrales se configuran en 0, todo producto con rotación > 0 cae en "Alta" y los de rotación = 0 quedan en "Sin rotación".

### Tendencia de ventas
- Se compara el volumen vendido en los últimos 3 meses (`desde3 = hoy - 3 meses`) vs. el trimestre anterior (`desde6 a desde3`).
- **↗ (subiendo):** ventas del último trimestre > ventas del trimestre anterior.
- **↘ (bajando):** ventas del último trimestre < ventas del trimestre anterior.
- **→ (estable):** ventas del último trimestre = ventas del trimestre anterior (incluyendo ambos en 0).

### Margen unitario
- Fórmula: `(PrecioVenta - PrecioCosto) / PrecioVenta`. Si `PrecioVenta = 0`, el margen es 0.
- Se usa el `Price` actual del producto (precio de venta) y `PrecioCosto` registrado en el producto.
- Se muestra como porcentaje con 1 decimal (ej: "35.2%"). Puede ser negativo si el costo supera el precio de venta; en ese caso se resalta en rojo.

### Valor inmovilizado
- Fórmula: `StockActual × PrecioCosto`.
- Representa el capital invertido que no está rotando.
- Se muestra en formato moneda (ej: "$ 12.500,00").

### Días sin venta
- Si el producto tiene ventas en el historial: días transcurridos desde la fecha de la última venta (`DateTime.Today - UltimaVenta.Date`).
- Si el producto no tiene ninguna venta registrada: se asigna el valor centinela `9999` (se muestra como "—" en la columna "Última venta"; el campo `DiasSinVenta` toma 9999 para que figure primero en el ordenamiento de peor rotación).
- La alerta de días sin venta se activa cuando `DiasSinVenta > DiasAlertaSinVenta` (default 90). Esta configuración es informativa; en la versión actual la alerta es implícita a través del color de la fila, no un ícono adicional.

### Recomendación comercial (AccionSugerida)
- **Sin rotación** → "Descontinuar / limpiar stock"
- **Baja** → "Promocionar o ajustar precio"
- **Media** → "Monitorear"
- **Alta** → "Mantener"
- La recomendación es informativa. Los botones de acción en la tabla ("Promoción", "Ajustar", "Descontinuar") muestran una notificación toast pero no realizan ninguna modificación en la base de datos.

### Historial de variación de precios
- Se registra automáticamente cada vez que se modifica el precio de un producto (edición individual, actualización masiva por porcentaje, importación CSV) siempre que el precio nuevo sea distinto al anterior.
- Campos almacenados por registro: `ProductoId`, `ProductoNombre`, `FechaModificacion` (con hora), `PrecioAnterior`, `PrecioNuevo`.
- El porcentaje de variación se calcula en la UI: `(PrecioNuevo - PrecioAnterior) / PrecioAnterior × 100`. Si `PrecioAnterior = 0`, el porcentaje es 0.
- La lista se ordena cronológicamente descendente (más reciente primero).
- Es solo lectura; no se puede editar ni eliminar desde esta pantalla.
- Si un producto nuevo nunca tuvo cambio de precio, no aparece en el historial (no se genera entrada al crear el producto).

### Filtros y ordenamiento de la tabla de rotación
- Los filtros de búsqueda por nombre, categoría y "solo baja rotación" son acumulables.
- El resultado se ordena por `Rotación ASC`, luego por `DiasSinVenta DESC` (los peores primero).
- El filtro "solo baja rotación" incluye los estados "Baja" y "Sin rotación".

### Exportación a Excel
- Se exporta el conjunto filtrado actualmente visible (con los mismos filtros aplicados), con un límite de 1000 registros.
- El nombre de archivo sigue el patrón: `RotacionProductos_YYYYMMDD_HHmm.xlsx`.
- La hoja se llama "Rotación".
- Si el usuario cancela el diálogo del sistema operativo para guardar el archivo, no se muestra error.

## 5. Datos y modelo

### Entidad `ConfiguracionApp` (tabla `Configuraciones`)
| Campo               | Tipo    | Default | Descripción                              |
|---------------------|---------|---------|------------------------------------------|
| Id                  | Guid    | —       | PK                                       |
| UmbralRotacionBaja  | decimal | 1.0     | Límite inferior del estado "Baja"        |
| UmbralRotacionMedia | decimal | 4.0     | Límite inferior del estado "Alta"        |
| DiasAlertaSinVenta  | int     | 90      | Días a partir de los cuales se considera crítico |

### Entidad `HistorialPrecio` (tabla `HistorialPrecios`)
| Campo              | Tipo     | Restricciones        | Descripción                              |
|--------------------|----------|----------------------|------------------------------------------|
| Id                 | Guid     | PK                   | Identificador único                      |
| ProductoId         | Guid     | Required             | FK al producto (sin navegación directa)  |
| ProductoNombre     | string   | Required, MaxLen 200 | Nombre del producto al momento del cambio|
| FechaModificacion  | DateTime | —                    | Timestamp del cambio (con hora local)    |
| PrecioAnterior     | decimal  | —                    | Precio antes del cambio                  |
| PrecioNuevo        | decimal  | —                    | Precio después del cambio                |

### DTO `RotacionProductoDto` (sin persistencia — calculado en memoria)
| Campo               | Tipo      | Descripción                                                      |
|---------------------|-----------|------------------------------------------------------------------|
| ProductoId          | Guid      | FK al producto                                                   |
| Nombre              | string    | Nombre del producto                                              |
| Categoria           | string    | Nombre de la categoría (resuelto desde tabla Categorias)         |
| UnidadesVendidas12m | int       | Suma de cantidades vendidas en los últimos 12 meses              |
| StockActual         | decimal   | Stock actual del producto (mínimo 0)                             |
| Rotacion            | decimal   | Calculado: `UnidadesVendidas12m / Max(1, StockActual)` o 0 si stock=0 |
| UltimaVenta         | DateTime? | Fecha de la venta más reciente; null si no hay ventas            |
| DiasSinVenta        | int       | Días desde última venta o 9999 si nunca hubo ventas              |
| ValorInmovilizado   | decimal   | `StockActual × PrecioCosto`, redondeado a 2 decimales            |
| MargenUnitario      | decimal   | `(Price - PrecioCosto) / Price`, o 0 si Price=0                  |
| Tendencia           | string    | "↗", "→" o "↘" según comparación trimestral                     |
| EstadoRotacion      | string    | "Alta" / "Media" / "Baja" / "Sin rotación"                       |
| AccionSugerida      | string    | Recomendación derivada del estado                                |

### Columnas del Excel exportado (en orden)
1. Producto
2. Categoría
3. Ventas 12m
4. Stock
5. Rotación (formato 0.00)
6. Valor Inmovilizado (formato $ #,##0.00)
7. Margen (formato 0.0%)
8. Tendencia
9. Última Venta (dd/MM/yyyy o "—")
10. Días sin Venta
11. Estado
12. Acción Sugerida

### Colores de fila en Excel
| Estado       | Color de fondo |
|--------------|---------------|
| Alta         | #d4edda (verde)|
| Media        | #cce5ff (azul) |
| Baja         | #fff3cd (amarillo)|
| Sin rotación | #f8d7da (rojo) |

## 6. UX / Interfaz

### Pantalla `VariacionPrecios.razor` — ruta `/variacion-precios`

La pantalla tiene dos secciones principales:

**Sección 1: Análisis de rotación**

- **Tarjeta resumen (superior):** muestra la rotación anual del inventario global en formato "X.XX veces/año". Tiene borde izquierdo primario, ícono de ciclo (bi-arrow-repeat) y etiqueta "Métrica Anual".
- **Tabla de rotación por producto:** debajo de la tarjeta. Encabezado con título, subtítulo descriptivo y controles de filtro alineados a la derecha en pantallas medianas/grandes y en columna en móvil.
  - Campo de búsqueda (por nombre del producto).
  - Selector de categoría (incluye opción "Todas las categorías").
  - Checkbox "Solo baja rotación".
  - Botón "Exportar Excel" (ícono bi-file-earmark-excel).
- **Tabla:** columnas Producto, Categoría, Ventas 12m, Stock, Rotación, Valor inmovilizado, Margen, Tendencia, Última venta, Estado, Acción.
  - El "Estado" se muestra como badge de color (verde/azul/amarillo/rojo) con el texto del estado.
  - El "Margen" se muestra en rojo si es negativo.
  - La columna "Acción" contiene tres botones: "Promoción", "Ajustar", "Descontinuar" — al hacer clic muestran un toast de confirmación.
  - Si no hay resultados con los filtros actuales, se muestra "Sin datos con los filtros actuales." centrado.

**Sección 2: Historial de variaciones de precios**

- Tarjeta con título "Historial de Variaciones" e ícono bi-clock-history.
- Campo de búsqueda a la derecha del encabezado (placeholder: "Buscar por producto o fecha (dd/mm/aaaa)...").
- Tabla de solo lectura con columnas: Fecha (dd/MM/yyyy HH:mm), Producto, Precio Anterior, Precio Nuevo, Variación.
  - La variación se muestra como badge: verde con ↑ si el precio subió, rojo con ↓ si bajó, con el porcentaje en valor absoluto.
- Si no hay resultados, se muestra un mensaje con ícono bi-info-circle.

**Estados de la interfaz:**
- **Carga inicial:** `OnInitializedAsync` carga en paralelo la rotación global, el historial de precios, las categorías y la tabla de rotación.
- **Sin datos en rotación:** mensaje centrado en la tabla.
- **Sin datos en historial:** mensaje centrado con ícono informativo.
- **Exportación exitosa:** toast "Excel exportado correctamente."
- **Error en exportación:** toast con mensaje de error.

## 7. Definiciones técnicas

### Stack y arquitectura
- **Framework:** .NET 8 / .NET MAUI Blazor Hybrid (aplicación de escritorio Windows).
- **UI:** Blazor Server-side rendering dentro de MAUI WebView; estilos con Tailwind CSS.
- **ORM:** Entity Framework Core con SQLite como base de datos local.
- **Excel:** ClosedXML (`XLWorkbook`) para generación del archivo .xlsx.
- **File picker:** `CommunityToolkit.Maui.Storage.FileSaver` para el diálogo de guardado nativo.

### Componente principal
- **Archivo:** `SistemaDeStockV3/Components/Pages/VariacionPrecios.razor`
- **Ruta:** `/variacion-precios`
- **Servicios inyectados:** `DataService`, `NotificationService`, `ReportService`

### Métodos del `DataService`
- `CalcularRotacionAnualAsync()` → `Task<double>`: calcula rotación global. Retorna 0 en error o stock vacío.
- `GetRotacionProductosAsync(Guid? categoriaId, string? search, bool soloBaja, int take)` → `Task<List<RotacionProductoDto>>`: carga en memoria todos los productos y ventas de los últimos 12 meses, calcula los DTOs por cada producto y aplica filtros en memoria. El cálculo de tendencia usa `desde3` (hoy - 3 meses) y `desde6` (hoy - 6 meses).
- `GetHistorialPreciosAsync()` → `Task<List<HistorialPrecio>>`: devuelve todos los registros ordenados por `FechaModificacion DESC`.
- `GetCategoriasAsync()` → `Task<List<Categoria>>`: lista de categorías para el selector.
- `RegistrarHistorialPrecio(Producto, decimal precioAnterior, decimal precioNuevo)`: método privado invocado por `SaveProductoAsync`, `ActualizarPreciosMasivosAsync` e importación CSV. Solo registra si el precio cambió.

### Método del `ReportService`
- `GenerateInventoryRotationReport(List<RotacionProductoDto> rotaciones)` → `byte[]`: genera el Excel en memoria y retorna los bytes. Crea la hoja "Rotación", escribe encabezados con fondo `#1d2442` y texto blanco, escribe los datos con color de fila según estado, y ajusta anchos de columna automáticamente.

### Comportamiento del filtro de búsqueda
- La búsqueda en la tabla de rotación es reactiva (se recarga `GetRotacionProductosAsync` en cada keystroke vía `@oninput`).
- La búsqueda en el historial es local (filtrado del lado del cliente sobre la colección ya cargada, usando `IEnumerable` computado).

### Paginación
- La tabla de rotación tiene un componente `<AppPagination>` comentado en el código actual. El comportamiento vigente es: carga hasta 200 registros, muestra 10 por página mediante `.Skip().Take()` sobre la colección en memoria.
- El historial no tiene paginación; se muestra la totalidad de los registros filtrados.

## 8. Seguridad y permisos

- La aplicación es de escritorio local, de un solo usuario (el dueño del negocio). No hay sistema de autenticación ni roles diferenciados en esta versión.
- La pantalla `/variacion-precios` es accesible para cualquier usuario que tenga la aplicación instalada.
- El historial de precios es de solo lectura desde esta pantalla; no existe endpoint ni botón que permita modificarlo o eliminarlo.
- Los botones de acción comercial ("Promoción", "Ajustar", "Descontinuar") no escriben en la base de datos; solo emiten notificaciones toast.
- La exportación a Excel se realiza localmente; el archivo se guarda en la ruta que el usuario elige mediante el diálogo nativo del sistema operativo.

## 9. Criterios de aceptación

### Análisis de rotación — tarjeta global
- [ ] Dado que existen ventas en los últimos 12 meses y stock positivo, cuando se abre la pantalla, entonces la tarjeta muestra la rotación global redondeada a 2 decimales con el sufijo "veces/año".
- [ ] Dado que el stock total es 0, cuando se abre la pantalla, entonces la tarjeta muestra "0.00 veces/año" sin error.
- [ ] Dado que no hay ventas en los últimos 12 meses (pero hay stock), cuando se abre la pantalla, entonces la tarjeta muestra "0.00 veces/año".

### Análisis de rotación — tabla por producto
- [ ] Dado que existen productos activos, cuando se carga la tabla sin filtros, entonces se muestran hasta 200 productos ordenados por rotación ascendente (peor primero) y luego por días sin venta descendente.
- [ ] Dado un producto con `StockActual = 0` y ventas en los últimos 12 meses, cuando se calcula su rotación, entonces `EstadoRotacion = "Sin rotación"` y `Rotacion = 0`.
- [ ] Dado un producto con `StockActual > 0` y `UnidadesVendidas12m = 0`, cuando se calcula su rotación, entonces `EstadoRotacion = "Sin rotación"`, `Rotacion = 0`.
- [ ] Dado un producto con `Rotacion >= UmbralRotacionMedia`, cuando se muestra en la tabla, entonces el badge de estado es verde con el texto "Alta".
- [ ] Dado un producto con `UmbralRotacionBaja <= Rotacion < UmbralRotacionMedia`, cuando se muestra en la tabla, entonces el badge es azul con el texto "Media".
- [ ] Dado un producto con `0 < Rotacion < UmbralRotacionBaja`, cuando se muestra en la tabla, entonces el badge es amarillo con el texto "Baja".
- [ ] Dado un producto con `Rotacion = 0`, cuando se muestra en la tabla, entonces el badge es rojo con el texto "Sin rotación".
- [ ] Dado un producto sin ninguna venta registrada, cuando se calcula `DiasSinVenta`, entonces el valor es 9999 y la columna "Última venta" muestra "—".
- [ ] Dado un producto con ventas, cuando se calcula `DiasSinVenta`, entonces es igual a `(hoy - fecha_última_venta).días`.
- [ ] Dado que las ventas del último trimestre son mayores que las del trimestre anterior, cuando se muestra el producto, entonces la tendencia es "↗".
- [ ] Dado que las ventas del último trimestre son menores que las del trimestre anterior, cuando se muestra el producto, entonces la tendencia es "↘".
- [ ] Dado que las ventas de ambos trimestres son iguales (incluyendo 0 en ambos), cuando se muestra el producto, entonces la tendencia es "→".
- [ ] Dado un producto con margen negativo (`PrecioCosto > Price`), cuando se muestra el margen, entonces se renderiza en color rojo.
- [ ] Dado un producto con `Price = 0`, cuando se calcula el margen, entonces `MargenUnitario = 0`.

### Filtros
- [ ] Dado que el usuario escribe un término en el campo de búsqueda, cuando se ingresa el texto, entonces la tabla se recarga mostrando solo productos cuyo nombre contiene el término (sin distinción de mayúsculas).
- [ ] Dado que el usuario selecciona una categoría en el selector, cuando cambia la selección, entonces la tabla muestra solo los productos de esa categoría.
- [ ] Dado que el usuario activa "Solo baja rotación", cuando se aplica el filtro, entonces la tabla muestra solo productos con estado "Baja" o "Sin rotación".
- [ ] Dado que no hay productos que coincidan con los filtros aplicados, cuando se renderiza la tabla, entonces se muestra el mensaje "Sin datos con los filtros actuales." sin errores de rendering.

### Acciones comerciales
- [ ] Dado cualquier producto en la tabla, cuando el usuario hace clic en "Promoción", "Ajustar" o "Descontinuar", entonces aparece un toast con el nombre del producto y la acción y no se modifica ningún dato en la base de datos.

### Recomendación comercial
- [ ] Dado un producto con estado "Sin rotación", entonces `AccionSugerida = "Descontinuar / limpiar stock"`.
- [ ] Dado un producto con estado "Baja", entonces `AccionSugerida = "Promocionar o ajustar precio"`.
- [ ] Dado un producto con estado "Media", entonces `AccionSugerida = "Monitorear"`.
- [ ] Dado un producto con estado "Alta", entonces `AccionSugerida = "Mantener"`.

### Exportación a Excel
- [ ] Dado que el usuario hace clic en "Exportar Excel", cuando elige una ruta válida, entonces se guarda un archivo .xlsx con nombre `RotacionProductos_YYYYMMDD_HHmm.xlsx`.
- [ ] Dado que el Excel fue generado, cuando se abre, entonces tiene exactamente 12 columnas (en el orden especificado en la sección 5) y una hoja llamada "Rotación".
- [ ] Dado que el Excel fue generado, cuando se inspeccionan las filas de datos, entonces cada fila tiene el color de fondo correspondiente al estado del producto (verde/azul/amarillo/rojo).
- [ ] Dado que el usuario cancela el diálogo de guardado, cuando se cancela, entonces no se muestra ningún mensaje de error.
- [ ] Dado que ocurre un error durante la generación o el guardado, cuando falla, entonces se muestra un toast de error con el mensaje de la excepción.
- [ ] Dado que hay filtros aplicados, cuando se exporta, entonces el Excel respeta los filtros activos y exporta hasta 1000 registros.

### Historial de variación de precios
- [ ] Dado que existen cambios de precio registrados, cuando se carga la pantalla, entonces la tabla de historial muestra los registros ordenados por fecha descendente.
- [ ] Dado que el precio nuevo es mayor al anterior, cuando se muestra la variación, entonces el badge es verde con ícono ↑ y el porcentaje en valor absoluto con 1 decimal.
- [ ] Dado que el precio nuevo es menor al anterior, cuando se muestra la variación, entonces el badge es rojo con ícono ↓ y el porcentaje en valor absoluto con 1 decimal.
- [ ] Dado que `PrecioAnterior = 0`, cuando se calcula la variación, entonces el porcentaje es 0% sin error de división.
- [ ] Dado que el usuario escribe en el campo de búsqueda del historial, cuando ingresa un nombre de producto, entonces se filtran los registros que contienen ese texto (sin distinción de mayúsculas).
- [ ] Dado que el usuario escribe una fecha en formato "dd/mm/aaaa", cuando se filtra, entonces se muestran solo los registros de esa fecha.
- [ ] Dado que no hay registros que coincidan con la búsqueda, cuando se renderiza la tabla, entonces se muestra el mensaje con ícono bi-info-circle.
- [ ] Dado que un producto fue creado pero su precio nunca fue modificado, cuando se consulta el historial, entonces ese producto no aparece.

## 10. Casos borde y manejo de errores

- **Stock = 0 (división por cero):** la fórmula de rotación retorna 0 directamente (no usa el denominador). El estado queda como "Sin rotación". No hay excepción de runtime.
- **Stock negativo:** se normaliza a 0 mediante `Math.Max(0, p.Stock)` antes de cualquier cálculo.
- **Producto sin ventas históricas:** `UltimaVenta = null`, `DiasSinVenta = 9999`, `UnidadesVendidas12m = 0`, `Rotacion = 0`, `EstadoRotacion = "Sin rotación"`. Se muestra "—" en la columna "Última venta".
- **Producto nuevo sin historial de precios:** no aparece en la tabla del historial de variaciones. Es el comportamiento correcto dado que el historial solo registra cambios, no creaciones.
- **Umbrales configurados en 0 (`UmbralRotacionBaja = 0`, `UmbralRotacionMedia = 0`):** cualquier producto con `Rotacion > 0` entra en el estado "Alta" (porque `rotacion >= 0` es siempre verdadero para positivos). Solo los de rotación exactamente 0 quedan en "Sin rotación". Este es un caso de uso límite; el sistema no valida que los umbrales sean mayores que 0; la validación de umbrales coherentes es responsabilidad de la pantalla de Configuración.
- **`UmbralRotacionBaja > UmbralRotacionMedia` (umbrales invertidos):** el sistema no detecta esta inconsistencia; la lógica de clasificación podría producir estados incorrectos. Debe validarse en la pantalla de Configuración (fuera de alcance de esta spec).
- **`PrecioAnterior = 0` en historial:** el porcentaje de variación se calcula como 0 (sin división por cero); el badge se muestra en verde (diff >= 0 si el nuevo precio es positivo) o rojo.
- **Error en `CalcularRotacionAnualAsync`:** el método captura cualquier excepción y retorna 0, logeando el error en `Debug.WriteLine`. La UI muestra "0.00 veces/año" sin indicar el error al usuario.
- **Error en `GetRotacionProductosAsync`:** si falla la carga, la lista `rotaciones` queda vacía y la tabla muestra el mensaje de sin datos. No hay manejo explícito de excepción en el componente; si la excepción no es capturada, Blazor puede mostrar un error genérico de página.
- **Error en exportación a Excel:** capturado en el bloque `try/catch` de `ExportarRotacion`; se muestra toast de error con el mensaje de la excepción. La cancelación del diálogo nativo se detecta comparando si el mensaje de excepción contiene "cancel" (case-insensitive); en ese caso no se muestra error.
- **Límite de registros:** la vista carga hasta 200 productos; la exportación hasta 1000. Si el negocio tiene más de 1000 productos con los filtros aplicados, los que excedan el límite no se exportan. No hay advertencia al usuario sobre este truncado.

## 11. Preguntas abiertas

1. **Límite de exportación sin advertencia:** cuando se exportan más de 1000 productos, el Excel se trunca silenciosamente. ¿Se debería agregar un toast de aviso ("Se exportaron los primeros 1000 registros") para que el usuario sepa que el resultado está incompleto?

2. **Paginación de la tabla de rotación:** el componente `<AppPagination>` está comentado. ¿Se activa en esta iteración o se mantiene el límite de 200 registros sin paginación visible?

3. **Valor inmovilizado con precio de costo vs. precio de venta:** actualmente `ValorInmovilizado = Stock × PrecioCosto`. ¿Es correcto usar el precio de costo o el dueño prefiere ver el valor a precio de venta (`Stock × Price`) para saber cuánto podría recuperar si vende el stock?

4. **DiasSinVenta = 9999 como centinela:** este valor se usa para ordenar los productos sin ventas al principio de la tabla. ¿Es correcto mostrarlo directamente en el Excel en la columna "Días sin Venta", o debería mostrarse como un texto como "N/A" para evitar confusión al leer el archivo?

5. **Alerta explícita de días sin venta:** `DiasAlertaSinVenta = 90` está configurado pero la alerta es solo implícita (color de fila rojo/amarillo). ¿Se requiere un ícono o badge adicional que resalte específicamente los productos que superan el umbral de días, independientemente del estado de rotación?

6. **Coherencia del nombre de producto en historial:** `ProductoNombre` se persiste al momento del cambio de precio. Si un producto es renombrado posteriormente, el historial muestra el nombre antiguo. ¿Es el comportamiento deseado (auditoría estricta del estado en el momento del cambio) o se prefiere mostrar siempre el nombre actual?
