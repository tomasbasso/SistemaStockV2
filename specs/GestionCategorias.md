# Spec: Gestión de Categorías

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"Para organizar el inventario del negocio, los productos se agrupan en categorías (por ejemplo: Electrónica, Herramientas, Limpieza). El administrador necesita poder crear categorías nuevas, editarlas, eliminarlas con confirmación, y ver qué productos pertenecen a cada una. Las categorías se muestran en una grilla visual de tarjetas."

---

## 2. Objetivo

Permitir al administrador del sistema organizar los productos en categorías lógicas, manteniendo el catálogo ordenado y navegable. La funcionalidad cubre el ciclo completo de vida de una categoría: creación, edición, eliminación y consulta de productos asociados. Resuelve la necesidad de estructurar el inventario sin depender de categorías hardcodeadas, dándole al negocio autonomía total sobre su taxonomía de productos.

---

## 3. Alcance

### Incluye

- Listado de categorías en grilla responsiva de tarjetas.
- Creación de categorías nuevas mediante modal.
- Edición del nombre de una categoría existente mediante el mismo modal.
- Eliminación física de una categoría con modal de confirmación y advertencia de irreversibilidad.
- Consulta de los productos asociados a una categoría mediante modal con tabla.
- Estado vacío en el listado (sin categorías registradas).
- Estado vacío en el modal de productos (categoría sin productos).
- Estado de carga (skeleton) mientras se obtienen los datos del servidor.
- Notificaciones de éxito y error para cada operación.

### No incluye (fuera de alcance)

- Gestión jerárquica de categorías (subcategorías o categorías padre-hijo).
- Importación o exportación masiva de categorías.
- Búsqueda o filtrado dentro del listado de categorías.
- Imágenes o íconos personalizados por categoría.
- Reasignación automática de productos al eliminar una categoría.
- Paginación del listado de categorías.
- Control de acceso por roles (el sistema actualmente es monousuario administrador).

---

## 4. Definiciones funcionales

### Creación de categorías

- El campo nombre es obligatorio y tiene un límite de 100 caracteres.
- El nombre debe ser único en todo el sistema (sin distinción de mayúsculas/minúsculas a nivel base de datos, ya que el índice único de SQLite es case-insensitive por defecto para ASCII).
- Al guardar, se invoca `Data.SaveCategoriaAsync`. Si la categoría no existe en la base (Id nuevo), se crea; si ya existe, se actualiza su nombre.
- Tras un guardado exitoso, el modal se cierra y el listado se recarga.
- Si el servidor retorna una excepción (por ejemplo, violación del índice único), se muestra una notificación de error con el mensaje de la excepción.

### Edición de categorías

- Al abrir el modal en modo edición, se clona el objeto `Categoria` para no mutar el listado mientras el usuario escribe.
- El modal muestra el mismo formulario que la creación; solo cambia el título ("Editar Categoría" vs. "Nueva Categoría").
- El comportamiento de guardado y notificación es idéntico al de creación.

### Eliminación de categorías

- La eliminación es **física** (hard delete): se ejecuta `_db.Categorias.Remove(entity)` seguido de `SaveChangesAsync`.
- No existe soft delete en la entidad `Categoria`.
- El modal de confirmación muestra el nombre de la categoría y una advertencia de que la acción es irreversible.
- Si la eliminación falla (excepción en servidor), se muestra notificación de error y el modal se cierra igualmente.
- **Productos huérfanos:** al eliminar una categoría, los productos que tenían `CategoryId` apuntando a esa categoría quedan con una FK que no resuelve a ninguna categoría existente. La base de datos (SQLite sin FK enforcement activado por defecto) no lanza error. Los productos afectados siguen existiendo y son operables, pero su campo de categoría aparecerá vacío o sin resolver en las vistas que consuman el nombre de la categoría (por ejemplo, reportes de rotación). La resolución de este estado es responsabilidad del administrador (reasignar manualmente los productos a otra categoría). Este comportamiento queda documentado y fuera del alcance de la presente funcionalidad resolver automáticamente.

### Vista de productos por categoría

- Al presionar el ícono de ojo en una tarjeta, se abre el modal de productos con spinner de carga mientras se obtienen los datos.
- Los productos se filtran por `Producto.CategoryId == cat.Id` y se ordenan alfabéticamente por nombre.
- Solo se muestran productos no eliminados (`IsDeleted == false`).
- El encabezado del modal indica cuántos resultados hay ("X productos en [Nombre Categoría]"); para un solo resultado, la etiqueta usa la forma singular ("1 producto en…").
- Si el stock de un producto es menor o igual a su `StockMinimo`, el valor de stock se muestra en rojo con ícono de advertencia.
- Si el stock es mayor al mínimo, el valor se muestra en verde.
- Si la categoría no tiene productos, se muestra un estado vacío con mensaje descriptivo.

### Listado principal

- Las categorías se ordenan alfabéticamente por nombre (orden dado por `GetCategoriasAsync`, que aplica `.OrderBy(c => c.Name)`).
- Si no hay categorías registradas, se muestra un estado vacío con acceso directo para crear la primera.
- Mientras se cargan los datos, se muestran 3 tarjetas skeleton animadas.
- Las acciones de cada tarjeta (ver, editar, eliminar) son visibles únicamente al hacer hover sobre la tarjeta.

---

## 5. Datos y modelo

### Entidad principal: `Categoria`

| Campo | Tipo    | Restricciones                                        |
|-------|---------|------------------------------------------------------|
| Id    | Guid    | PK, generado automáticamente                         |
| Name  | string  | Requerido, MaxLength 100, índice único (case-insensitive en SQLite ASCII) |

### Entidad relacionada: `Producto`

| Campo      | Tipo    | Relevancia en este contexto                          |
|------------|---------|------------------------------------------------------|
| CategoryId | Guid    | FK lógica a `Categoria.Id` (sin FK constraint activa en SQLite por defecto) |
| Name       | string  | Mostrado en modal de productos                       |
| SKU        | string  | Mostrado en modal de productos; `"—"` si está vacío  |
| Stock      | int     | Mostrado con color según comparación con StockMinimo |
| StockMinimo| int     | Umbral para alerta visual de stock bajo              |
| Price      | decimal | Mostrado formateado como `$N.2f`                     |
| IsDeleted  | bool    | Solo productos con `IsDeleted == false` se muestran  |

### Operaciones de persistencia

| Método                        | Comportamiento                                                                              |
|-------------------------------|---------------------------------------------------------------------------------------------|
| `GetCategoriasAsync()`        | Retorna todas las categorías ordenadas por nombre.                                          |
| `SaveCategoriaAsync(c)`       | Upsert: si el Id no existe en la tabla, inserta; si existe, actualiza el Name.              |
| `DeleteCategoriaAsync(id)`    | Hard delete: elimina la fila de la tabla `Categorias`. No toca la tabla `Productos`.        |
| `GetProductosAsync()`         | Usado para obtener todos los productos; el filtro por categoría se aplica en memoria (LINQ).|

### Base de datos

- Motor: SQLite.
- ORM: Entity Framework Core.
- FK enforcement: desactivado por defecto en SQLite (no se lanzan errores al eliminar una categoría con productos referenciados).

---

## 6. UX / Interfaz

### Vista principal — `/categorias`

**Estructura:**
- Encabezado con título "Categorías", subtítulo "Gestiona las familias de productos" y botón "Nueva Categoría" (alineado a la derecha en desktop, apilado en móvil).
- Grilla responsiva:
  - 1 columna en móvil (`grid-cols-1`)
  - 3 columnas en tablet (`md:grid-cols-3`)
  - 4 columnas en desktop (`lg:grid-cols-4`)
  - Gap de `gap-6` entre tarjetas.

**Estados de la vista:**

| Estado       | Descripción                                                                                       |
|--------------|---------------------------------------------------------------------------------------------------|
| Cargando     | 3 tarjetas skeleton con animación `animate-pulse`. Se muestra durante `isLoading == true`.        |
| Vacío        | Ícono `bi-tags`, mensaje "No hay categorías registradas" y enlace directo para crear la primera.  |
| Con datos    | Grilla de tarjetas; cada tarjeta eleva `-translate-y-1` al hacer hover.                           |

**Tarjeta de categoría:**
- Ícono `bi-tag-fill` en un cuadrado redondeado con color primario.
- Nombre de la categoría en texto blanco, semibold.
- Tres botones de acción (visibles solo en hover, `opacity-0 → opacity-100`):
  - Ojo (`bi-eye`): abre modal de productos — hover color primario.
  - Lápiz (`bi-pencil`): abre modal de edición — hover color primario.
  - Papelera (`bi-trash`): abre modal de eliminación — hover color danger (rojo).

### Modal de alta/edición

- Título dinámico: "Nueva Categoría" o "Editar Categoría".
- Campo: "Nombre de la Categoría" (`InputText` con `autofocus`).
  - Placeholder: "Ej: Herramientas Manuales".
  - Validación en frontend con `DataAnnotationsValidator`: muestra mensaje debajo del campo si el nombre está vacío o supera 100 caracteres.
- Botones: "Cancelar" (cierra modal sin guardar) y "Guardar" (submit del formulario).

### Modal de eliminación

- Título: "Confirmar Eliminación".
- Cuerpo: mensaje mencionando el nombre de la categoría a eliminar.
- Advertencia en color danger: "Esta acción no se puede deshacer."
- Botones: "Cancelar" (cierra sin eliminar) y "Eliminar" (ejecuta la operación).

### Modal de productos

- Título: `"Productos en: [Nombre Categoría]"`.
- Contador de resultados antes de la tabla: "[X] producto(s) en [Nombre Categoría]" — con concordancia gramatical (singular/plural).
- Tabla con columnas: Nombre, SKU, Stock (centrado), Precio (alineado a la derecha).
- Stock en rojo + ícono `bi-exclamation-triangle-fill` si `Stock <= StockMinimo`.
- Stock en verde si `Stock > StockMinimo`.
- SKU vacío o en blanco se muestra como "—".
- Precio formateado como `$X.XX`.
- Estado vacío: ícono `bi-box-seam`, mensaje "No hay productos en esta categoría."
- Botón "Cerrar" fijo al pie del modal.

### Notificaciones

- Éxito al crear: "Categoría creada."
- Éxito al editar: "Categoría actualizada."
- Éxito al eliminar: "Categoría '[Nombre]' eliminada."
- Error en cualquier operación: "Error al guardar: [mensaje]" o "Error al eliminar: [mensaje]", según el caso.

---

## 7. Definiciones técnicas

### Stack

- Framework: Blazor (MAUI Hybrid / Windows App), .NET 8.
- Componente: `Categorias.razor` en `Components/Pages/`.
- Modal reutilizable: `AppModal.razor` en `Components/Shared/`.
- Notificaciones: `NotificationService` inyectado.
- Persistencia: `DataService` inyectado, usa EF Core sobre SQLite.

### Flujo de carga de datos

1. `OnInitializedAsync` invoca `LoadData()`.
2. `LoadData()` activa `isLoading = true`, llama `Data.GetCategoriasAsync()` y asigna el resultado a `items`, luego `isLoading = false`.
3. Blazor re-renderiza el componente con los datos.

### Flujo de creación/edición

1. Usuario abre modal → se instancia un nuevo `Categoria` (alta) o se clona el existente (edición).
2. El `EditForm` con `DataAnnotationsValidator` valida el campo en tiempo real.
3. `OnValidSubmit` invoca `Save()` → `Data.SaveCategoriaAsync(editingItem)`.
4. En éxito: notificación toast de éxito, modal cerrado, `LoadData()` rellamado.
5. En error: notificación toast de error, modal permanece cerrado (comportamiento actual del catch).

### Flujo de eliminación

1. Usuario abre modal de confirmación.
2. Al confirmar, se invoca `Data.DeleteCategoriaAsync(deletingItem.Id)`.
3. En éxito: notificación de éxito, modal cerrado, `LoadData()` rellamado.
4. En error: notificación de error, modal cerrado.

### Flujo de vista de productos

1. Usuario hace click en ícono ojo → `viewingProducts = null` (dispara spinner), modal abierto.
2. Se llama `Data.GetProductosAsync()` y se filtra en memoria por `CategoryId`.
3. El resultado se asigna a `viewingProducts`, Blazor re-renderiza con la tabla o el estado vacío.

### Unicidad del nombre

- La unicidad se garantiza a nivel de base de datos mediante un índice único sobre `Categoria.Name` (`HasIndex(e => e.Name).IsUnique()`).
- No hay validación de unicidad en frontend ni en la capa de servicio antes de persistir.
- Si hay duplicado, `SaveChangesAsync` lanza una `DbUpdateException` que es capturada en el catch de `Save()` y notificada al usuario.

---

## 8. Seguridad y permisos

- El sistema es monousuario (administrador único del negocio). No existe autenticación por roles implementada.
- Todas las operaciones de la pantalla de categorías están disponibles para cualquier usuario con acceso a la aplicación.
- La aplicación es de escritorio (MAUI WinUI), por lo que el acceso físico al equipo es el único control de acceso existente.
- No se aplica ninguna restricción adicional de autorización sobre esta funcionalidad.

---

## 9. Criterios de aceptación

### Listado

- [ ] Dado que no hay categorías registradas, cuando el usuario navega a `/categorias`, entonces se muestra el estado vacío con ícono, mensaje descriptivo y enlace para crear la primera categoría.
- [ ] Dado que hay categorías registradas, cuando la página termina de cargar, entonces las categorías se muestran como tarjetas en una grilla (1 col en móvil, 3 en tablet, 4 en desktop) ordenadas alfabéticamente.
- [ ] Dado que la página está cargando datos, cuando `isLoading` es `true`, entonces se muestran exactamente 3 tarjetas skeleton animadas en lugar de las tarjetas reales.
- [ ] Dado que una tarjeta está en reposo, cuando el usuario no hace hover, entonces los botones de acción no son visibles (opacidad 0).
- [ ] Dado que el usuario hace hover sobre una tarjeta, cuando el puntero entra en la tarjeta, entonces los botones de acción (ojo, lápiz, papelera) se vuelven visibles.

### Creación

- [ ] Dado que el modal de alta está abierto, cuando el usuario deja el campo nombre vacío y presiona Guardar, entonces el formulario no se envía y se muestra el mensaje "El nombre de la categoría es obligatorio."
- [ ] Dado que el modal de alta está abierto, cuando el usuario ingresa un nombre con más de 100 caracteres y presiona Guardar, entonces el formulario no se envía y se muestra el mensaje "El nombre no puede superar 100 caracteres."
- [ ] Dado que el modal de alta está abierto, cuando el usuario ingresa un nombre válido y presiona Guardar, entonces se invoca `SaveCategoriaAsync`, el modal se cierra, se muestra el toast "Categoría creada." y la nueva categoría aparece en el listado.
- [ ] Dado que ya existe una categoría con el nombre "Electrónica", cuando el usuario intenta crear otra categoría con el mismo nombre (o con variante de mayúsculas como "electrónica"), entonces `SaveChangesAsync` lanza una excepción y se muestra el toast de error con el mensaje de la excepción.

### Edición

- [ ] Dado que el usuario presiona el ícono lápiz sobre una tarjeta, cuando el modal se abre, entonces el campo nombre ya contiene el nombre actual de la categoría y el título del modal dice "Editar Categoría".
- [ ] Dado que el modal de edición está abierto, cuando el usuario modifica el nombre y presiona Guardar, entonces se invoca `SaveCategoriaAsync` con el Id existente, el modal se cierra y se muestra el toast "Categoría actualizada."
- [ ] Dado que el usuario cancela la edición sin guardar, cuando presiona "Cancelar", entonces el modal se cierra y el nombre de la categoría en el listado no cambia.

### Eliminación

- [ ] Dado que el usuario presiona el ícono papelera sobre una tarjeta, cuando el modal de confirmación se abre, entonces se muestra el nombre de la categoría y la advertencia de que la acción es irreversible.
- [ ] Dado que el modal de confirmación está abierto, cuando el usuario presiona "Eliminar", entonces se invoca `DeleteCategoriaAsync`, el modal se cierra, se muestra el toast de éxito con el nombre de la categoría y la categoría desaparece del listado.
- [ ] Dado que el usuario presiona "Cancelar" en el modal de confirmación, cuando el modal se cierra, entonces la categoría sigue existiendo en el listado.

### Vista de productos

- [ ] Dado que el usuario presiona el ícono ojo sobre una tarjeta, cuando el modal de productos se abre, entonces se muestra un spinner mientras se cargan los datos.
- [ ] Dado que la categoría tiene productos asociados, cuando los datos terminan de cargar, entonces la tabla muestra las columnas Nombre, SKU, Stock y Precio de todos los productos no eliminados de esa categoría, ordenados alfabéticamente.
- [ ] Dado que un producto tiene `Stock <= StockMinimo`, cuando se muestra en la tabla, entonces el valor de stock aparece en rojo con un ícono de advertencia.
- [ ] Dado que un producto tiene `Stock > StockMinimo`, cuando se muestra en la tabla, entonces el valor de stock aparece en verde.
- [ ] Dado que un producto tiene el campo SKU vacío o en blanco, cuando se muestra en la tabla, entonces la celda de SKU muestra "—".
- [ ] Dado que la categoría no tiene productos asociados, cuando los datos terminan de cargar, entonces se muestra el estado vacío con el mensaje "No hay productos en esta categoría."
- [ ] Dado que la categoría tiene exactamente 1 producto, cuando el contador se renderiza, entonces muestra "1 producto" (singular, sin "s").
- [ ] Dado que la categoría tiene N productos (N > 1), cuando el contador se renderiza, entonces muestra "N productos" (plural).

---

## 10. Casos borde y manejo de errores

### Nombre duplicado

- **Situación:** el usuario intenta crear o editar una categoría con un nombre que ya existe en la tabla.
- **Comportamiento:** EF Core lanza `DbUpdateException` al violar el índice único. La excepción es capturada en `Save()` y se muestra un toast de error con el mensaje de la excepción.
- **Pendiente de mejora (no en este alcance):** mostrar un mensaje de error más amigable del tipo "Ya existe una categoría con ese nombre" en lugar del mensaje técnico de EF Core.

### Nombre con caracteres especiales o emojis

- **Situación:** el usuario ingresa nombres como "Herramientas & Accesorios", "Línea Hogar 🏠" o "Categoría №1".
- **Comportamiento esperado:** SQLite acepta Unicode en columnas de texto. La validación de frontend solo verifica longitud y obligatoriedad; no restringe caracteres especiales ni emojis. El nombre se guarda y muestra tal cual fue ingresado.
- **Consideración visual:** los emojis ocupan espacio visual variable; si el nombre con emoji supera el ancho de la tarjeta, se aplicará el truncado CSS natural del contenedor. No es necesario validar esto en frontend.
- **Unicidad con emojis:** SQLite trata los emojis como caracteres Unicode; "Hogar" y "Hogar 🏠" son valores distintos y no chocan con el índice único.

### Eliminar categoría con productos asociados

- **Situación:** el administrador elimina una categoría que tiene uno o más productos con `CategoryId` apuntando a su Id.
- **Comportamiento actual:** SQLite no tiene FK enforcement activo por defecto; la eliminación procede sin error. Los productos quedan con un `CategoryId` que no resuelve a ninguna categoría existente ("huérfanos").
- **Impacto en otras pantallas:** en reportes de rotación y en cualquier vista que resuelva el nombre de la categoría vía `categorias.TryGetValue(p.CategoryId, ...)`, el producto aparecerá con categoría vacía (`""`).
- **Recomendación futura:** antes de eliminar, verificar si existen productos asociados y advertir al usuario con el conteo, ofreciendo la opción de reasignar o confirmar igualmente. Esta lógica está fuera del alcance de este spec.

### Categoría sin productos (modal vacío)

- **Situación:** el usuario presiona el ícono ojo sobre una categoría recién creada o a la que no se le asignó ningún producto.
- **Comportamiento:** el modal muestra el spinner brevemente y luego el estado vacío con ícono `bi-box-seam` y el mensaje "No hay productos en esta categoría."

### Error de red / base de datos durante carga

- **Situación:** `GetCategoriasAsync()` o `GetProductosAsync()` falla con excepción.
- **Comportamiento actual:** no hay manejo explícito de errores en `LoadData()` ni en `OpenViewProducts()`. Una excepción no capturada puede dejar el componente en estado inconsistente.
- **Recomendación futura:** envolver las llamadas de carga en try/catch y mostrar un estado de error con opción de reintentar. Fuera del alcance de este spec.

### Modal abierto sobre datos desactualizados

- **Situación:** el usuario tiene el modal de eliminación abierto y otro proceso externo (poco probable en monousuario de escritorio) elimina la misma categoría.
- **Comportamiento:** `DeleteCategoriaAsync` llama `FindAsync` y, si no encuentra la entidad, no hace nada (`if (entity != null)` guard). El modal se cierra y el listado se recarga sin errores visibles.

---

## 11. Preguntas abiertas

1. **Mensaje de error amigable para nombre duplicado:** ¿Se desea interceptar la `DbUpdateException` para mostrar "Ya existe una categoría con ese nombre" en lugar del mensaje técnico del ORM? Requiere modificación en `Save()` o en `SaveCategoriaAsync`.

2. **Advertencia previa a eliminar categoría con productos:** ¿Debería el sistema verificar antes de mostrar el modal de eliminación si existen productos en esa categoría y, de ser así, mostrar un aviso del tipo "Esta categoría contiene X productos. Al eliminarla, esos productos quedarán sin categoría."? Requiere una query adicional al servicio.

3. **Restricción de eliminación con productos:** alternativa más estricta a la pregunta anterior: ¿Debería bloquearse la eliminación si la categoría tiene productos, obligando al usuario a reasignar primero?

4. **Activar FK enforcement en SQLite:** ¿Se desea ejecutar `PRAGMA foreign_keys = ON` en el contexto de base de datos para que SQLite lance error al intentar eliminar una categoría referenciada? Esto requeriría también definir el comportamiento de cascada (Restrict, SetNull, Cascade) en el modelo.

5. **Paginación o búsqueda en el listado:** si el negocio crece y tiene muchas categorías, ¿se necesitaría paginación o un campo de búsqueda en el listado principal?
