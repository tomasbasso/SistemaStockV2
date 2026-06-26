# Spec: Configuración y Respaldos

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El dueño del negocio necesita un lugar donde configurar los datos de su empresa (nombre, dirección, teléfono) que aparecen en todos los PDFs que genera el sistema. También necesita poder configurar un sistema de copias de seguridad: elegir una carpeta destino (idealmente en Google Drive) para que el sistema guarde backups automáticos cada 24 horas. En cualquier momento puede hacer un backup manual. Y si algo salió mal, puede restaurar la base de datos desde un archivo de backup, lo que reinicia la aplicación automáticamente."

---

## 2. Objetivo

Proveer al dueño del negocio una pantalla única de configuración con dos responsabilidades: (1) editar los datos de identificación de la empresa que se imprimen en todos los documentos PDF del sistema (remitos, presupuestos, estados de cuenta), y (2) gestionar el ciclo completo de respaldo de la base de datos SQLite local: configuración de carpeta destino, backup manual inmediato, backup automático cada 24 horas al iniciar la app, backup de cierre de sesión y restauración desde archivo con reinicio de la aplicación.

---

## 3. Alcance

### Incluye

- Edición y persistencia de `ConfiguracionApp`: nombre del negocio, dirección, teléfono, símbolo monetario (Moneda).
- Edición de umbrales de análisis de rotación: `UmbralRotacionBaja`, `UmbralRotacionMedia`, `DiasAlertaSinVenta`. *(Estos campos están en el modelo pero no expuestos en la UI actual; quedan dentro del alcance del modelo de datos aunque la pantalla no los muestre por ahora.)*
- Selector de carpeta destino de respaldos mediante `FolderPicker` nativo del SO; la ruta se persiste en `Preferences`.
- **Backup histórico** (`Backup_Stock_yyyyMMdd_HHmm.db`): generado manualmente ("Respaldar Ahora") y automáticamente al inicio de la app si pasaron más de 24 horas desde el último backup histórico.
- **Backup de cierre** (`Backup_Stock_UltimoCierre.db`): generado automáticamente al cerrar la sesión vía `BackupService.ExecuteClosingBackupAsync`; nombre fijo, se sobreescribe en cada cierre.
- Retención automática de los últimos 15 backups históricos; los excedentes se eliminan por antigüedad.
- Visualización en pantalla de las fechas del último backup histórico y del último backup de cierre (hora local).
- **Restauración**: selector de archivo nativo (`.db`, `.sqlite`, `.sqlite3`), cierre de pools SQLite, GC forzado, sobreescritura del archivo activo, cierre de la app en 3 segundos con aviso visible.
- Notificaciones toast (éxito/error/advertencia) para todas las acciones.

### No incluye (fuera de alcance)

- Integración directa con la API de Google Drive; la sincronización a la nube es responsabilidad de Google Drive Desktop instalado en el sistema operativo.
- Programación de backups en horarios específicos (no hay scheduler interno; el trigger es el inicio de la app).
- Versionado ni diff entre backups.
- Restauración parcial (tablas específicas) o migración entre versiones del esquema al restaurar.
- Cifrado o compresión de los archivos de backup.
- Gestión de múltiples perfiles de empresa.
- Acceso a esta pantalla por parte de usuarios no administradores.
- Edición de `UmbralRotacionBaja`, `UmbralRotacionMedia` y `DiasAlertaSinVenta` desde la UI de Configuración (los valores se usan internamente; la pantalla no los expone).

---

## 4. Definiciones funcionales

### 4.1 Datos de la empresa

- Los campos editables son: **Nombre del Negocio** (obligatorio, máx. 150 caracteres), **Dirección Completa** (opcional, máx. 300 caracteres), **Teléfono** (opcional, máx. 50 caracteres) y **Moneda / Símbolo monetario** (opcional, máx. 10 caracteres, valor por defecto `"ARS"`).
- Al guardar, los cambios se persisten en la tabla `ConfiguracionApp` de la base de datos SQLite local mediante `DataService.SaveConfiguracionAsync`.
- El sistema carga la configuración existente al inicializar la pantalla; si no existe ningún registro, crea uno con los valores por defecto del modelo.
- Los datos de empresa impactan directamente en el encabezado y pie de todos los PDFs generados (remitos, presupuestos, estados de cuenta). Los cambios en el nombre del negocio afectan también el menú lateral; ese cambio se ve recién al reiniciar la app.
- El éxito del guardado se confirma con un mensaje inline que desaparece a los 3 segundos y con una notificación toast.

### 4.2 Carpeta de destino de respaldos

- El usuario elige la carpeta mediante el selector nativo del SO (`FolderPicker.Default.PickAsync`).
- La ruta se persiste inmediatamente en `Preferences` con clave `"Backup.TargetFolder"` y se muestra en el campo de texto de solo lectura.
- Si el usuario cancela el selector, la carpeta anterior no se modifica.
- Se recomienda en la UI que la carpeta elegida sea una carpeta sincronizada por Google Drive Desktop.
- El botón "Respaldar Ahora" permanece deshabilitado si no hay carpeta configurada o si la carpeta configurada ya no existe en el sistema de archivos.

### 4.3 Backup histórico

- **Nombre de archivo:** `Backup_Stock_yyyyMMdd_HHmm.db` (fecha/hora local del momento de generación).
- **Trigger manual:** botón "Respaldar Ahora" en la pantalla de Configuración.
- **Trigger automático:** al iniciar la app, `BackupService.CheckAndRunAutoBackupAsync` verifica si pasaron más de 24 horas desde el último backup histórico (clave `Preferences "Backup.LastRunUtc"`). Si la condición se cumple y hay carpeta configurada válida, ejecuta el backup silenciosamente.
- Tras un backup exitoso (manual o automático), se actualiza `Preferences "Backup.LastRunUtc"` con la fecha UTC actual y se refresca la fecha visible en la pantalla.
- Si el backup automático falla (carpeta no existe, error de escritura, DB no encontrada), el fallo es silencioso desde la perspectiva del usuario: no se muestra alerta, no se bloquea el inicio, pero el error queda logueado en consola. El backup manual sí muestra el error explícitamente.

### 4.4 Retención de backups históricos

- Tras cada backup histórico exitoso, el servicio lista todos los archivos que coincidan con el patrón `Backup_Stock_*.db` en la carpeta destino, ordenados por fecha de creación descendente.
- Si la cantidad supera 15, se eliminan los excedentes comenzando por los más antiguos.
- Si hay exactamente 15 archivos antes de crear el nuevo backup, se crea primero el nuevo (queda con 16) y luego se elimina el más antiguo (vuelve a 15). El resultado siempre es exactamente 15 archivos históricos.
- Los errores durante la limpieza de archivos viejos son ignorados silenciosamente; no revierten ni invalidan el backup recién creado.

### 4.5 Backup de cierre

- **Nombre de archivo:** `Backup_Stock_UltimoCierre.db` (nombre fijo; se sobreescribe en cada cierre).
- **Trigger:** cierre de sesión del usuario; invocado desde `BackupService.ExecuteClosingBackupAsync`.
- Requiere que haya una carpeta de destino configurada y existente; si no la hay, el backup de cierre se omite silenciosamente.
- Tras el backup exitoso, se actualiza `Preferences "Backup.LastCloseUtc"` con la fecha UTC actual.
- La pantalla muestra la fecha del último backup de cierre de forma independiente al backup histórico.

### 4.6 Acción "Respaldar Ahora"

- Disponible solo si hay carpeta configurada y existente (`canBackup = true`).
- Durante la ejecución, el botón muestra spinner "Generando respaldo..." y tanto el botón de backup como el de restauración quedan deshabilitados para evitar ejecuciones concurrentes.
- Si el resultado es exitoso: se muestra alerta inline verde con el mensaje de confirmación y se actualiza la fecha visible del último backup histórico.
- Si el resultado es fallido: se muestra alerta inline roja con el mensaje de error.

### 4.7 Acción "Restaurar backup"

- El usuario selecciona un archivo mediante el selector nativo del SO; se aceptan extensiones `.db`, `.sqlite`, `.sqlite3`.
- Antes de que el usuario seleccione el archivo, la UI no muestra ningún modal de confirmación adicional. El aviso de consecuencias irreversibles está expresado en el diseño de la sección y en el color de advertencia del botón.
- Si el usuario cancela el selector de archivo, la operación termina sin cambios.
- Si el archivo seleccionado tiene una extensión no válida, se devuelve error y se muestra alerta roja.
- Proceso exitoso:
  1. `SqliteConnection.ClearAllPools()` cierra todos los pools de conexión SQLite.
  2. `GC.Collect()` + `GC.WaitForPendingFinalizers()` libera handles de archivo pendientes.
  3. Se sobreescribe el archivo de base de datos activo (`stock.db`) con el archivo seleccionado.
  4. Se muestra alerta verde con el aviso de que la app se cerrará en 3 segundos.
  5. Tras 3 segundos, `Application.Current?.Quit()` cierra la aplicación.
  6. El usuario debe reabrir la app manualmente para cargar los datos restaurados.
- Proceso fallido: se muestra alerta roja con el mensaje de error; la app no se cierra.

---

## 5. Datos y modelo

### Entidad `ConfiguracionApp` (tabla SQLite, 1 registro singleton)

| Campo | Tipo | Restricciones | Valor por defecto |
|---|---|---|---|
| `Id` | `Guid` | PK | `Guid.NewGuid()` |
| `NombreNegocio` | `string` | Obligatorio, máx. 150 chars | `"Mi Negocio"` |
| `Moneda` | `string` | Opcional, máx. 10 chars | `"ARS"` |
| `DireccionNegocio` | `string` | Opcional, máx. 300 chars | `""` |
| `Telefono` | `string` | Opcional, máx. 50 chars | `""` |
| `UmbralRotacionBaja` | `decimal` | No nulo | `1.0` |
| `UmbralRotacionMedia` | `decimal` | No nulo | `4.0` |
| `DiasAlertaSinVenta` | `int` | No nulo | `90` |

### Claves de `Preferences` (almacenamiento local del SO / MAUI)

| Clave | Tipo | Descripción |
|---|---|---|
| `Backup.TargetFolder` | `string` | Ruta absoluta de la carpeta destino de respaldos |
| `Backup.LastRunUtc` | `string` (ISO 8601) | Fecha UTC del último backup histórico exitoso |
| `Backup.LastCloseUtc` | `string` (ISO 8601) | Fecha UTC del último backup de cierre exitoso |

### Archivo de base de datos activo

- Ruta: `FileSystem.AppDataDirectory + "/stock.db"`
- Solo se sobreescribe durante la operación de Restaurar.

---

## 6. UX / Interfaz

### Pantalla `Configuracion.razor` (`/configuracion`)

La pantalla se divide en dos tarjetas (`premium-card`) verticales:

**Tarjeta 1 — Datos de la empresa**
- Grilla 2 columnas en desktop / 1 columna en mobile.
- Campos: Nombre del Negocio, Moneda (Símbolo), Teléfono, Dirección Completa (ancho completo).
- Botón primario "Guardar Cambios" alineado a la derecha; muestra spinner "Guardando..." durante el guardado.
- Mensaje de éxito inline con ícono y texto verde; se oculta automáticamente a los 3 segundos.
- Mensajes de validación por campo (texto rojo) provistos por `DataAnnotationsValidator`.

**Tarjeta 2 — Respaldo de Datos**
- Texto descriptivo de la funcionalidad.
- Campo de texto de solo lectura con la ruta de la carpeta seleccionada (o "Sin carpeta seleccionada") + botón "Seleccionar Carpeta".
- Nota tipográfica menor recomendando usar Google Drive Desktop.
- Línea de estado: "Último respaldo histórico: [fecha]" con ícono reloj.
- Línea de estado: "Último backup de cierre: [fecha]" con ícono calendario.
- Dos botones en fila: "Respaldar ahora" (color primario) y "Restaurar backup" (color advertencia). Ambos muestran spinner y texto alternativo durante la ejecución. Ambos se deshabilitan cuando hay una operación en curso.
- Zona de alerta inline (éxito en verde, error en rojo) debajo de los botones; solo visible cuando hay un mensaje activo.

**Estados de la pantalla**

| Estado | Descripción |
|---|---|
| Carga inicial | Skeleton animado (`animate-pulse`) mientras se obtiene `ConfiguracionApp` de la base de datos |
| Sin carpeta | `selectedFolderDisplay = "Sin carpeta seleccionada"`, botón "Respaldar Ahora" deshabilitado |
| Backup en curso | `isBackingUp = true`, spinner en botón de backup, ambos botones deshabilitados |
| Restaurando | `isRestoring = true`, spinner en botón de restaurar, ambos botones deshabilitados |
| Error de acción | Alerta roja inline con mensaje de error |
| Éxito de acción | Alerta verde inline con mensaje de confirmación |

---

## 7. Definiciones técnicas

- **Framework:** .NET 8 MAUI Blazor Hybrid (aplicación de escritorio Windows, con estructura preparada para Android).
- **Componente principal:** `Configuracion.razor` ubicado en `SistemaDeStockV3/Components/Pages/`.
- **Servicio de datos:** `DataService.GetConfiguracionAsync()` / `DataService.SaveConfiguracionAsync()` para CRUD del singleton `ConfiguracionApp` en SQLite.
- **Servicio de respaldos:** `BackupService` (`SistemaDeStockV3/Services/BackupService.cs`) como servicio inyectado; métodos principales:
  - `ExecuteBackupToFolderAsync(string targetFolder, bool isAutomatic)` — backup histórico.
  - `ExecuteClosingBackupAsync(string targetFolder)` — backup de cierre.
  - `CheckAndRunAutoBackupAsync()` — backup automático al inicio (verifica el umbral de 24 horas).
  - `RestoreBackupAsync()` — restauración con cierre de conexiones y reinicio.
  - `GetConfiguredFolder()`, `GetLastBackupUtc()`, `GetLastClosingBackupUtc()` — lectura de estado desde `Preferences`.
- **Selector de carpeta:** `FolderPicker.Default.PickAsync` (CommunityToolkit.Maui.Storage), ejecutado en el hilo principal (`MainThread.InvokeOnMainThreadAsync`).
- **Selector de archivo:** `FilePicker.Default.PickAsync` (Microsoft.Maui.Storage), ejecutado en el hilo principal.
- **Persistencia de preferencias:** `Microsoft.Maui.Storage.Preferences` (clave-valor del SO).
- **Cierre de conexiones SQLite:** `Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()` + `GC.Collect()` / `GC.WaitForPendingFinalizers()`.
- **Cierre de la aplicación:** `Application.Current?.Quit()` tras un `Task.Delay(3000)`.
- **Patrón de resultado:** `Result<string>` (tipo propio del proyecto) con propiedades `Success`, `Message`.
- **Copia de archivo:** `FileStream` con `FileShare.ReadWrite` en la lectura para no bloquear la base activa; `FileShare.None` en la escritura del destino.
- **Retención:** ordenamiento por `FileInfo.CreationTimeUtc` descendente; se saltan los primeros 15 y se eliminan los restantes.
- **Logging:** errores de backup automático y limpieza de retención se escriben en `Console` (sin interrumpir al usuario).

---

## 8. Seguridad y permisos

- La pantalla de configuración es accesible solo para el rol **Administrador / Dueño del negocio**. No hay acceso para operadores o usuarios de solo lectura.
- No se implementa control de acceso adicional dentro del componente (la restricción se aplica en el enrutamiento/menú del sistema).
- La restauración es una operación destructiva e irreversible; se mitiga con la advertencia visual en el botón (color de aviso) y el texto descriptivo de la sección. No hay doble confirmación modal por decisión de diseño.
- Los archivos de backup no están cifrados ni protegidos con contraseña. La seguridad de los archivos depende de la carpeta destino elegida y de los permisos del sistema de archivos del SO.
- No se transmite ningún dato a servidores externos; la sincronización a la nube es delegada completamente a herramientas del SO (Google Drive Desktop).

---

## 9. Criterios de aceptación

### CA-01 — Carga inicial de configuración
- [ ] Dado que la app inicia y existe un registro `ConfiguracionApp`, cuando el usuario navega a `/configuracion`, entonces los campos NombreNegocio, Dirección, Teléfono y Moneda se muestran precargados con los valores almacenados.
- [ ] Dado que no existe ningún registro `ConfiguracionApp`, cuando el usuario navega a `/configuracion`, entonces los campos se muestran con los valores por defecto (`"Mi Negocio"`, `"ARS"`, campos opcionales vacíos).
- [ ] Dado que la carga está en progreso, entonces se muestra el skeleton de carga animado en lugar del formulario.

### CA-02 — Guardar datos de empresa
- [ ] Dado que el campo NombreNegocio está vacío, cuando el usuario intenta guardar, entonces se muestra el mensaje de validación "El nombre del negocio es obligatorio." y el guardado no se ejecuta.
- [ ] Dado que todos los campos son válidos, cuando el usuario hace clic en "Guardar Cambios", entonces el botón muestra spinner "Guardando...", los datos se persisten en SQLite, aparece la notificación toast de éxito y el mensaje inline verde desaparece a los 3 segundos.
- [ ] Dado que ocurre un error en el guardado, entonces se muestra notificación toast de error con el mensaje de la excepción.

### CA-03 — Selección de carpeta de respaldo
- [ ] Dado que el usuario hace clic en "Seleccionar Carpeta", entonces se abre el selector nativo del SO.
- [ ] Dado que el usuario selecciona una carpeta válida, entonces la ruta se muestra en el campo de texto y se persiste en `Preferences "Backup.TargetFolder"`.
- [ ] Dado que el usuario cancela el selector, entonces la carpeta previamente configurada no cambia.
- [ ] Dado que no hay carpeta configurada, entonces el botón "Respaldar Ahora" está deshabilitado.

### CA-04 — Backup manual ("Respaldar Ahora")
- [ ] Dado que hay carpeta configurada y existente, cuando el usuario hace clic en "Respaldar Ahora", entonces se genera el archivo `Backup_Stock_yyyyMMdd_HHmm.db` en esa carpeta, se actualiza `Preferences "Backup.LastRunUtc"`, se refresca la fecha visible en pantalla y aparece alerta verde de éxito.
- [ ] Dado que el backup manual es exitoso y hay más de 15 archivos `Backup_Stock_*.db` en la carpeta, entonces los archivos más antiguos se eliminan hasta dejar exactamente 15.
- [ ] Dado que hay exactamente 15 archivos antes del backup, cuando se genera uno nuevo, entonces primero se crea (quedando 16) y luego se elimina el más antiguo (quedando 15).
- [ ] Dado que ocurre un error al escribir el archivo (permisos, disco lleno), entonces se muestra alerta roja con el mensaje de error; la fecha visible no se actualiza.
- [ ] Durante el backup, ambos botones (backup y restaurar) están deshabilitados y el botón de backup muestra el spinner.

### CA-05 — Backup automático al inicio
- [ ] Dado que han pasado más de 24 horas desde `Preferences "Backup.LastRunUtc"` y hay carpeta configurada y existente, cuando la app inicia, entonces `CheckAndRunAutoBackupAsync` genera el backup histórico automáticamente sin interacción del usuario.
- [ ] Dado que no han pasado 24 horas, cuando la app inicia, entonces no se genera ningún backup automático.
- [ ] Dado que la carpeta configurada no existe al momento del backup automático, entonces el backup falla silenciosamente (sin alerta al usuario) y se loguea el error en consola.
- [ ] Dado que el backup automático falla por cualquier razón, entonces la app continúa iniciando normalmente sin bloqueos ni alertas visibles.

### CA-06 — Backup de cierre
- [ ] Dado que el usuario cierra la sesión y hay carpeta configurada y existente, entonces se genera o sobreescribe `Backup_Stock_UltimoCierre.db` en la carpeta destino y se actualiza `Preferences "Backup.LastCloseUtc"`.
- [ ] Dado que no hay carpeta configurada, entonces el backup de cierre se omite silenciosamente.
- [ ] La fecha del último backup de cierre se muestra de forma independiente a la del backup histórico en la pantalla de configuración.

### CA-07 — Restaurar backup
- [ ] Dado que el usuario hace clic en "Restaurar backup", entonces se abre el selector de archivo nativo aceptando `.db`, `.sqlite`, `.sqlite3`.
- [ ] Dado que el usuario cancela el selector, entonces no ocurre ningún cambio en la base de datos ni en el estado de la app.
- [ ] Dado que el usuario selecciona un archivo con extensión inválida, entonces se muestra alerta roja con mensaje de error y la app no se cierra.
- [ ] Dado que el usuario selecciona un archivo `.db` válido, cuando la restauración es exitosa, entonces: se muestran los pasos de limpieza de conexiones, el archivo `stock.db` se sobreescribe, aparece alerta verde indicando que la app se cerrará en 3 segundos, y tras 3 segundos la app se cierra.
- [ ] El usuario debe reabrir la app manualmente para cargar los datos restaurados.
- [ ] Dado que ocurre un error durante la sobreescritura (lock de archivo, archivo corrupto ilegible), entonces se muestra alerta roja con el mensaje de error y la app no se cierra.
- [ ] Durante la restauración, el botón de restaurar muestra spinner y ambos botones quedan deshabilitados.

### CA-08 — Visualización de fechas
- [ ] La fecha del último backup histórico se muestra en hora local con formato `dd/MM/yyyy HH:mm`.
- [ ] La fecha del último backup de cierre se muestra en hora local con formato `dd/MM/yyyy HH:mm`.
- [ ] Si nunca se realizó un backup histórico, se muestra "Nunca".
- [ ] Si nunca se realizó un backup de cierre, se muestra "Nunca".

---

## 10. Casos borde y manejo de errores

| Caso | Comportamiento definido |
|---|---|
| La carpeta destino configurada deja de existir (fue eliminada o desconectada) al momento de un backup manual | `canBackup` evalúa `Directory.Exists(selectedFolder)`; el botón queda deshabilitado. Si el path está en `Preferences` pero la carpeta no existe, el botón está deshabilitado sin alerta proactiva. |
| La carpeta destino no existe al inicio del backup automático | El backup falla silenciosamente; no se muestra alerta al usuario; se loguea en consola. |
| El archivo de backup a restaurar está corrupto o es ilegible | `CopyToAsync` lanza excepción; el `catch` general la captura; se muestra alerta roja con el mensaje de error; la app no se cierra; `stock.db` puede quedar parcialmente sobreescrita. **Riesgo:** si el error ocurre a mitad de la copia, `stock.db` queda en estado inválido. Mitigación recomendada para versión futura: escribir a un archivo temporal y reemplazar atómicamente. |
| Fallo en `ClearAllPools` o en `GC.Collect` antes de la restauración | Estos métodos no lanzan excepciones en condiciones normales; si la base de datos aun así está bloqueada por otro hilo, `FileStream` con `FileShare.None` lanzará `IOException`, que el `catch` captura y devuelve como error. |
| El backup automático falla silenciosamente | No hay notificación al usuario, no se bloquea el inicio, no se registra en `Preferences`. El usuario puede verificar el estado en la pantalla de Configuración si nota que la fecha de último backup no se actualizó. |
| Exactamente 15 archivos históricos antes del backup | Se crea el backup nuevo (total 16), luego se elimina el más antiguo (total 15). El archivo eliminado es el de `CreationTimeUtc` más antiguo. |
| Error al eliminar archivos viejos (permisos, archivo bloqueado) | El error se ignora silenciosamente; el backup recién creado no se revierte. Puede acumularse más de 15 archivos históricos temporalmente. |
| El usuario hace clic en "Restaurar" mientras hay un backup en curso (o viceversa) | Ambos botones se deshabilitan mientras cualquiera de las dos operaciones está activa (`isBackingUp || isRestoring`). |
| La app no puede ejecutar `Application.Current?.Quit()` | El `?.` evita NullReferenceException. Si la app no se cierra, el usuario queda en un estado inconsistente con los datos de la sesión anterior en memoria; deberá cerrar la app manualmente. |
| `ConfiguracionApp` no existe en la base de datos al cargar la pantalla | `GetConfiguracionAsync` retorna `null`; el código hace `?? new ConfiguracionApp()` con los valores por defecto. El usuario puede guardar y creará el primer registro. |

---

## 11. Preguntas abiertas

1. **Corrupción parcial durante restauración:** ¿Se acepta el riesgo actual de `stock.db` quedando en estado inválido si la copia falla a mitad de camino, o se implementa escritura en archivo temporal + reemplazo atómico en próxima versión?

2. **Exposición de umbrales de rotación en la UI:** `UmbralRotacionBaja`, `UmbralRotacionMedia` y `DiasAlertaSinVenta` están en el modelo pero no tienen campos en la pantalla actual. ¿Se agregarán a la sección de Configuración en esta iteración o se difieren?

3. **Alerta proactiva de carpeta perdida:** Si el usuario entra a la pantalla de Configuración y la carpeta configurada ya no existe, ¿se muestra una advertencia visible (por ejemplo, el campo en rojo con un texto "La carpeta ya no existe en el sistema"), o alcanza con que el botón esté deshabilitado?

4. **Backup automático en segundo plano:** El backup automático se dispara al inicio de la app. Si la app queda abierta más de 48 horas, ¿hay algún mecanismo de re-check periódico interno (timer), o el backup automático solo se ejecuta una vez por inicio?

5. **Notificación de backup automático exitoso:** Actualmente el backup automático exitoso es completamente silencioso. ¿Se desea agregar alguna notificación toast discreta o indicador de estado que informe al usuario que el backup automático se ejecutó al inicio?
