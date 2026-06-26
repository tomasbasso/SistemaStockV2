# Spec: Gestión de Inventario / Productos

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El negocio necesita administrar su catálogo de productos: cargar nuevos productos con su SKU, precio de costo, margen de ganancia y ubicación física en el local. El sistema debe calcular automáticamente el precio de venta a partir del costo y el margen. También necesita poder ajustar el stock de forma rápida sin abrir formularios complejos, aplicar aumentos o descuentos porcentuales masivos a grupos de productos, eliminar múltiples productos a la vez, e importar productos desde un archivo Excel."

---

## 2. Objetivo

Proveer al negocio una pantalla unificada para administrar el catálogo completo de productos: alta, edición, ajuste de stock rápido, actualización masiva de precios, eliminación múltiple e importación desde Excel. El sistema calcula el precio de venta automáticamente a partir del costo y el margen, y mantiene un historial de cambios de precio para auditoría. Resuelve la necesidad operativa de mantener el inventario actualizado de forma ágil sin depender de flujos de pantallas múltiples.

---

## 3. Alcance

### Incluye
- Listado/tabla de productos con búsqueda y filtros (nombre, SKU, categoría)
- Indicador visual de salud de stock (Sin stock / Stock bajo / Saludable)
- Ajuste de stock rápido inline (botones + y − en la fila de la tabla)
- Modal de creación y edición de producto con todos sus campos
- Cálculo bidireccional automático: PrecioCosto + Margen → PrecioVenta, y PrecioVenta → Margen
- Ajuste masivo de precios por porcentaje (incremento o reducción)
- Registro de historial de precio en la tabla `HistorialPrecio` al confirmar ajuste masivo
- Simulación en tiempo real del precio resultante antes de confirmar el ajuste masivo
- Eliminación múltiple con soft delete (IsDeleted = true) y confirmación previa
- Importación de productos desde archivo `.xlsx` con informe de resultado
- Integración con escaneo de código de barras: escanear en esta pantalla abre el modal de edición del producto correspondiente

### No incluye (fuera de alcance)
- Gestión de categorías (altas, bajas, edición de la tabla Categoría): se asume que ya existe un ABM separado
- Reportes o gráficos de evolución de precios (solo se almacena el historial)
- Exportación del catálogo a Excel u otro formato
- Integración con proveedores o sistemas externos de compra
- Auditoría de ajustes de stock manual (solo se auditan cambios de precio)
- Gestión de múltiples sucursales o ubicaciones de almacén
- Control de lotes o fechas de vencimiento

---

## 4. Definiciones funcionales

### 4.1 Indicadores de salud de stock

| Condición | Etiqueta | Color |
|---|---|---|
| StockActual = 0 | Sin stock | Rojo |
| StockActual > 0 y StockActual ≤ StockMínimo | Stock bajo | Amarillo |
| StockActual > StockMínimo | Saludable | Verde |

El indicador se muestra como badge en la columna de stock de la tabla.

### 4.2 Ajuste de stock rápido

- La tabla expone botones `+` y `−` directamente en cada fila, sin abrir modal.
- Cada clic modifica el `StockActual` en ±1 y persiste inmediatamente en la base de datos.
- No se permite que el stock quede en negativo: si `StockActual` es 0, el botón `−` queda deshabilitado.
- El indicador de salud se actualiza en tiempo real tras cada ajuste.

### 4.3 Cálculo bidireccional de precios

El cálculo sigue estas fórmulas:

```
PrecioVenta = PrecioCosto × (1 + MargenGanancia / 100)
MargenGanancia = ((PrecioVenta - PrecioCosto) / PrecioCosto) × 100
```

- Si el usuario modifica `PrecioCosto` o `MargenGanancia` → el sistema recalcula `PrecioVenta` automáticamente.
- Si el usuario modifica `PrecioVenta` directamente → el sistema recalcula `MargenGanancia` automáticamente usando el `PrecioCosto` actual.
- Caso especial: si `PrecioCosto = 0`, el margen es matemáticamente indefinido. En ese caso el campo `MargenGanancia` se muestra con valor `0` y se deshabilita el cálculo automático hasta que se ingrese un costo mayor a 0. El campo `PrecioVenta` puede editarse libremente cuando `PrecioCosto = 0`.
- Los precios se almacenan y muestran con hasta 2 decimales.

### 4.4 Ajuste masivo de precios

- El usuario filtra los productos por categoría y/o término de búsqueda, luego selecciona con checkboxes individuales o con "seleccionar todos".
- Ingresa un porcentaje (positivo = incremento, negativo = reducción) y el modal muestra en tiempo real el precio resultante de cada producto seleccionado antes de confirmar.
- Al confirmar, el sistema:
  1. Actualiza `PrecioVenta` de cada producto seleccionado aplicando el porcentaje sobre el precio actual.
  2. Recalcula y actualiza `MargenGanancia` para cada producto (excepto los que tienen `PrecioCosto = 0`, donde deja el margen en 0).
  3. Inserta un registro en `HistorialPrecio` por cada producto modificado.
- Si no hay ningún producto seleccionado al intentar confirmar, el sistema muestra un aviso "Seleccionás al menos un producto para aplicar el ajuste" y no ejecuta ninguna acción.
- El porcentaje puede tener hasta 2 decimales. El resultado final se redondea a 2 decimales.
- No existe límite máximo ni mínimo en el porcentaje ingresado, pero el precio resultante no puede quedar en negativo; si la reducción llevaría el precio a 0 o menos, el sistema muestra una advertencia por producto y lo excluye del ajuste.

### 4.5 Eliminación múltiple

- El usuario selecciona uno o más productos con checkboxes en la tabla y presiona "Eliminar seleccionados".
- El sistema muestra un modal de confirmación indicando la cantidad de productos a eliminar.
- Al confirmar, los registros se marcan con `IsDeleted = true` (soft delete); no se eliminan físicamente de la base de datos.
- Los productos con `IsDeleted = true` no aparecen en la tabla ni en búsquedas, ni están disponibles en ningún otro módulo del sistema.
- Si el usuario no tiene ningún producto seleccionado, el botón "Eliminar seleccionados" permanece deshabilitado.

### 4.6 Importación desde Excel (.xlsx)

- El archivo debe tener al menos las columnas: Columna A = SKU, Columna B = Nombre, Columna C = Precio (de venta). La primera fila se trata como encabezado y se ignora.
- El usuario selecciona una categoría por defecto para los productos nuevos que se creen durante la importación.
- Lógica fila por fila:
  - **SKU ya existente en la base de datos** → actualiza `Nombre` y `PrecioVenta`; recalcula `MargenGanancia` usando el `PrecioCosto` previo almacenado. Si el producto tiene `PrecioCosto = 0`, deja el margen en 0.
  - **SKU nuevo** → crea el producto con `StockActual = 5`, `StockMínimo = 0` por defecto, y la categoría seleccionada por el usuario.
- Una fila se considera con error si: SKU está vacío, Nombre está vacío, o el Precio no es un número válido mayor o igual a 0. Las filas con error no se procesan; el resto sí.
- Al finalizar la importación, el sistema muestra un informe con: cantidad de productos creados, cantidad de productos actualizados, cantidad de filas con error (indicando número de fila y motivo de cada error).
- Si el archivo no tiene columnas A, B y C, o el formato no es `.xlsx`, la importación se cancela inmediatamente con un mensaje de error antes de procesar ninguna fila.

### 4.7 Escaneo de código de barras

- Si el usuario escanea un código de barras mientras está en la pantalla de Productos, el sistema busca el producto por `CodigoBarras`.
  - Si encuentra el producto → abre directamente el modal de edición de ese producto.
  - Si no encuentra ningún producto con ese código → muestra un aviso "No se encontró ningún producto con ese código de barras."

---

## 5. Datos y modelo

### Entidades principales

#### Producto

| Campo | Tipo | Restricciones | Notas |
|---|---|---|---|
| Id | int | PK, autoincremental | |
| SKU | string | Único, requerido, no nulo | Identificador alfanumérico del producto |
| Nombre | string | Requerido, no nulo | |
| CategoriaId | int | FK → Categoria, requerido | |
| UnidadMedida | string | Requerido, default `"u."` | Ej: "u.", "kg", "lt" |
| Ubicacion | string | Max 100 chars, opcional | Ubicación física en el local |
| PrecioCosto | decimal | ≥ 0, requerido | Precio de costo del producto |
| MargenGanancia | decimal | ≥ 0, almacenado en % | Ej: `30` equivale a 30% |
| PrecioVenta | decimal | ≥ 0, requerido | Precio de venta al público |
| StockActual | int | ≥ 0, requerido | No puede quedar en negativo |
| StockMinimo | int | ≥ 0, default `0` | Umbral para alerta de stock bajo |
| CodigoBarras | string | Opcional, único si no nulo | |
| IsDeleted | bool | Default `false` | Soft delete |
| FechaCreacion | datetime | Auto, UTC | |
| FechaModificacion | datetime | Auto, UTC | Actualizado en cada cambio |

#### HistorialPrecio

| Campo | Tipo | Restricciones | Notas |
|---|---|---|---|
| Id | int | PK, autoincremental | |
| ProductoId | int | FK → Producto, requerido | |
| FechaCambio | datetime | UTC, auto | Momento del ajuste masivo |
| PrecioAnterior | decimal | ≥ 0 | Precio de venta antes del ajuste |
| PrecioNuevo | decimal | ≥ 0 | Precio de venta después del ajuste |
| VariacionPorcentual | decimal | | Porcentaje aplicado (puede ser negativo) |

### Relaciones

- `Producto` N:1 `Categoria` (FK `CategoriaId`)
- `HistorialPrecio` N:1 `Producto` (FK `ProductoId`)

### Restricciones de integridad

- SKU único en la tabla Producto (incluyendo registros con `IsDeleted = true`). Si un SKU fue eliminado (soft delete) y se intenta crear uno nuevo con el mismo SKU, el sistema lo rechaza y avisa que ese SKU ya existe (aunque el producto esté eliminado).
- `CodigoBarras` único entre los productos activos (`IsDeleted = false`). No se valida unicidad contra productos eliminados.

---

## 6. UX / Interfaz

### Pantalla principal: `Productos.razor`

**Layout general:**
- Barra superior con: campo de búsqueda (filtra por nombre, SKU o categoría en tiempo real), selector de categoría para filtrar, botones de acción: "Nuevo Producto", "Ajuste Masivo de Precios", "Importar Excel", "Eliminar seleccionados" (deshabilitado si no hay selección).
- Tabla de productos con columnas: checkbox de selección, SKU, Nombre, Categoría, Ubicación, Stock (con badge de salud de stock coloreado), PrecioVenta, botones de ajuste rápido `+` / `−`, botón "Editar".
- La tabla filtra en tiempo real conforme el usuario escribe en el buscador o cambia la categoría.

**Estados de la tabla:**
- **Vacío (sin productos):** mensaje "No hay productos cargados. Podés crear el primero con el botón 'Nuevo Producto'."
- **Sin resultados de búsqueda:** mensaje "No se encontraron productos para esa búsqueda."
- **Cargando:** indicador de carga (spinner) mientras se obtienen los datos.

### Modal de Creación / Edición

- Se abre al presionar "Nuevo Producto", "Editar" en una fila, o al escanear un código de barras existente.
- Campos en el modal: SKU, Nombre, Categoría (dropdown), Unidad de Medida, Ubicación, Precio de Costo, Margen de Ganancia (%), Precio de Venta, Stock Actual, Stock Mínimo, Código de Barras.
- Los campos PrecioCosto, MargenGanancia y PrecioVenta están enlazados: al modificar cualquiera de los tres, los otros se recalculan automáticamente según las reglas del §4.3.
- Al presionar "Guardar": valida los campos, muestra errores inline si hay, o persiste y cierra el modal.
- Al presionar "Cancelar" o cerrar el modal: descarta los cambios sin guardar (solicita confirmación si hubo modificaciones).

### Modal de Ajuste Masivo de Precios

- Encabezado: "Ajuste Masivo de Precios"
- Buscador + selector de categoría para filtrar la lista de productos mostrada en el modal.
- Checkbox "Seleccionar todos" y checkboxes individuales por fila.
- Campo: "Porcentaje de ajuste" (número, acepta decimales, positivo o negativo).
- Tabla con columnas: Nombre del producto, Precio actual, Precio resultante (calculado en tiempo real mientras el usuario escribe el porcentaje).
- Los productos cuyo precio resultante quedaría en 0 o negativo se marcan con advertencia y se excluyen al confirmar.
- Botones: "Confirmar ajuste" y "Cancelar".
- Estado vacío: si no hay productos en la lista (filtro sin resultados), muestra "No hay productos que coincidan con el filtro aplicado."

### Modal de Confirmación de Eliminación

- Texto: "¿Confirmar la eliminación de [N] producto/s? Esta acción no puede deshacerse desde la aplicación."
- Botones: "Eliminar" (acción destructiva, color rojo) y "Cancelar".

### Flujo de Importación Excel

- Al presionar "Importar Excel": se abre un selector de archivos filtrado a `.xlsx`.
- Luego se muestra un paso intermedio para que el usuario seleccione la categoría por defecto.
- Se muestra una barra de progreso mientras se procesa el archivo.
- Al finalizar, se muestra el informe de resultado (creados, actualizados, errores con detalle).
- Botón "Cerrar" para volver al listado, que se recarga automáticamente.

---

## 7. Definiciones técnicas

### Stack

- **Framework:** .NET 8 MAUI + Blazor Hybrid
- **Componente principal:** `Productos.razor`
- **ORM:** Entity Framework Core (asumido, coherente con el stack MAUI estándar del proyecto)
- **Base de datos:** SQLite local (asumido como base de datos embebida estándar para MAUI desktop)
- **Lectura de Excel:** ClosedXML o EPPlus (librería ya disponible en el proyecto por la presencia de `ExcelNumberFormat.dll` en los binarios)

### Arquitectura

- El componente `Productos.razor` consume un servicio inyectable (`IProductoService` o equivalente) que encapsula toda la lógica de negocio: CRUD de productos, ajuste masivo, importación.
- El cálculo bidireccional de precios ocurre en el componente Razor (lógica de UI) sin llamada al servidor.
- La persistencia de `HistorialPrecio` ocurre dentro de la misma transacción de base de datos que actualiza los precios en el ajuste masivo, para garantizar consistencia.
- El soft delete se implementa mediante un filtro global de EF Core que excluye automáticamente los registros con `IsDeleted = true` de todas las consultas, salvo las explícitas de administración.

### Importación Excel

- Se usa la librería de Excel disponible (ClosedXML/EPPlus) para leer el archivo en memoria.
- El procesamiento es fila por fila; cada fila se intenta persistir de forma independiente para que los errores en una fila no bloqueen el procesamiento del resto.
- La importación completa se ejecuta en una única transacción de base de datos: si la transacción falla globalmente (error de infraestructura), se hace rollback total. Los errores de validación por fila no hacen rollback; esa fila simplemente se registra como error en el informe.

### Escaneo de código de barras

- El input de escaneo se captura mediante un campo de texto oculto con autofoco, ya que los lectores de código de barras emulan teclado. El componente escucha el evento `onkeydown` para detectar el `Enter` que envía el lector al finalizar el escaneo.

---

## 8. Seguridad y permisos

- El sistema SistemaDeStockV3 es una aplicación de escritorio de uso local (MAUI), sin autenticación de usuarios definida en esta feature.
- Todos los usuarios que accedan a la aplicación tienen acceso completo a la pantalla de Productos.
- No se implementa control de roles diferenciado para esta pantalla en el alcance de esta especificación.
- Los datos residen en la base de datos SQLite local del dispositivo; la seguridad física del equipo es responsabilidad del operador del negocio.

> Si en el futuro se implementa un módulo de roles, las operaciones de "Eliminar", "Ajuste Masivo" e "Importar Excel" deberían restringirse al rol Administrador.

---

## 9. Criterios de aceptación

### Alta y edición de producto

- [ ] Dado que el usuario abre el modal de creación, cuando completa SKU, Nombre, Categoría, Precio de Costo y Margen de Ganancia y guarda, entonces el sistema crea el producto, cierra el modal y el producto aparece en la tabla.
- [ ] Dado que el usuario ingresa un SKU ya existente en el modal de creación, cuando intenta guardar, entonces el sistema muestra el error "El SKU ingresado ya existe" y no crea el producto.
- [ ] Dado que el usuario modifica el Precio de Costo en el modal, cuando el campo pierde el foco o el valor cambia, entonces el sistema recalcula y actualiza el Precio de Venta manteniendo el mismo Margen de Ganancia.
- [ ] Dado que el usuario modifica el Margen de Ganancia, cuando el campo cambia, entonces el sistema recalcula y muestra el nuevo Precio de Venta.
- [ ] Dado que el usuario modifica el Precio de Venta directamente, cuando el campo cambia, entonces el sistema recalcula y actualiza el Margen de Ganancia usando el Precio de Costo actual.
- [ ] Dado que el Precio de Costo es 0, cuando el usuario modifica el Precio de Venta, entonces el campo Margen de Ganancia permanece en 0 y no se intenta la división por cero.
- [ ] Dado que el usuario intenta guardar un producto sin Nombre, cuando presiona "Guardar", entonces el sistema muestra el error "El nombre es requerido" y no persiste.
- [ ] Dado que el usuario intenta guardar con un precio negativo, cuando presiona "Guardar", entonces el sistema muestra el error "Los precios no pueden ser negativos" y no persiste.

### Indicadores de stock

- [ ] Dado un producto con StockActual = 0, cuando se muestra en la tabla, entonces el badge dice "Sin stock" en color rojo.
- [ ] Dado un producto con StockActual ≤ StockMínimo y StockActual > 0, cuando se muestra en la tabla, entonces el badge dice "Stock bajo" en color amarillo.
- [ ] Dado un producto con StockActual > StockMínimo, cuando se muestra en la tabla, entonces el badge dice "Saludable" en color verde.

### Ajuste de stock rápido

- [ ] Dado que un producto tiene StockActual = 5, cuando el usuario presiona `+`, entonces StockActual pasa a 6 y el badge se actualiza si cambia de rango.
- [ ] Dado que un producto tiene StockActual = 1, cuando el usuario presiona `−`, entonces StockActual pasa a 0 y el badge cambia a "Sin stock" rojo.
- [ ] Dado que un producto tiene StockActual = 0, cuando el usuario intenta presionar `−`, entonces el botón está deshabilitado y el stock no cambia.

### Búsqueda y filtros

- [ ] Dado que el usuario escribe en el buscador, cuando el texto cambia, entonces la tabla muestra solo los productos cuyo Nombre, SKU o Categoría contengan el texto ingresado (sin distinguir mayúsculas).
- [ ] Dado que el usuario selecciona una categoría en el filtro, cuando el filtro cambia, entonces la tabla muestra solo productos de esa categoría.
- [ ] Dado que el usuario combina buscador y filtro de categoría, cuando ambos están activos, entonces la tabla aplica ambos filtros simultáneamente.

### Ajuste masivo de precios

- [ ] Dado que el usuario selecciona 3 productos y escribe 10 en el campo de porcentaje, cuando observa el modal, entonces la columna "Precio resultante" muestra el precio actual × 1.10 para cada producto, en tiempo real.
- [ ] Dado que el usuario confirma el ajuste, cuando la operación termina, entonces los PrecioVenta de los productos seleccionados se actualizaron, el MargenGanancia fue recalculado, y existen registros en HistorialPrecio para cada producto modificado con FechaCambio, PrecioAnterior, PrecioNuevo y VariacionPorcentual correctos.
- [ ] Dado que el usuario no seleccionó ningún producto, cuando intenta confirmar el ajuste, entonces el sistema muestra el aviso "Seleccionás al menos un producto para aplicar el ajuste" y no modifica nada.
- [ ] Dado que la reducción llevaría el precio de un producto a 0 o negativo, cuando se confirma el ajuste, entonces ese producto es excluido del ajuste y se muestra una advertencia indicando cuáles productos fueron excluidos.

### Eliminación múltiple

- [ ] Dado que el usuario selecciona 2 productos y presiona "Eliminar seleccionados", cuando confirma en el modal, entonces los 2 productos quedan con IsDeleted = true y dejan de aparecer en la tabla.
- [ ] Dado que el usuario cierra el modal de confirmación de eliminación con "Cancelar", cuando vuelve a la tabla, entonces los productos siguen activos y visibles.
- [ ] Dado que no hay productos seleccionados, cuando el usuario observa la barra de acciones, entonces el botón "Eliminar seleccionados" está deshabilitado.

### Importación Excel

- [ ] Dado un archivo .xlsx válido con SKUs nuevos, cuando el usuario confirma la importación eligiendo una categoría, entonces los productos nuevos son creados con StockActual = 5 y la categoría seleccionada, y el informe muestra la cantidad correcta de creados.
- [ ] Dado un archivo .xlsx con un SKU que ya existe, cuando se importa, entonces el producto existente actualiza Nombre y PrecioVenta, el MargenGanancia se recalcula con el PrecioCosto previo, y el informe lo cuenta como actualizado.
- [ ] Dado un archivo .xlsx con una fila donde el Precio no es un número válido, cuando se importa, entonces esa fila aparece en el informe como error con el número de fila y el motivo "Precio inválido", y las demás filas se procesan normalmente.
- [ ] Dado un archivo .xlsx con una fila donde el SKU está vacío, cuando se importa, entonces esa fila aparece en el informe como error con motivo "SKU vacío".
- [ ] Dado que el usuario selecciona un archivo que no es .xlsx, cuando intenta importar, entonces el sistema muestra un error "El archivo debe ser de formato .xlsx" y no procesa nada.
- [ ] Dado un archivo .xlsx que no contiene las columnas A, B y C con datos reconocibles, cuando se importa, entonces el sistema muestra un error de formato y cancela la importación.

### Escaneo de código de barras

- [ ] Dado que el usuario escanea un código de barras de un producto existente mientras está en la pantalla de Productos, cuando el lector envía el código, entonces el modal de edición de ese producto se abre automáticamente.
- [ ] Dado que el usuario escanea un código de barras que no corresponde a ningún producto, cuando el lector envía el código, entonces el sistema muestra el aviso "No se encontró ningún producto con ese código de barras."

---

## 10. Casos borde y manejo de errores

- **SKU duplicado al crear:** si se intenta guardar un producto con un SKU que ya existe (incluso si el producto original tiene `IsDeleted = true`), el sistema rechaza la operación con el mensaje "El SKU ingresado ya existe en el sistema."
- **Precio de costo = 0 (margen indefinido):** el campo `MargenGanancia` se muestra en 0 y se deshabilita el recálculo automático hacia ese campo. El usuario puede editar `PrecioVenta` libremente. Al importar un producto con `PrecioCosto = 0` y actualizarlo, el margen queda en 0 sin calcular.
- **Stock negativo tras ajuste manual:** el botón `−` se deshabilita cuando `StockActual = 0`, impidiendo que el stock baje de cero.
- **Ajuste masivo con 0 productos seleccionados:** se bloquea la confirmación con aviso al usuario. No se ejecuta ninguna operación en base de datos.
- **Ajuste masivo que llevaría un precio a 0 o negativo:** los productos afectados se excluyen del ajuste y se listan en una advertencia antes o después de confirmar.
- **Excel con columnas faltantes o formato incorrecto:** si el archivo no es `.xlsx` o no tiene datos en las columnas A/B/C, la importación se cancela inmediatamente con mensaje descriptivo antes de procesar ninguna fila.
- **Excel con filas parcialmente inválidas:** cada fila se valida de forma independiente. Las filas inválidas se registran en el informe de errores con número de fila y motivo; las válidas se procesan.
- **Excel con SKU eliminado (IsDeleted = true):** si el SKU del archivo corresponde a un producto con soft delete, se actualiza el producto existente (no se crea uno nuevo) y se reactiva (`IsDeleted = false`). Se registra como "actualizado" en el informe. *(Decisión técnica: permite reimportar productos eliminados por error.)*
- **Código de barras duplicado al editar:** si al guardar un producto el `CodigoBarras` ingresado ya pertenece a otro producto activo, el sistema rechaza con el mensaje "El código de barras ya está asignado a otro producto."
- **Error de base de datos durante ajuste masivo o importación:** si ocurre un error de infraestructura, se hace rollback de la transacción completa y se muestra un mensaje de error genérico al usuario, sugiriendo reintentar.
- **Cierre del modal con cambios sin guardar:** si el usuario modificó campos en el modal de creación/edición y presiona "Cancelar" o el botón de cierre, el sistema pide confirmación antes de descartar los cambios.

---

## 11. Preguntas abiertas

- **¿El soft delete de un producto con SKU previo bloquea para siempre ese SKU?** Se definió que sí (§5, Restricciones de integridad), pero podría revisarse si el negocio necesita reutilizar SKUs de productos discontinuados.
- **¿Se necesita auditoría del ajuste de stock manual (+/−)?** En esta especificación no se registra historial para ajustes manuales de stock, solo para cambios de precio. Si en el futuro se requiere trazabilidad de movimientos de stock, habría que agregar una tabla `HistorialStock`.
- **¿El informe de importación Excel se persiste o solo se muestra en pantalla?** Por ahora se asume que se muestra en pantalla y no se almacena. Si se requiere revisarlo después, habría que agregar persistencia.
- **¿Los productos con IsDeleted = true deben poder verse o recuperarse desde alguna pantalla?** No se define en esta spec una pantalla de "papelera" o recuperación. Si se necesita, sería una funcionalidad adicional.
- **¿La unidad de medida es un campo libre o un listado predefinido?** Se asumió campo libre (string) con default `"u."`. Si se necesita control sobre los valores posibles, habría que implementar una tabla de unidades de medida.
