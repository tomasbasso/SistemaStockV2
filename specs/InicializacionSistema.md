# Spec: Inicialización del Sistema

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"Te voy a contar una historia. Cuando un usuario abre la aplicación SistemaDeStockV3, el sistema debe realizar en segundo plano una secuencia de pasos críticos antes de mostrar cualquier pantalla: validar y crear la base de datos SQLite si no existe, ejecutar scripts de modificación de esquema pendientes, cargar la configuración de la app, verificar si corresponde hacer un backup automático, y finalmente renderizar la pantalla principal. Todo esto debe completarse de forma robusta y en el orden correcto para garantizar la integridad de los datos desde el inicio de la sesión."

## 2. Objetivo

Garantizar que cada vez que el usuario abre SistemaDeStockV3, la base de datos SQLite esté íntegra, actualizada al esquema vigente y con la configuración inicial cargada antes de que se muestre cualquier pantalla, eliminando el riesgo de que el WebView intente acceder a datos en un estado incompleto o corrupto. Adicionalmente, el proceso verifica si corresponde ejecutar un backup automático silencioso, protegiendo los datos del negocio sin interrumpir el flujo de inicio.

## 3. Alcance

### Incluye

- Creación de la base de datos SQLite en `FileSystem.AppDataDirectory/stock.db` si no existe, usando `EnsureCreatedAsync()`.
- Ejecución de todos los scripts de migración manual de esquema (columnas y tablas), validados con `PRAGMA table_info()` para ser idempotentes.
- Seeding inicial de `ConfiguracionApp` en el primer arranque (si la tabla está vacía).
- Carga y disponibilidad de la `ConfiguracionApp` para el resto de la aplicación.
- Verificación y ejecución condicional del backup automático al inicio (`CheckAndRunAutoBackupAsync`): se ejecuta solo si hay carpeta configurada Y pasaron más de 24 horas desde el último backup automático.
- Renderizado de `MainPage` (WebView Blazor Hybrid) como pantalla inicial, navegando al Dashboard/Home.
- Manejo de errores con logging a `Debug.WriteLine` cuando falla la inicialización de la DB; la app continúa el arranque (comportamiento defensivo actual).
- Backup de cierre silencioso al cerrar la app en Windows y Android (si hay carpeta configurada).

### No incluye (fuera de alcance)

- Autenticación o login de usuarios: la app no tiene sistema de autenticación.
- Pantalla de splash o indicador de progreso de inicialización visible al usuario: el WebView no se muestra hasta que la DB está lista, pero no hay spinner/pantalla de carga explícita.
- Migraciones con `dotnet ef migrations` (EF Core migrations formales): la arquitectura MAUI multi-target no lo permite; el sistema usa migración manual con SQL raw.
- Restauración automática de backups al inicio: la restauración es una acción manual del usuario desde la UI.
- Validación de integridad estructural de la DB (PRAGMA integrity_check): fuera del alcance de esta inicialización.
- Sincronización con backend remoto o nube: la app es completamente offline.
- Múltiples instancias de la app corriendo simultáneamente.

## 4. Definiciones funcionales

### Orden estricto de inicialización

La inicialización ocurre en el constructor de `App.xaml.cs` de forma sincrónica-bloqueante para el hilo principal: se usa `Task.Run(...).GetAwaiter().GetResult()` para descargar la ejecución async al thread pool y bloquear el hilo principal hasta que termine. Esto garantiza que el WebView nunca se instancie antes de que la DB esté lista. El orden de operaciones es:

1. Creación/validación de la DB (`EnsureCreatedAsync`)
2. Ejecución de scripts de migración manual de esquema
3. Seeding inicial de datos (solo primer arranque)
4. Carga de `ConfiguracionApp` disponible en DI
5. Verificación y ejecución condicional de backup automático (`CheckAndRunAutoBackupAsync`)
6. Renderizado de `MainPage` (WebView → Dashboard/Home)

### Regla de migración de esquema (idempotencia)

Cada modificación de esquema (nueva columna o tabla) debe verificar primero si ya existe usando `PRAGMA table_info(tabla)` o `CREATE TABLE IF NOT EXISTS`, antes de ejecutar el `ALTER TABLE` o `CREATE TABLE`. Esto permite que el mismo código de inicialización sea seguro de ejecutar en cualquier versión existente de la DB sin producir errores.

### Regla de seeding inicial

El seeding solo se ejecuta si la tabla `Configuraciones` está completamente vacía. En ese caso se crea una `ConfiguracionApp` con valores por defecto: `NombreNegocio = "Comercial Kai Ken"`, `Moneda = "ARS"`. El seeding no se repite en arranques subsiguientes.

### Regla del backup automático al inicio

El `BackupService.CheckAndRunAutoBackupAsync()` evalúa dos condiciones independientes:
- Condición A: hay una carpeta de destino configurada en `Preferences` (`Backup.TargetFolder`) que existe en el sistema de archivos.
- Condición B: han transcurrido más de 24 horas desde el último backup automático exitoso (registrado en `Preferences` como `Backup.LastRunUtc`).

Si ambas condiciones se cumplen → ejecuta el backup silenciosamente (copia del archivo `stock.db` con nombre `Backup_Stock_yyyyMMdd_HHmm.db`).
Si cualquiera de las condiciones falla → no ejecuta backup y retorna un `Result` descriptivo del motivo, sin interrumpir el arranque.

El resultado del backup automático (éxito o falla) se descarta silenciosamente durante el arranque: no se muestra ningún mensaje al usuario.

### Regla de retención de backups

Al ejecutar un backup hacia una carpeta, el servicio elimina los archivos más antiguos si hay más de 15 archivos `Backup_Stock_*.db` en la carpeta destino (retención de los 15 más recientes por fecha de creación).

### Regla del backup de cierre

Al cerrar la app (evento `OnWindowClosed` en Windows, `OnStop` en Android), si hay carpeta configurada, se genera un archivo `Backup_Stock_UltimoCierre.db` (sobreescribe el anterior). Este proceso es independiente del backup automático de inicio y no actualiza `Backup.LastRunUtc`.

### Comportamiento ante error de inicialización de DB

Si `dataService.InitializeAsync()` lanza una excepción, el error se registra en `Debug.WriteLine` y la app continúa mostrando `MainPage`. La aplicación quedará en un estado degradado (las consultas a la DB fallarán en runtime). No hay pantalla de error específica para este caso en el alcance actual.

## 5. Datos y modelo

### Entidades persistidas en SQLite (`stock.db`)

| Entidad | Tabla SQLite | Soft Delete | Campos decimales como TEXT |
|---|---|---|---|
| `ConfiguracionApp` | `Configuraciones` | No | `UmbralRotacionBaja`, `UmbralRotacionMedia` |
| `Categoria` | `Categorias` | No | — |
| `Producto` | `Productos` | Sí (`IsDeleted`) | `Price`, `PrecioCosto`, `Margen` |
| `Cliente` | `Clientes` | Sí (`IsDeleted`) | — |
| `CuentaCorriente` | `CuentasCorrientes` | No | `Balance` |
| `MovimientoFinanciero` | `MovimientosFinancieros` | No | `Amount` |
| `Venta` | `Ventas` | Sí (`IsDeleted`) | `Total` |
| `VentaDetalle` | `VentaDetalles` | No | `UnitPrice` |
| `Presupuesto` | `Presupuestos` | Sí (`IsDeleted`) | `Total` |
| `PresupuestoDetalle` | `PresupuestoDetalles` | No | `UnitPrice` |
| `HistorialPrecio` | `HistorialPrecios` | No | `PrecioAnterior`, `PrecioNuevo` |

### Convención de tipos en SQLite

Los campos `decimal` en C# se mapean como `TEXT` en SQLite (`.HasColumnType("TEXT")`). Esto evita pérdida de precisión en el motor SQLite, que no tiene un tipo decimal nativo. EF Core maneja la serialización/deserialización.

Los campos `Guid` (PKs y FKs) se almacenan como `TEXT` en SQLite.

Los campos `bool` (como `IsDeleted`, `IsFiado`) se almacenan como `INTEGER` (0/1).

Los campos `enum` (como `TipoMovimiento`, `CondicionIva`) se almacenan como `TEXT` (string de la clave del enum).

### Scripts de migración manual activos (al 2026-06-17)

Los siguientes scripts se aplican idempotentemente en cada arranque, en este orden:

1. `Productos.UnidadMedida` → `TEXT NOT NULL DEFAULT 'u.'`
2. `Productos.Ubicacion` → `TEXT NOT NULL DEFAULT ''`
3. `Productos.CodigoBarras` → `TEXT NULL`
4. `CREATE TABLE IF NOT EXISTS Presupuestos` + `PresupuestoDetalles`
5. `Productos.IsDeleted` → `INTEGER NOT NULL DEFAULT 0`
6. `Clientes.IsDeleted` → `INTEGER NOT NULL DEFAULT 0`
7. `Configuraciones.UmbralRotacionBaja` → `TEXT NOT NULL DEFAULT '1.0'`
8. `Configuraciones.UmbralRotacionMedia` → `TEXT NOT NULL DEFAULT '4.0'`
9. `Configuraciones.DiasAlertaSinVenta` → `INTEGER NOT NULL DEFAULT 90`
10. `Ventas.IsDeleted` → `INTEGER NOT NULL DEFAULT 0`
11. `Presupuestos.IsDeleted` → `INTEGER NOT NULL DEFAULT 0`
12. `Productos.PrecioCosto` → `TEXT NOT NULL DEFAULT '0'`
13. `Productos.Margen` → `TEXT NOT NULL DEFAULT '0'`
14. `Clientes.CUIT` → `TEXT NOT NULL DEFAULT ''`
15. `Clientes.Email` → `TEXT NOT NULL DEFAULT ''`
16. `Clientes.CondicionIva` → `TEXT NOT NULL DEFAULT 'ConsumidorFinal'`
17. `CREATE TABLE IF NOT EXISTS HistorialPrecios`

### Filtros globales de soft delete (EF Core)

Las entidades con `IsDeleted` tienen filtros globales configurados en `OnModelCreating`:
- `Producto`: `HasQueryFilter(p => !p.IsDeleted)`
- `Cliente`: `HasQueryFilter(c => !c.IsDeleted)`
- `Venta`: `HasQueryFilter(v => !v.IsDeleted)`
- `Presupuesto`: `HasQueryFilter(p => !p.IsDeleted)`

Las consultas que necesiten acceder a registros eliminados deben usar `.IgnoreQueryFilters()`.

### Preferencias del sistema (no DB, usa MAUI Preferences)

| Clave | Tipo | Descripción |
|---|---|---|
| `Backup.TargetFolder` | `string` | Ruta de la carpeta de destino para backups automáticos |
| `Backup.LastRunUtc` | `string` (ISO 8601) | Timestamp UTC del último backup automático exitoso |
| `Backup.LastCloseUtc` | `string` (ISO 8601) | Timestamp UTC del último backup de cierre exitoso |

### Ubicación del archivo de DB

`Path.Combine(FileSystem.AppDataDirectory, "stock.db")`

En Windows: `%LOCALAPPDATA%\Packages\<AppId>\LocalState\stock.db` (o equivalente según el modo de publicación).

### Registro de servicios en DI (MauiProgram.cs)

| Servicio | Lifetime | Notas |
|---|---|---|
| `StockDbContext` | `Transient` | Una instancia por resolución |
| `DataService` | `Transient` | Recibe `StockDbContext` por DI |
| `BackupService` | `Singleton` | Sin dependencia de DB |
| `ReportService` | `Singleton` | — |
| `NotificationService` | `Singleton` | — |
| `PdfService` | `Singleton` | — |

## 6. UX / Interfaz

### Pantallas involucradas

**Durante la inicialización:** no hay pantalla visible. El sistema bloquea el hilo de UI hasta que `InitializeAsync()` complete. El usuario ve una pantalla en negro o el splash de MAUI (según la plataforma) durante este período.

**Al finalizar la inicialización:** se renderiza `MainPage.xaml`, que contiene el `BlazorWebView`. El WebView carga automáticamente el componente raíz Blazor, que navega al `Dashboard/Home`.

### Flujo de usuario

```
Usuario abre la app
  → Splash nativa de MAUI (breve, automática)
  → Pantalla negra / carga silenciosa (inicialización DB + backup)
  → Dashboard/Home visible
```

No hay feedback visual del progreso de inicialización (spinner, barra de progreso, mensajes de estado). La duración típica es imperceptible (<1 segundo en hardware moderno con DB existente) o unos pocos segundos en el primer arranque con DB nueva.

### Estados

| Estado | Qué ve el usuario |
|---|---|
| DB inexistente (primer arranque) | Splash → breve demora → Dashboard vacío (sin datos) |
| DB existente, sin cambios de esquema | Splash → Dashboard con datos |
| DB existente, esquema desactualizado | Splash → breve demora (scripts) → Dashboard con datos |
| Error de inicialización de DB | Splash → Dashboard vacío/roto (sin mensaje al usuario) |
| Backup automático ejecutado | Splash → Dashboard (sin mensaje, proceso silencioso) |

## 7. Definiciones técnicas

### Stack tecnológico

- **Framework:** .NET 8 MAUI + Blazor Hybrid
- **UI:** Razor components + Tailwind CSS v4 (tema Navy, dark mode)
- **Base de datos:** SQLite via EF Core (Microsoft.EntityFrameworkCore.Sqlite)
- **Acceso a datos:** `StockDbContext` (hereda de `DbContext`), sin uso de EF Core Migrations formales
- **Preferencias de usuario:** `Microsoft.Maui.Storage.Preferences`
- **Filesystem:** `Microsoft.Maui.Storage.FileSystem`

### Estrategia de inicialización (thread safety)

`App.xaml.cs` constructor ejecuta:

```csharp
Task.Run(async () => await dataService.InitializeAsync()).GetAwaiter().GetResult();
```

- `Task.Run(...)`: mueve la ejecución al thread pool, evitando deadlock por `SynchronizationContext` en el hilo principal de MAUI.
- `.GetAwaiter().GetResult()`: bloquea el hilo principal hasta que la tarea complete, garantizando orden estricto antes de `MainPage = new MainPage()`.

### Estrategia de migración de esquema

Se usa `EnsureCreatedAsync()` (no `MigrateAsync()`) porque `dotnet ef` no puede ejecutarse sobre proyectos MAUI multi-target. Para cambios post-creación se aplican scripts SQL raw con verificación `PRAGMA table_info()`. Este patrón es apropiado para una aplicación de usuario único, offline y de un solo dispositivo.

### Conexión a la DB durante migraciones

El método `InitializeDatabaseAsync()` abre explícitamente la conexión con `connection.OpenAsync()` si estaba cerrada, y la vuelve a cerrar al finalizar los scripts. Esto evita conflictos con el pool de conexiones de EF Core durante la ejecución de comandos raw.

### Backup automático: mecanismo

`BackupService.ExecuteBackupToFolderAsync()` realiza una copia del archivo `stock.db` usando streams con `FileShare.ReadWrite` en origen y `FileShare.None` en destino. No usa el modo WAL de SQLite ni `VACUUM INTO`, por lo que el backup puede incluir páginas no confirmadas si hubiera una transacción abierta (bajo riesgo dado que se ejecuta antes de mostrar la UI).

### Retención de backups

Al crear un backup, se eliminan los archivos `Backup_Stock_*.db` más antiguos si superan los 15 archivos en la carpeta destino.

### Backup de cierre

Implementado como event handler en `MauiProgram.cs`:
- **Windows:** `window.Closed` → `Task.Run(() => backup.ExecuteClosingBackupAsync(folder))`
- **Android:** `OnStop` activity → `Task.Run(() => backup.ExecuteClosingBackupAsync(folder))`

El archivo de cierre siempre se llama `Backup_Stock_UltimoCierre.db` y sobreescribe el anterior.

### Configuración de ventana (Windows)

Al iniciar en Windows, la ventana se maximiza mediante `OverlappedPresenter.Maximize()`. El ícono de la app se carga desde `custom_appicon.ico` en el directorio base del ejecutable.

### Modo del WebView (Android)

El `BlazorWebView` en Android usa un `PermissionWebChromeClient` personalizado que auto-aprueba todos los permisos solicitados (actualmente necesario para la cámara en el lector de código de barras).

## 8. Seguridad y permisos

La aplicación no implementa autenticación ni autorización de usuarios. Está diseñada para correr en el dispositivo del negocio como aplicación de acceso local directo. No hay roles ni sesiones.

**Permisos de sistema requeridos:**

| Plataforma | Permiso | Motivo |
|---|---|---|
| Windows | Acceso a `%LOCALAPPDATA%` | Creación y lectura de `stock.db` |
| Windows | Acceso a carpeta elegida por el usuario | Backup automático y manual |
| Android | Almacenamiento externo (si aplica) | Backup a carpeta elegida |
| Android | Cámara | Lector de código de barras (auto-aprobado en WebView) |

**Consideraciones de integridad de datos:**

- La DB no está cifrada. Si el dispositivo es comprometido físicamente, los datos son accesibles.
- No hay checksum ni firma del archivo de DB para detectar corrupción externa.
- El backup automático protege contra pérdida de datos por falla del dispositivo, no contra acceso no autorizado.

## 9. Criterios de aceptación

### CA-01: Primer arranque — creación de DB nueva

- [ ] Dado que `stock.db` no existe en `FileSystem.AppDataDirectory`, cuando el usuario abre la app por primera vez, entonces el archivo `stock.db` es creado, todas las tablas definidas en `OnModelCreating` existen con su estructura correcta, y se registra una fila en `Configuraciones` con `NombreNegocio = "Comercial Kai Ken"` y `Moneda = "ARS"`.

### CA-02: Arranque con DB existente — idempotencia de scripts

- [ ] Dado que `stock.db` existe con el esquema completo y actualizado, cuando el usuario abre la app, entonces todos los scripts de migración se ejecutan sin error (las verificaciones `PRAGMA table_info` detectan que las columnas ya existen y omiten los `ALTER TABLE`), y la DB no sufre modificaciones.

### CA-03: Arranque con DB desactualizada — aplicación de scripts pendientes

- [ ] Dado que `stock.db` existe pero le faltan columnas agregadas en versiones posteriores (por ej., `Productos.CodigoBarras`), cuando el usuario abre la app, entonces los scripts detectan las columnas faltantes y las agregan con sus valores por defecto, sin perder datos existentes.

### CA-04: Orden garantizado — WebView no carga antes que la DB

- [ ] Dado cualquier condición de arranque, cuando la inicialización está en curso, entonces `MainPage` (y el WebView) no se instancian hasta que `dataService.InitializeAsync()` retorna (exitosamente o con excepción capturada).

### CA-05: Backup automático — condición de ejecución correcta

- [ ] Dado que hay una carpeta de backup configurada en `Preferences` (`Backup.TargetFolder`) que existe en el filesystem, y han pasado más de 24 horas desde `Backup.LastRunUtc`, cuando el usuario abre la app, entonces se crea un archivo `Backup_Stock_yyyyMMdd_HHmm.db` en esa carpeta y se actualiza `Backup.LastRunUtc` con el timestamp actual.

### CA-06: Backup automático — condición de NO ejecución

- [ ] Dado que hay carpeta configurada pero `Backup.LastRunUtc` indica que el último backup fue hace menos de 24 horas, cuando el usuario abre la app, entonces no se crea ningún archivo de backup.
- [ ] Dado que no hay carpeta configurada en `Backup.TargetFolder` (o la carpeta no existe), cuando el usuario abre la app, entonces no se crea ningún archivo de backup y la app arranca normalmente.

### CA-07: Backup automático — silencioso

- [ ] Dado que el backup automático se ejecuta (o falla) durante el arranque, entonces no se muestra ningún mensaje, toast, diálogo ni notificación al usuario.

### CA-08: Retención de backups

- [ ] Dado que la carpeta de backup contiene 16 o más archivos `Backup_Stock_*.db`, cuando se ejecuta un nuevo backup automático, entonces el archivo más antiguo es eliminado, dejando como máximo 15 archivos (más el nuevo = 15 total).

### CA-09: Seeding — no repetición

- [ ] Dado que `stock.db` ya tiene al menos una fila en `Configuraciones`, cuando el usuario abre la app, entonces no se agrega ninguna fila adicional de seeding.

### CA-10: Error de DB — arranque defensivo

- [ ] Dado que `InitializeAsync()` lanza una excepción (por ej., archivo corrupto), cuando el error es capturado en `App.xaml.cs`, entonces el error se registra en `Debug.WriteLine` y la app continúa instanciando `MainPage` (el usuario ve la pantalla principal, aunque la DB esté inaccesible).

### CA-11: Backup de cierre — Windows

- [ ] Dado que hay carpeta de backup configurada, cuando el usuario cierra la ventana de la app en Windows, entonces se crea o sobreescribe `Backup_Stock_UltimoCierre.db` en la carpeta configurada.

### CA-12: Backup de cierre — Android

- [ ] Dado que hay carpeta de backup configurada, cuando la actividad Android recibe `OnStop`, entonces se crea o sobreescribe `Backup_Stock_UltimoCierre.db` en la carpeta configurada.

### CA-13: Soft delete — filtros globales activos desde el inicio

- [ ] Dado que existen registros con `IsDeleted = true` en `Productos`, `Clientes`, `Ventas` y `Presupuestos`, cuando cualquier componente Blazor consulta esas entidades luego de la inicialización, entonces los registros con `IsDeleted = true` no aparecen en los resultados (filtro global activo por defecto).

### CA-14: Ventana maximizada en Windows

- [ ] Dado que la app se abre en Windows, cuando `MainPage` se renderiza, entonces la ventana está maximizada (no en pantalla completa kiosco).

## 10. Casos borde y manejo de errores

### DB inexistente + carpeta de backup no accesible al mismo tiempo

Si es el primer arranque (`stock.db` no existe) y la carpeta de backup configurada en `Preferences` ya no existe en el filesystem, el backup automático retorna `Result.Fail` con mensaje descriptivo y se descarta silenciosamente. La creación de la DB y el seeding inicial no se ven afectados.

### Archivo `stock.db` bloqueado por otro proceso

Si otro proceso tiene el archivo `stock.db` con lock exclusivo cuando la app intenta abrirlo, `EnsureCreatedAsync()` lanzará una `SqliteException`. Este error es capturado por el `try/catch` en `App.xaml.cs` y logueado. La app arrancará en estado degradado.

### Archivo `stock.db` corrupto

Si el archivo existe pero está corrupto (encabezado SQLite inválido), `EnsureCreatedAsync()` lanzará una excepción. Mismo comportamiento que el punto anterior: log + arranque degradado. El usuario deberá restaurar un backup manualmente desde la UI (que estará inaccesible si la DB no cargó). Mitigación esperada: el backup de cierre debería proveer un archivo válido reciente.

### Script de migración falla (columna existe con tipo diferente)

SQLite no soporta `ALTER COLUMN`. Si una columna ya existe pero con un tipo diferente al esperado por el modelo EF Core, `PRAGMA table_info` detecta que la columna existe y el script de `ALTER TABLE ADD COLUMN` se omite (no se ejecuta). No hay reintento ni corrección automática. El comportamiento en runtime dependerá de la compatibilidad de tipos (puede ser silencioso si SQLite hace coerción, o fallar en consultas específicas).

### Backup automático: carpeta destino sin permisos de escritura

`ExecuteBackupToFolderAsync` lanzará una `UnauthorizedAccessException` o `IOException` al intentar escribir el archivo. La excepción es capturada por el `try/catch` del método, que retorna `Result.Fail`. Durante el arranque este resultado se descarta silenciosamente. El backup de cierre tiene el mismo comportamiento.

### Backup automático: disco lleno en carpeta destino

Mismo flujo que el caso anterior: `IOException` capturada, `Result.Fail` descartado silenciosamente.

### Backup automático: `Backup.LastRunUtc` corrupto en Preferences

Si el valor almacenado no es parseable como `DateTime`, `DateTime.TryParse` retorna `false` y `lastRunUtc` queda como `DateTime.MinValue`. En ese caso, `elapsed` será mayor a 24 horas (siempre), y el backup se ejecutará en el próximo arranque.

### Backup de cierre en Windows: app cerrada abruptamente (kill de proceso)

El evento `window.Closed` no se dispara si el proceso es terminado por el sistema operativo (kill, crash). En ese caso no se genera el backup de cierre. El backup automático de inicio del próximo arranque cubre parcialmente este riesgo.

### Primer arranque + seeding: `SaveChangesAsync` falla

Si el seeding inicial lanza una excepción en `SaveChangesAsync`, la excepción se propaga hacia `InitializeDatabaseAsync`, luego a `DataService.InitializeAsync`, luego al `Task.Run` en `App.xaml.cs`, donde es capturada y logueada. La `ConfiguracionApp` no queda persistida; las consultas posteriores que dependan de ella recibirán `null` y deberán manejarlo defensivamente.

### Arranque durante actualización de la app (versión nueva con nuevos scripts)

Cuando se instala una nueva versión que agrega nuevos scripts de migración, el primer arranque post-actualización ejecuta todos los scripts nuevos. Los scripts existentes (ya aplicados) se saltan por la verificación `PRAGMA`. Los scripts nuevos se aplican en orden. Si un script nuevo falla, la excepción se propaga con el mismo comportamiento de error de DB descripto arriba.

### Tiempo de inicialización excesivo

No hay timeout configurado en `Task.Run(...).GetAwaiter().GetResult()`. Si la inicialización cuelga (por ej., por lock de archivo), el hilo principal queda bloqueado indefinidamente y la app aparece congelada. El sistema operativo puede eventualmente mostrar el diálogo de "la aplicación no responde".

## 11. Preguntas abiertas

1. **Pantalla de carga:** ¿Se desea agregar algún indicador visual (splash, spinner) durante la inicialización para dar feedback al usuario en dispositivos lentos o en el primer arranque? Actualmente no existe ninguno.

2. **Manejo de DB corrupta:** ¿Debe existir un mecanismo de recuperación automática (por ej., renombrar el archivo corrupto y crear una DB nueva) en lugar del actual arranque degradado silencioso?

3. **Timeout de inicialización:** ¿Se debe implementar un timeout para `InitializeAsync()` que, si se supera, muestre un mensaje de error al usuario en lugar de congelar la app?

4. **Integridad de la DB en arranque:** ¿Se debe ejecutar `PRAGMA integrity_check` (o `PRAGMA quick_check`) en cada arranque para detectar corrupción proactivamente, antes de intentar usar la DB?

5. **Logging persistente:** ¿Los errores de inicialización deben escribirse a un archivo de log en disco (además de `Debug.WriteLine`) para facilitar el diagnóstico en producción?

6. **Nuevos scripts de migración:** ¿Existe un lineamiento sobre cómo incorporar nuevos scripts al método `InitializeDatabaseAsync()` (por ej., tabla de versiones de esquema, o convención de numeración de scripts)?
