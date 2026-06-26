# Spec: Lector de Códigos por Cámara

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El empleado del negocio está parado frente a una estantería con su dispositivo Android. Quiere escanear un producto con la cámara para ver rápidamente su precio, stock disponible y dónde está ubicado. Si el producto ya existe en el sistema, puede directamente agregarlo al carrito del POS o ir a editar su ficha. Si el código de barras que escaneó no está registrado en el sistema, puede asignárselo a un producto existente o crear uno nuevo con ese código precargado."

---

## 2. Objetivo

Permitir que el empleado use la cámara de su dispositivo Android (o Windows con cámara disponible) para identificar productos físicos mediante el escaneo de su código de barras, accediendo al instante al precio, stock y ubicación del producto, y habilitando acciones de gestión directas (agregar al POS, editar, asignar código o crear producto nuevo) sin necesidad de buscar manualmente en el sistema.

El flujo reemplaza la búsqueda textual en contextos de trabajo presencial sobre estantería, reduciendo el tiempo de consulta y los errores de tipeo.

---

## 3. Alcance

### Incluye
- Stream de video en tiempo real usando la cámara trasera del dispositivo (Android principal, Windows con cámara como secundario)
- Detección y decodificación de códigos en simbologías EAN-13, EAN-8, Code-128, QR y otras soportadas por la librería `html5-qrcode`
- Ingreso manual de código como fallback (campo de texto + búsqueda)
- **Rama A — Código encontrado:** pantalla de ficha del producto con nombre, SKU, stock, precio de venta, precio de costo y ubicación; botones "Agregar al POS" y "Editar producto"
- **Rama B — Código no encontrado:** pantalla de opciones con "Asignar a producto existente" (buscador + vinculación) y "Crear producto nuevo" (formulario con código precargado y bloqueado)
- Solicitud y manejo del permiso de cámara en Android (permiso `CAMERA`)
- Liberación del stream de video al salir de la pantalla o navegar a otra sección
- Botón "Escanear otro" para reiniciar el flujo sin navegar fuera de la página
- Protección de unicidad de código de barras: si el código ya está asignado a otro producto, la asignación es rechazada con mensaje de error
- Indicador visual de estado de stock (sin stock / stock bajo / stock normal) en la ficha del producto escaneado

### No incluye (fuera de alcance)
- Escaneo por linterna (modo iluminación forzada)
- Generación ni impresión de etiquetas con código de barras desde esta pantalla
- Escaneo en lote (múltiples productos en un solo flujo)
- Historial de códigos escaneados en sesión
- Soporte para iOS / macOS / Tizen en esta versión
- Edición de campos del producto dentro del Scanner (solo lectura de ficha; la edición ocurre en `Productos.razor`)
- Desasignación de códigos de barras (desvincular un código de un producto)

---

## 4. Definiciones funcionales

### Flujo de escaneo activo
Al entrar a `/scanner`, el componente `BarcodeScanner.razor` inicia automáticamente el stream de video. El usuario no necesita presionar ningún botón para comenzar a escanear: la detección es continua y se dispara al primer cuadro donde se reconoce un código válido.

### Unicidad de escáner activo
Mientras el sistema está en el modo "Escaneando" y no se detectó ningún código, el stream permanece activo. Una vez detectado un código (válido o no), el stream se detiene para procesar el resultado. No se procesan múltiples lecturas simultáneas: el sistema toma el primer código decodificado exitosamente y descarta los siguientes hasta que el usuario reinicie el flujo.

### Manejo de múltiples códigos en el campo visual
Si en el encuadre de la cámara aparecen simultáneamente más de un código de barras, la librería `html5-qrcode` resuelve internamente cuál decodificar. El sistema acepta el primer código que la librería reporte y lo procesa. No hay selección manual entre múltiples códigos en el campo visual; es responsabilidad del usuario encuadrar un solo código.

### Rama A: código encontrado en la DB
La búsqueda se realiza mediante `Data.GetProductoPorCodigoBarrasAsync(codigo)`, que devuelve el primer producto cuyo campo `CodigoBarras` coincide exactamente (comparación case-sensitive, sin trim). Si se encuentra, se muestra la ficha del producto con:
- Nombre completo
- SKU
- Stock actual con indicador de color: rojo si `Stock <= 0`, amarillo si `Stock <= StockMinimo`, verde si `Stock > StockMinimo`
- Precio de venta
- Precio de costo (solo si `PrecioCosto > 0`)
- Stock mínimo
- Ubicación física (solo si el campo no está vacío)
- Nombre de la categoría

Un producto con `Stock = 0` igual aparece en la ficha (no se bloquea la visualización). El botón "Agregar al POS" permanece visible incluso con stock cero; la gestión de si se puede agregar o no al carrito queda a cargo de `PuntoDeVenta.razor`.

### Acción "Agregar al POS"
Navega a `/ventas?scan={codigo}` pasando el código escaneado como query string. `PuntoDeVenta.razor` recibe el parámetro y realiza la búsqueda exacta por `CodigoBarras` para agregar el producto al carrito. Si el POS no puede agregarlo (sin stock), muestra su propio aviso.

### Acción "Editar producto"
Navega a `/productos` sin parámetros adicionales. La selección del producto para editar queda a cargo del usuario dentro de `Productos.razor`.

### Rama B: código no encontrado en la DB
Se muestran dos opciones excluyentes. El usuario elige una:

**Opción 1 — Asignar a producto existente:**  
Se muestra un buscador que filtra por nombre o SKU (mínimo 1 carácter escrito). La lista muestra hasta 20 resultados. El usuario selecciona un producto y confirma. El sistema llama a `Data.AsignarCodigoBarrasAsync(productoId, codigoEscaneado)`. Si el código ya está asignado a otro producto distinto, el servicio lanza `InvalidOperationException` y el sistema lo muestra como error de notificación (toast). Si la asignación es exitosa, la pantalla transiciona automáticamente a la Rama A mostrando la ficha del producto recién vinculado.

**Opción 2 — Crear producto nuevo:**  
Se muestra el formulario de alta de producto con el campo `CodigoBarras` precargado con el código escaneado y en modo solo lectura (no editable por el usuario). Los campos obligatorios son: Nombre, Categoría, Precio y Unidad de medida. El campo Stock inicial comienza en 0 por defecto. Al guardar con éxito, la pantalla transiciona a la Rama A con la ficha del producto creado.

### Sobreescritura de código de barras en asignación
El sistema **no sobreescribe** silenciosamente. Si el producto seleccionado para asignar ya tiene un código de barras propio, `AsignarCodigoBarrasAsync` sobreescribe ese valor porque el control de unicidad en esa función solo verifica que el código nuevo no esté asignado a *otro* producto distinto. El empleado no recibe advertencia en pantalla sobre el código previo que se reemplaza. (Ver Sección 11 — Preguntas abiertas.)

### Fallback de ingreso manual
Disponible en cualquier momento mientras el modo sea "Escaneando". El usuario puede tipear o pegar el código en el campo de texto y presionar Enter o el botón de búsqueda. El sistema procesa ese código exactamente igual que si viniera de la cámara.

### Ciclo de vida del stream de cámara
El stream se inicia en `OnAfterRenderAsync` del componente `BarcodeScanner`. Se libera en `DisposeAsync` del componente, que se invoca cuando `BarcodeScanner` sale del DOM (navegación o reinicio que oculte el componente). El componente padre `Scanner.razor` no mantiene el stream entre modos; cuando el usuario reinicia, `BarcodeScanner` se monta de nuevo y reabre el stream.

---

## 5. Datos y modelo

### Entidades involucradas

| Entidad | Campos relevantes para este caso de uso |
|---|---|
| `Producto` | `Id` (Guid), `Name` (string, req, max 200), `SKU` (string, max 50), `CategoryId` (Guid, req), `Stock` (int), `StockMinimo` (int), `Price` (decimal, req, > 0), `PrecioCosto` (decimal), `UnidadMedida` (string, req, max 20), `Ubicacion` (string, max 100), `CodigoBarras` (string?, max 50), `IsDeleted` (bool) |
| `Categoria` | `Id` (Guid), `Name` (string) |

### Campo CodigoBarras
- Tipo: `string?` (nullable)
- Restricción de unicidad: verificada a nivel de aplicación en `AsignarCodigoBarrasAsync` y en `SaveProductoAsync` (no hay índice único en BD para esta columna según el código relevado; la unicidad es solo de aplicación).
- Longitud máxima: 50 caracteres.
- Simbologías posibles: EAN-13 (13 dígitos), EAN-8 (8 dígitos), Code-128 (alfanumérico variable), QR (alfanumérico, puede ser más largo).

### Métodos de DataService usados

| Método | Descripción |
|---|---|
| `GetProductoPorCodigoBarrasAsync(string)` | Busca el primer producto activo (no filtra `IsDeleted` explícitamente según código visto) cuyo `CodigoBarras` coincide exactamente. |
| `AsignarCodigoBarrasAsync(Guid, string)` | Verifica unicidad y actualiza `CodigoBarras` del producto. Lanza `InvalidOperationException` si el código ya pertenece a otro producto. |
| `SaveProductoAsync(Producto)` | Crea un nuevo producto. Para el flujo "Crear nuevo", el `CodigoBarras` se setea antes de llamar a este método. |
| `GetCategoriasAsync()` | Devuelve la lista de categorías para el selector del formulario de creación. |
| `GetProductosAsync()` | Carga todos los productos en memoria para el buscador de "Asignar a existente". |

---

## 6. UX / Interfaz

### Pantalla única con estados internos (modo)
La funcionalidad vive en una sola ruta `/scanner` (`Scanner.razor`). La pantalla no navega a otras rutas salvo en las acciones "Agregar al POS" y "Editar producto". Los estados (modos) se manejan con el enum interno `Modo`:

```
Modo.Escaneando → Modo.Encontrado
Modo.Escaneando → Modo.NoEncontrado → Modo.AsignarExistente
Modo.Escaneando → Modo.NoEncontrado → Modo.CrearNuevo
Modo.AsignarExistente → (éxito) → Modo.Encontrado
Modo.CrearNuevo → (éxito) → Modo.Encontrado
Modo.AsignarExistente → (cancelar) → Modo.NoEncontrado
Modo.CrearNuevo → (cancelar) → Modo.NoEncontrado
Cualquier modo != Escaneando → (botón "Escanear otro") → Modo.Escaneando
```

### Estado: Escaneando
- Header con título "Lector de Códigos" y subtítulo
- Card con el componente `BarcodeScanner` embebido (visor de cámara activo)
- Separador visual y campo de texto para ingreso manual con botón de búsqueda
- Estado inicial: cámara "iniciando..." con animación pulse; al conectarse, muestra el stream
- Estado de error de cámara: ícono de cámara apagada, mensaje de error, botón "Reintentar"

### Estado: Encontrado (Rama A)
- Badge con el código escaneado (monoespacio)
- Card del producto con: SKU, badge de stock (colores según nivel), nombre grande, categoría, precio de venta destacado, precio de costo, grid con stock mínimo y ubicación (si existe)
- Grid de dos botones: "Agregar al POS" (primario) y "Editar producto" (secundario)
- Botón "Escanear otro" en el header

### Estado: No encontrado (Rama B — selección)
- Badge con el código escaneado en color advertencia
- Card central con ícono de interrogación y texto explicativo
- Dos opciones en lista vertical tipo "card-button": "Asignar a producto existente" y "Crear producto nuevo"
- Botón "Escanear otro" en el header

### Estado: Asignar a existente
- Card con subtítulo que muestra el código a asignar (monoespacio)
- Input de búsqueda con ícono
- Lista scrolleable (max 272px) con hasta 20 resultados; cada ítem muestra nombre, SKU, stock y precio; ítem seleccionado resaltado
- Estado vacío si no se escribió nada: "Escribí para buscar un producto."
- Estado sin resultados: "No se encontraron productos."
- Footer con botones "Volver" y "Vincular código" (deshabilitado si no hay selección o mientras guarda)
- Indicador de carga (spinner) en el botón mientras se guarda

### Estado: Crear producto nuevo
- Card con subtítulo que muestra el código precargado
- Formulario con campos: Nombre (req, autofocus), SKU, Categoría (req, select), Precio (req), Stock inicial, Unidad de medida (req)
- Campo Código de barras: display read-only estilizado con badge "Pre-cargado" (no es un `<input>` editable)
- Footer con botones "Volver" y "Crear producto" (spinner mientras guarda)
- Mensajes de validación inline por campo

### Transiciones y animaciones
- Los estados distintos al "Escaneando" tienen clase `animate-fade-in`
- El botón de guardado muestra `bi-arrow-repeat animate-spin` durante la operación async

---

## 7. Definiciones técnicas

### Stack y plataforma
- Framework: .NET 8 MAUI Blazor Hybrid
- Plataforma objetivo principal: Android (también Windows con cámara)
- Routing: Blazor routing en `/scanner`
- Librería de escaneo: `html5-qrcode` (JavaScript), integrada vía `IJSRuntime`
- Bridge JS→C#: `JSInvokable` en `BarcodeScanner.razor` mediante `DotNetObjectReference`

### Componentes
- `Scanner.razor` (`/scanner`): componente de página, maneja el estado general, la lógica de negocio y las llamadas a `DataService`
- `BarcodeScanner.razor`: componente compartido reutilizable, encapsula el ciclo de vida del stream de cámara. Expone dos `EventCallback<string>`: `OnScanned` y `OnError`

### Integración con JavaScript
- `barcodeScanner.start(elementId, dotNetRef)`: inicializa el escáner en el elemento DOM con el ID dado
- `barcodeScanner.stop()`: detiene el stream y libera la cámara
- `OnScannerStarted()`: callback invocado cuando el stream está listo (activa la visibilidad del visor)
- `OnBarcodeScanned(barcode)`: callback con el código decodificado
- `OnScannerError(error)`: callback con el mensaje de error

### Permisos en Android
- Permiso requerido: `CAMERA` (declarado en `AndroidManifest.xml`)
- El permiso se solicita en runtime mediante los mecanismos de MAUI para Android
- Si el usuario deniega el permiso, el componente JS captura el error al intentar abrir el stream y lo reporta vía `OnScannerError`; Scanner.razor muestra el mensaje de error de cámara

### Integración con PuntoDeVenta
- La acción "Agregar al POS" navega a `/ventas?scan={Uri.EscapeDataString(codigo)}`
- `PuntoDeVenta.razor` debe leer ese query string al inicializarse y realizar la búsqueda exacta por `CodigoBarras`. (Nota: al momento de redactar esta spec, `PuntoDeVenta.razor` no tiene implementado el parámetro `[SupplyParameterFromQuery]` para `scan`; esto debe implementarse o verificarse.)

### Navegación y ciclo de vida
- `Scanner.razor` implementa `IAsyncDisposable`; su `DisposeAsync` retorna `ValueTask.CompletedTask` (delega la liberación de cámara a `BarcodeScanner.DisposeAsync`)
- `BarcodeScanner.razor` implementa `IAsyncDisposable`; llama a `barcodeScanner.stop()` en dispose con try/catch silencioso para no romper si el stream ya fue liberado

### Persistencia
- Base de datos: SQLite local vía Entity Framework Core (`StockDbContext`)
- Las operaciones de escritura son `AsignarCodigoBarrasAsync` y `SaveProductoAsync`; ambas llaman a `SaveChangesAsync()`

---

## 8. Seguridad y permisos

### Permiso de cámara (Android)
- El sistema operativo Android gestiona el permiso `CAMERA`
- Si el usuario niega el permiso, la cámara no inicia y se muestra el estado de error con opción de reintentar
- El usuario puede ir a Configuración del sistema para habilitarlo; el botón "Reintentar" vuelve a intentar abrir el stream

### Roles de usuario en la aplicación
La aplicación SistemaDeStockV3 no implementa un sistema de roles multiusuario en esta versión. Cualquier usuario con acceso al dispositivo puede usar el Scanner. Las restricciones son:
- La asignación de código de barras a un producto existente es irreversible desde esta pantalla (no hay desasignación)
- La creación de un producto nuevo es una operación permanente en la base de datos local

### Integridad de datos
- La unicidad del campo `CodigoBarras` está garantizada solo a nivel de aplicación (no por índice único en BD). Condiciones de carrera teóricas en uso concurrente no aplican dado que la BD es local single-user.

---

## 9. Criterios de aceptación

### Flujo de escaneo exitoso (Rama A)
- [ ] Dado que el usuario abre `/scanner`, cuando la cámara está disponible y se otorgó el permiso, entonces el stream de video se inicia automáticamente y el visor aparece sin acción del usuario
- [ ] Dado que el stream está activo, cuando la cámara detecta un código de barras válido (EAN-13, EAN-8, Code-128 o QR), entonces el sistema llama a `GetProductoPorCodigoBarrasAsync` con el código decodificado
- [ ] Dado que el código escaneado corresponde a un producto registrado en la DB, cuando se resuelve la búsqueda, entonces se muestra la ficha del producto con nombre, SKU, stock (con badge de color), precio de venta, precio de costo (si > 0) y ubicación (si no está vacía)
- [ ] Dado que la ficha está visible, cuando el usuario toca "Agregar al POS", entonces el sistema navega a `/ventas?scan={codigo}` pasando el código escaneado como query string
- [ ] Dado que la ficha está visible, cuando el usuario toca "Editar producto", entonces el sistema navega a `/productos`
- [ ] Dado que la ficha está visible (cualquier Rama), cuando el usuario toca "Escanear otro", entonces el sistema reinicia a modo Escaneando, limpia todos los estados previos y reabre el stream de cámara

### Stock en la ficha (Rama A)
- [ ] Dado un producto con `Stock = 0`, cuando se muestra la ficha, entonces el badge de stock es rojo con texto "Sin stock"
- [ ] Dado un producto con `Stock > 0` pero `Stock <= StockMinimo`, cuando se muestra la ficha, entonces el badge de stock es amarillo con el valor numérico y la unidad de medida
- [ ] Dado un producto con `Stock > StockMinimo`, cuando se muestra la ficha, entonces el badge de stock es verde con el valor numérico y la unidad de medida

### Código no encontrado (Rama B)
- [ ] Dado que el código escaneado no existe en la DB, cuando se resuelve la búsqueda, entonces se muestra la pantalla "Código no registrado" con las dos opciones de acción
- [ ] Dado que el usuario elige "Asignar a producto existente", cuando escribe al menos un carácter en el buscador, entonces aparece una lista filtrada por nombre o SKU con hasta 20 resultados
- [ ] Dado que el usuario selecciona un producto y toca "Vincular código", cuando `AsignarCodigoBarrasAsync` retorna con éxito, entonces el sistema muestra una notificación de éxito y transiciona a la ficha del producto recién vinculado (Rama A)
- [ ] Dado que el código escaneado ya está asignado a otro producto, cuando el usuario intenta vincular, entonces el sistema muestra un mensaje de error (toast) con el nombre del producto que ya tiene ese código y no realiza ningún cambio
- [ ] Dado que el usuario elige "Crear producto nuevo", cuando se muestra el formulario, entonces el campo Código de barras aparece precargado con el código escaneado y no es editable
- [ ] Dado que el usuario completa los campos obligatorios y toca "Crear producto", cuando `SaveProductoAsync` retorna con éxito, entonces el sistema muestra una notificación de éxito y transiciona a la ficha del producto creado (Rama A)

### Fallback manual
- [ ] Dado que el stream está activo, cuando el usuario escribe un código en el campo de texto y presiona Enter o el botón de búsqueda, entonces el sistema procesa ese código igual que si viniera de la cámara

### Ciclo de vida de cámara
- [ ] Dado que el usuario navega fuera de `/scanner` (a cualquier otra ruta), cuando el componente se desmonta, entonces el stream de video se libera y la cámara queda disponible para otras aplicaciones

### Permiso denegado
- [ ] Dado que el usuario deniega el permiso de cámara, cuando `BarcodeScanner` intenta iniciar el stream, entonces se muestra el estado de error de cámara con mensaje descriptivo y botón "Reintentar"
- [ ] Dado que el error de cámara está visible, cuando el usuario toca "Reintentar", entonces el sistema intenta abrir el stream nuevamente sin recargar la página

---

## 10. Casos borde y manejo de errores

### Permiso de cámara denegado
- El error es capturado por `barcodeScanner.start()` en JS y reportado vía `OnScannerError`
- Se muestra estado de error visual (ícono de cámara apagada + mensaje + botón "Reintentar")
- El fallback de ingreso manual sigue disponible si el usuario prefiere tipear el código

### Cámara no disponible en el dispositivo (hardware ausente)
- Mismo flujo que permiso denegado: el JS falla al intentar acceder a `getUserMedia`, el error se propaga como `OnScannerError`, y se muestra el estado de error
- El fallback manual permanece funcional

### Producto con stock = 0 escaneado
- La ficha se muestra normalmente con badge rojo "Sin stock"
- El botón "Agregar al POS" sigue activo; la decisión de bloquear la adición al carrito recae en `PuntoDeVenta.razor` (que actualmente muestra un toast "Sin stock: [nombre]" y no agrega el ítem)

### Múltiples códigos en el campo visual simultáneamente
- `html5-qrcode` decodifica el primero que identifica en el frame
- El sistema procesa ese código y detiene el stream; el usuario no puede forzar la elección de un código alternativo
- Recomendación de UX (no implementada en código): enfocar un solo código por vez encuadrando de cerca

### Código ya asignado a otro producto (intento de reasignación)
- `AsignarCodigoBarrasAsync` lanza `InvalidOperationException` con mensaje específico
- `Scanner.razor` captura la excepción en el bloque `catch` y llama a `Notifications.Error(ex.Message)`
- El toast muestra: "El código '{codigo}' ya está asignado a '{nombre_del_producto_existente}'"
- El sistema permanece en el modo `Modo.AsignarExistente` para que el usuario pueda intentar con otro producto

### Producto seleccionado para asignar ya tiene su propio código de barras previo
- `AsignarCodigoBarrasAsync` sobreescribe el valor anterior sin advertencia
- No hay confirmación de sobreescritura en el flujo actual; el empleado no es notificado del código previo que se perdió
- (Ver Sección 11 — Preguntas abiertas, punto 1)

### Error al crear producto nuevo (validación)
- `EditForm` con `DataAnnotationsValidator` previene el submit si campos obligatorios están vacíos o inválidos
- Mensajes de validación aparecen inline debajo de cada campo
- Si `SaveProductoAsync` lanza una excepción en runtime (ej: error de BD), el bloque `catch` llama a `Notifications.Error(ex.Message)`

### Error de red / BD durante asignación o creación
- Capturado en bloques `try/catch` en `ConfirmarAsignacion()` y `GuardarNuevoProducto()`
- Se muestra toast de error con el mensaje de la excepción
- El estado `_guardando` se resetea en el bloque `finally`, rehabilitando el botón de acción

### Reinicio del escáner durante modo Encontrado / NoEncontrado
- El botón "Escanear otro" llama a `Reiniciar()`, que limpia todo el estado y vuelve a `Modo.Escaneando`
- `BarcodeScanner` se vuelve a montar en el DOM y ejecuta `OnAfterRenderAsync` → `StartScanner()` nuevamente

---

## 11. Preguntas abiertas

1. **Sobreescritura de código previo sin advertencia:** Si el producto al que se quiere asignar el código nuevo ya tiene un `CodigoBarras` distinto, el sistema actualmente lo sobreescribe silenciosamente. ¿Se debe mostrar una confirmación del tipo "Este producto ya tiene el código [X]. ¿Querés reemplazarlo por [Y]?" antes de ejecutar la asignación?

2. **Integración query string en PuntoDeVenta:** La navegación de "Agregar al POS" pasa el código vía `?scan={codigo}`, pero `PuntoDeVenta.razor` no tiene implementado el parámetro `[SupplyParameterFromQuery]` ni la lógica de `OnParametersSetAsync` para procesarlo. ¿Esto ya está implementado y no fue visible en el relevamiento, o es una funcionalidad pendiente de desarrollar en `PuntoDeVenta`?

3. **Permiso de cámara en Android — flujo proactivo:** ¿La solicitud del permiso `CAMERA` se hace proactivamente al entrar a `/scanner` (antes de intentar abrir el stream), o reactivamente cuando `html5-qrcode` falla? La implementación actual parece ser reactiva. ¿Se prefiere el flujo proactivo con un diálogo explicativo antes de abrir la cámara?

4. **Filtrado por productos activos en buscador de asignación:** `GetProductosAsync()` actualmente no filtra `IsDeleted = false` explícitamente en la query (depende de si hay filtro global en el contexto). ¿Los productos eliminados (soft delete) deben aparecer en el buscador de "Asignar a existente"? Se asume que no, pero debe verificarse.

5. **Límite de búsqueda en asignación:** El buscador de "Asignar a existente" muestra hasta 20 resultados (`.Take(20)`). Si el negocio tiene muchos productos similares, 20 puede ser insuficiente. ¿Se acepta este límite o se prefiere paginación / mensaje indicando que hay más resultados?
