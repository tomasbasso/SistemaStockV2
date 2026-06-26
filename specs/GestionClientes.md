# Spec: Gestión de Clientes y Cuentas Corrientes

> Fecha: 2026-06-17 · Estado: Borrador · Origen: historia de usuario

## 1. Historia de usuario original

"El negocio tiene clientes que compran regularmente y algunos de ellos compran a cuenta corriente (fiado). El dueño necesita administrar la cartera de clientes: cargar sus datos comerciales, ver cuánto le debe cada uno, registrar pagos parciales o totales de deudas, ver el historial de compras fiadas de cada cliente, contactarlos rápidamente por WhatsApp, y exportar un estado de cuenta formal en PDF para entregarles."

---

## 2. Objetivo

Permitir al dueño del negocio gestionar su cartera de clientes con un foco en la deuda de cuenta corriente (fiado): registrar y editar clientes con datos comerciales completos, controlar cuánto debe cada uno, registrar cobros (parciales o totales), revisar el historial de ventas fiadas y generar un estado de cuenta en PDF listo para entregar. Resuelve la necesidad de tener visibilidad centralizada de la deuda de los clientes sin recurrir a anotaciones manuales o planillas externas.

---

## 3. Alcance

### Incluye
- Alta, edición y eliminación lógica (soft delete) de clientes con datos comerciales: nombre, teléfono, dirección, CUIT, email y condición de IVA.
- Búsqueda/filtro de clientes en tiempo real por nombre, teléfono y CUIT.
- Visualización del balance de cuenta corriente por cliente, con indicadores de color (rojo para deuda, verde para saldo cero o a favor).
- Registro de pagos parciales o totales sobre la deuda de un cliente, con generación automática de un MovimientoFinanciero de tipo Ingreso en la caja general.
- Modal de detalle con historial cronológico de todas las ventas fiadas del cliente, incluyendo productos y subtotales.
- Acceso directo a WhatsApp del cliente mediante apertura de `wa.me/54XXXXXXXXXX` (solo cuando hay teléfono cargado).
- Exportación de estado de cuenta en PDF (formato A4) con membrete del negocio, datos del cliente, saldo consolidado y detalle de ventas fiadas.

### No incluye (fuera de alcance)
- Eliminación física de clientes de la base de datos.
- Gestión de crédito o límites de endeudamiento por cliente.
- Envío de mensajes de WhatsApp desde la app (solo abre la conversación en la app externa).
- Notificaciones automáticas de deuda vencida o recordatorios.
- Asociación de múltiples cuentas corrientes por cliente (una sola CC por cliente).
- Historial de pagos registrados (los cobros quedan registrados como MovimientosFinancieros, no en una entidad de pagos propia).
- Módulo de facturación electrónica o integración con AFIP.
- Importación masiva de clientes desde Excel u otro formato.

---

## 4. Definiciones funcionales

### Cartera de clientes
- El único campo obligatorio para crear un cliente es el **nombre**. El resto (teléfono, dirección, CUIT, email) son opcionales.
- La **condición de IVA** es obligatoria y tiene un valor por defecto: `ConsumidorFinal`. Las opciones disponibles son: Monotributista, Responsable Inscripto, Monotributo Social, Exento, Consumidor Final, No Responsable, Sujeto No Categorizado.
- El **CUIT**, si se ingresa, debe respetar el formato `XX-XXXXXXXX-X`. El email, si se ingresa, debe ser un formato válido.
- La eliminación de un cliente es siempre lógica: el campo `IsDeleted` se marca como `true` y el cliente deja de aparecer en la lista. La cuenta corriente asociada se elimina físicamente al mismo tiempo.
- No puede haber dos clientes con el mismo Id (Guid), pero el nombre no es único por diseño.

### Búsqueda
- El campo de búsqueda filtra en tiempo real (evento `oninput`) sobre los clientes visibles por nombre, teléfono y CUIT.
- Si la búsqueda no arroja resultados, se muestra un estado vacío con el término buscado.

### Cuenta corriente
- Cada cliente tiene exactamente una cuenta corriente (`CuentaCorriente`), creada automáticamente al dar de alta el cliente con `Balance = 0`.
- El `Balance` se almacena como `decimal` en SQLite con tipo de columna TEXT.
- **Balance positivo** = el cliente le debe dinero al negocio → se resalta en rojo con leyenda "Deuda pendiente".
- **Balance cero o negativo** = sin deuda o a favor del cliente → se resalta en verde.
- El balance aumenta automáticamente cuando se registra una venta marcada como `IsFiado = true` en el Punto de Venta: la venta suma su `Total` al balance de la cuenta corriente del cliente.

### Registro de cobros (Saldar Deuda)
- El botón "Saldar Deuda" solo aparece cuando el balance de la cuenta corriente es **mayor a cero** (el cliente tiene deuda efectiva).
- El modal de cobro pre-carga el monto con el total adeudado (pago completo por defecto), pero el usuario puede ingresar un monto parcial.
- El monto ingresado debe ser **mayor a cero** y **menor o igual al balance actual**. El botón de confirmar queda deshabilitado si no se cumple esta condición.
- Al confirmar:
  1. Se crea un `MovimientoFinanciero` de tipo `Ingreso` con la descripción `"Cobro C/C - {NombreCliente}"` y el monto abonado.
  2. Se reduce el `Balance` de la `CuentaCorriente` del cliente en el monto abonado.
  3. Se muestra una notificación de éxito y la lista se recarga.
- Las dos operaciones anteriores se ejecutan de forma secuencial en el servicio. No hay transacción de base de datos explícita para el cobro (la atomicidad es manejada a nivel de `SaveChangesAsync` por separado).

### Historial de ventas fiadas (Ver Detalle)
- El modal de detalle muestra **todas las ventas con `IsFiado = true`** asociadas al cliente, ordenadas cronológicamente de manera descendente (más recientes primero).
- Por cada venta se muestra: número de venta, fecha y hora, lista de productos con cantidad y precio unitario, y total de la venta.
- El historial incluye todas las ventas fiadas del cliente, independientemente de si ya fueron saldadas parcialmente o no. No hay filtro por "saldo pendiente por venta".
- La suma de los totales de las ventas fiadas puede diferir del `Balance` actual si ya se registraron pagos parciales.
- Si el cliente no tiene ventas fiadas, el historial muestra un mensaje vacío: "No hay historial de compras fiadas para este cliente."

### WhatsApp
- Si el cliente tiene teléfono cargado, se muestra un botón con ícono de WhatsApp.
- Al hacer clic, se limpia el número eliminando caracteres no numéricos. Si el número resultante no empieza con "54" (código de Argentina) y tampoco con "1" (código de EE.UU. u otro), se antepone el prefijo "54".
- Se abre la URL `https://wa.me/{numero}` usando el `Launcher` de .NET MAUI (abre la app nativa de WhatsApp o WhatsApp Web según el dispositivo).
- Si el cliente no tiene teléfono cargado, el botón de WhatsApp no se muestra.

### Exportación de PDF
- El PDF se genera con QuestPDF en formato A4.
- El membrete incluye: nombre del negocio, dirección y teléfono (tomados de `ConfiguracionApp`). Si alguno de estos campos está vacío en la configuración, simplemente no se imprime esa línea.
- Los datos del cliente incluyen: nombre, teléfono, dirección, CUIT y email (solo los que no estén vacíos).
- El saldo consolidado muestra el `Balance` actual de la cuenta corriente.
- El detalle incluye todas las ventas fiadas con número, fecha, productos y total de cada venta, ordenadas de más reciente a más antigua.
- El documento incluye un aviso al pie: "Este documento es un resumen informativo del estado de cuenta corriente. No tiene validez como comprobante fiscal."
- Al generarse, se abre el diálogo de guardado nativo del sistema operativo (`FileSaver`) con el nombre de archivo sugerido: `EstadoCuenta_{NombreCliente}_{FechaHora}.pdf`.

---

## 5. Datos y modelo

### Entidades persistidas en SQLite

| Entidad | Campo | Tipo C# | Tipo SQLite | Notas |
|---|---|---|---|---|
| `Cliente` | `Id` | `Guid` | TEXT PK | Auto-generado |
| | `Name` | `string` | TEXT NOT NULL | Obligatorio, max 200 chars |
| | `Phone` | `string?` | TEXT | Opcional, max 50 chars |
| | `Address` | `string?` | TEXT | Opcional, max 300 chars |
| | `CUIT` | `string?` | TEXT | Opcional, formato `XX-XXXXXXXX-X`, max 13 chars |
| | `Email` | `string?` | TEXT | Opcional, formato email válido, max 200 chars |
| | `CondicionIva` | `CondicionIva` (enum) | TEXT | Persistido como string. Default: `ConsumidorFinal` |
| | `IsDeleted` | `bool` | INTEGER | Default 0. Query filter activo: solo se ven los no eliminados |
| `CuentaCorriente` | `Id` | `Guid` | TEXT PK | Auto-generado |
| | `ClienteId` | `Guid` | TEXT | FK hacia Cliente.Id. Índice único (una CC por cliente) |
| | `Balance` | `decimal` | TEXT | Positivo = deuda; Negativo = a favor; Cero = sin deuda |
| `MovimientoFinanciero` | `Id` | `Guid` | TEXT PK | Auto-generado al registrar cobro |
| | `Type` | `TipoMovimiento` | TEXT | `Ingreso` al registrar cobro de CC |
| | `Amount` | `decimal` | TEXT | Monto del cobro |
| | `Date` | `DateTime` | TEXT | Fecha/hora del cobro |
| | `Description` | `string` | TEXT | `"Cobro C/C - {NombreCliente}"` |
| `Venta` | `IsFiado` | `bool` | INTEGER | `true` → suma al balance de la CC del cliente |
| | `ClienteId` | `Guid?` | TEXT | Si es fiado, debe estar presente |

### DTOs (sin persistencia)

| DTO | Uso |
|---|---|
| `VentaFiadaDetalle` | Proyección de `Venta` + `VentaDetalle` + `Producto` para el historial y el PDF. Campos: `NumeroVenta`, `Fecha`, `Total`, `Items` (lista de strings con formato `"{Qty}x {Nombre} ({Precio:C})"`) |
| `EstadoCuentaData` | Datos para el PDF. Campos: `Cliente`, `CuentaCorriente`, `Config`, `VentasFiadas`, `FechaGeneracion` |

### Enum `CondicionIva`
```
ResponsableInscripto = 0
Monotributista       = 1
MonotributoSocial    = 2
Exento               = 3
ConsumidorFinal      = 4
NoResponsable        = 5
SujetoNoCategorizado = 6
```

### Relaciones
- `Cliente` 1 ↔ 1 `CuentaCorriente` (creada automáticamente al dar de alta el cliente)
- `Cliente` 1 ↔ N `Venta` (a través de `Venta.ClienteId`)
- `Venta` 1 ↔ N `VentaDetalle`

---

## 6. UX / Interfaz

### Pantalla principal: `/clientes` (`Clientes.razor`)

**Estado vacío (sin clientes):**
- Ícono de personas, mensaje "No hay clientes registrados", descripción y link "Crear Cliente →".

**Estado con clientes:**
- Barra de búsqueda en tiempo real (max-w-md) con ícono de lupa, placeholder "Buscar por nombre, tel o CUIT...".
- Grid de tarjetas (1 columna en mobile, 2 en lg+). Cada tarjeta muestra:
  - Avatar con ícono de persona.
  - Nombre del cliente (texto grande y bold).
  - Teléfono (o "Sin teléfono" si está vacío).
  - Dirección, CUIT y email (solo si están cargados).
  - Condición de IVA.
  - Balance de cuenta corriente (alineado a la derecha):
    - Texto "Balance C/C" (etiqueta).
    - Monto formateado como moneda.
    - Color rojo + leyenda "Deuda pendiente" si balance > 0.
    - Color verde si balance <= 0.
  - Acciones (fila de botones en el pie de la tarjeta):
    - **"Ver Detalle"** (siempre visible) → abre modal de detalle de cuenta corriente.
    - **"Saldar Deuda"** (solo si balance > 0) → abre modal de pago.
    - **WhatsApp** (solo si tiene teléfono) → abre WhatsApp.
    - **Editar** (lápiz) → abre modal de edición.
    - **Eliminar** (papelera, en rojo) → abre modal de confirmación.

**Estado de búsqueda sin resultados:**
- Ícono de lupa, mensaje "No se encontraron clientes para "@termino"".

**Estado de carga inicial:**
- Spinner centrado en una card de altura mínima 400px.

### Modal: Nuevo / Editar Cliente
- Título: "Nuevo Cliente" o "Editar Cliente" según el modo.
- Campos del formulario:
  - **Nombre Completo** (obligatorio, `autofocus`).
  - **Teléfono** (opcional).
  - **Dirección** (opcional).
  - **CUIT** (opcional, placeholder `20-12345678-9`).
  - **Email** (opcional, tipo email, placeholder `cliente@ejemplo.com`).
  - **Condición IVA** (select obligatorio, default ConsumidorFinal).
- Validación: usa `DataAnnotationsValidator`. Los mensajes de error se muestran debajo de cada campo en rojo.
- Botones: "Cancelar" (cierra modal) y "Guardar" (submit).

### Modal: Saldar Deuda
- Título: "Saldar Deuda: C/C".
- Muestra nombre del cliente y deuda total formateada en rojo.
- Campo numérico para ingresar el monto, pre-cargado con el total adeudado.
- Texto aclaratorio: "Esto generará un Ingreso Financiero y reducirá el balance de la Cuenta Corriente."
- Botón "Registrar Pago" deshabilitado si `monto <= 0` o `monto > balance`.

### Modal: Detalle de Cuenta Corriente
- Encabezado con nombre del cliente, balance actual (coloreado) y botón "Descargar PDF".
- Sección "Historial de Compras en Cuenta Corriente":
  - Si no hay historial: texto italic "No hay historial de compras fiadas para este cliente."
  - Si hay historial: lista scrolleable (max-height 300px) de tarjetas por venta, con fecha, número de venta, lista de productos y total.
- Muestra spinner mientras carga el historial.

### Modal: Confirmar Eliminación
- Ícono de alerta en rojo, nombre del cliente, aclaración sobre eliminación de la cuenta corriente asociada.
- Botones: "Cancelar" y "Eliminar" (en rojo).

---

## 7. Definiciones técnicas

### Stack y plataforma
- **Framework:** .NET 8 MAUI Blazor Hybrid (app de escritorio Windows y potencialmente Android/iOS).
- **UI:** Blazor Components (`.razor`) con Tailwind CSS y Bootstrap Icons (`bi-*`).
- **ORM:** Entity Framework Core 8 con proveedor SQLite.
- **Base de datos:** SQLite local (archivo único), sin servidor externo.
- **Esquema:** Gestionado con `EnsureCreatedAsync` + migraciones manuales en `InitializeDatabaseAsync`. No se usa `dotnet ef migrations` por incompatibilidad con proyectos MAUI multi-target.

### Servicios involucrados

| Servicio | Responsabilidad en este módulo |
|---|---|
| `DataService` | Todas las operaciones CRUD sobre `Cliente`, `CuentaCorriente` y `MovimientoFinanciero`; también `GetVentasFiadasPorClienteAsync` y `GetConfiguracionAsync` |
| `PdfService` | Genera el PDF del estado de cuenta con `GenerarEstadoCuenta(EstadoCuentaData)` usando QuestPDF |
| `NotificationService` | Muestra toasts de éxito/error al usuario |

### Métodos clave de DataService

| Método | Descripción |
|---|---|
| `GetClientesAsync()` | Devuelve todos los clientes activos (query filter excluye `IsDeleted = true`), ordenados por nombre |
| `GetCuentasCorrientesAsync()` | Devuelve todas las cuentas corrientes (sin filtro de eliminados) |
| `SaveClienteAsync(Cliente)` | Insert (+ crea CC con balance 0) o update del cliente |
| `DeleteClienteAsync(Guid)` | Soft delete del cliente + eliminación física de su CC |
| `GetCuentaCorrienteAsync(Guid clienteId)` | Obtiene la CC de un cliente específico |
| `SaveCuentaCorrienteAsync(CuentaCorriente)` | Actualiza el balance de una CC existente |
| `AddMovimientoAsync(MovimientoFinanciero)` | Registra un movimiento financiero en la caja general |
| `GetVentasFiadasPorClienteAsync(Guid clienteId)` | Proyecta ventas con `IsFiado = true` del cliente a `List<VentaFiadaDetalle>` |
| `GetConfiguracionAsync()` | Obtiene la configuración del negocio para el membrete del PDF |

### Apertura de WhatsApp
- Se usa `Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(Uri)` para abrir la URL `https://wa.me/{numero}` en el sistema operativo.
- Limpieza del número: `phone.Where(char.IsDigit)` → si no empieza en "54" ni en "1", se antepone "54".

### Exportación de PDF
- Generación sincrónica con `PdfService.GenerarEstadoCuenta()` → retorna `byte[]`.
- Guardado con `CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync()` ejecutado en el hilo principal (`MainThread.InvokeOnMainThreadAsync`).
- El nombre de archivo limpia caracteres inválidos para el sistema de archivos: `string.Concat(nombre.Split(Path.GetInvalidFileNameChars()))`.

### Persistencia de decimales en SQLite
- EF Core almacena todos los campos `decimal` (`Balance`, `Amount`, `Price`, `Total`, etc.) como `TEXT` en SQLite para evitar pérdidas de precisión. Esta convención aplica a toda la base de datos.

---

## 8. Seguridad y permisos

- La aplicación es monousuario, de uso local y offline. No existe sistema de autenticación ni roles diferenciados.
- No hay restricciones de permisos a nivel de funcionalidad: el usuario único tiene acceso completo a todas las operaciones (crear, editar, eliminar, cobrar, exportar).
- Los datos sensibles del cliente (CUIT, email, teléfono) se almacenan en SQLite local en el dispositivo del usuario. No se envían a servicios externos.
- La integración de WhatsApp no transmite datos hacia la app: solo abre una URL en el sistema operativo.

---

## 9. Criterios de aceptación

### CRUD de clientes

- [ ] Dado que el usuario hace clic en "Nuevo Cliente", cuando completa el nombre y guarda, entonces el cliente aparece en la lista y se muestra un toast "Cliente creado con éxito."
- [ ] Dado que el usuario intenta guardar un cliente sin nombre, cuando envía el formulario, entonces se muestra el mensaje de validación "El nombre del cliente es obligatorio." y el cliente no se persiste.
- [ ] Dado que el usuario ingresa un CUIT con formato incorrecto (ej: "123456"), cuando intenta guardar, entonces se muestra el mensaje "Formato inválido. Use XX-XXXXXXXX-X." y el cliente no se persiste.
- [ ] Dado que el usuario ingresa un email con formato inválido, cuando intenta guardar, entonces se muestra el mensaje "El email no es válido." y el cliente no se persiste.
- [ ] Dado que el usuario hace clic en "Editar" sobre un cliente existente, cuando modifica algún campo y guarda, entonces los datos se actualizan en la lista y se muestra "Cliente actualizado."
- [ ] Dado que el usuario hace clic en "Eliminar" y confirma, cuando la operación finaliza, entonces el cliente desaparece de la lista, su cuenta corriente ya no existe en la base de datos, y se muestra "Cliente '{nombre}' eliminado."
- [ ] Dado que existe un cliente eliminado, cuando se consulta la lista de clientes, entonces el cliente eliminado no aparece (query filter activo).

### Búsqueda

- [ ] Dado que existen múltiples clientes, cuando el usuario escribe en el buscador, entonces la lista se filtra instantáneamente por nombre, teléfono o CUIT (búsqueda case-insensitive).
- [ ] Dado que el término de búsqueda no coincide con ningún cliente, entonces se muestra el estado "No se encontraron clientes para '@termino'".

### Visualización de balance

- [ ] Dado que un cliente tiene balance > 0, entonces su tarjeta muestra el balance en rojo y la leyenda "Deuda pendiente."
- [ ] Dado que un cliente tiene balance = 0 o < 0, entonces su tarjeta muestra el balance en verde sin leyenda de deuda.

### Saldar deuda

- [ ] Dado que un cliente tiene balance = 0, entonces el botón "Saldar Deuda" no aparece en su tarjeta.
- [ ] Dado que un cliente tiene balance > 0 y el usuario abre el modal de pago, entonces el campo de monto viene pre-cargado con el total adeudado.
- [ ] Dado que el usuario ingresa un monto > 0 y <= balance y confirma el pago, entonces: (1) el balance de la CC se reduce en ese monto, (2) se crea un MovimientoFinanciero de tipo Ingreso con descripción "Cobro C/C - {nombre}", (3) se muestra el toast "Pago de {monto:C} registrado para {nombre}." y (4) la lista se recarga con el balance actualizado.
- [ ] Dado que el usuario ingresa un monto mayor al balance en el campo de pago, entonces el botón "Registrar Pago" permanece deshabilitado.
- [ ] Dado que el usuario ingresa un monto <= 0, entonces el botón "Registrar Pago" permanece deshabilitado.

### Historial de ventas fiadas

- [ ] Dado que el usuario abre el modal "Ver Detalle" de un cliente con ventas fiadas, entonces se muestra la lista cronológica (más reciente primero) con número de venta, fecha, productos y total de cada venta.
- [ ] Dado que el usuario abre el modal "Ver Detalle" de un cliente sin ventas fiadas, entonces se muestra el mensaje "No hay historial de compras fiadas para este cliente."
- [ ] Dado que el modal de detalle está cargando, entonces se muestra un spinner de carga.

### WhatsApp

- [ ] Dado que un cliente tiene teléfono cargado, entonces aparece el botón de WhatsApp en su tarjeta.
- [ ] Dado que el usuario hace clic en el botón de WhatsApp, entonces se abre la URL `https://wa.me/54{numero_limpio}` (o `https://wa.me/{numero}` si ya tiene prefijo internacional) en el sistema operativo.
- [ ] Dado que un cliente no tiene teléfono cargado, entonces el botón de WhatsApp no aparece en su tarjeta.

### Exportación de PDF

- [ ] Dado que el usuario está en el modal de detalle de un cliente y hace clic en "Descargar PDF", entonces se genera un PDF A4 y se abre el diálogo de guardado nativo con el nombre `EstadoCuenta_{nombre}_{yyyyMMdd_HHmm}.pdf`.
- [ ] Dado que el PDF se genera exitosamente, entonces se muestra el toast "PDF guardado correctamente."
- [ ] Dado que los datos de configuración del negocio están vacíos (nombre vacío, sin dirección, sin teléfono), entonces el PDF se genera igualmente: el membrete muestra los campos disponibles y omite los vacíos.
- [ ] Dado que el cliente no tiene ventas fiadas, entonces el PDF incluye el mensaje "No hay ventas en cuenta corriente registradas para este cliente." en la sección de detalle.
- [ ] Dado que el PDF incluye ventas fiadas, entonces la suma de totales de las ventas se muestra al pie de la tabla como "TOTAL DEUDA EN CUENTA CORRIENTE".

---

## 10. Casos borde y manejo de errores

- **Cliente sin teléfono:** el botón de WhatsApp no aparece. No se genera error; simplemente no se renderiza el elemento.
- **Pago mayor al saldo pendiente:** el botón "Registrar Pago" se deshabilita con `disabled="@(paymentAmount <= 0 || paymentAmount > (settleCc?.Balance ?? 0))"`. No es posible persistir un pago mayor al balance desde la UI.
- **Cliente sin compras fiadas:** el modal de detalle muestra el estado vacío con texto italic. El botón "Descargar PDF" sigue disponible y genera el PDF con la sección de historial vacía.
- **PDF con datos de empresa vacíos:** `ConfiguracionApp` puede tener `DireccionNegocio` y `Telefono` como strings vacíos. El `PdfService` solo renderiza esas líneas si `!string.IsNullOrEmpty(campo)`. Si `NombreNegocio` está vacío, la app tiene valor por defecto "Mi Negocio" en el modelo; de no tener configuración cargada, el PDF usa "SistemaDeStockV3" como fallback.
- **Error al guardar cliente:** si `DataService.SaveClienteAsync` lanza una excepción, se captura en el handler `catch` de `Clientes.razor` y se muestra un toast "Error al guardar cliente: {mensaje}".
- **Error al registrar pago:** si alguno de los dos pasos del cobro falla (crear movimiento o actualizar CC), se captura la excepción y se muestra "Error al registrar el pago: {mensaje}". Las dos operaciones no están en una transacción explícita, por lo que podría quedar inconsistencia si falla el segundo paso; se acepta este riesgo dado el contexto monousuario.
- **Error al exportar PDF:** si `PdfService.GenerarEstadoCuenta` o `FileSaver.SaveAsync` falla, se muestra "Error al exportar PDF: {mensaje}".
- **Error al eliminar:** si `DataService.DeleteClienteAsync` falla, se muestra "Error al eliminar: {mensaje}".
- **Estado de carga inicial:** mientras `GetClientesAsync` y `GetCuentasCorrientesAsync` resuelven, se muestra el spinner. Si ambas listas están vacías tras la carga, se muestra el estado vacío.
- **Número de teléfono con prefijo internacional ya cargado:** si el número ya empieza en "54" o en "1", no se antepone el prefijo "54" para evitar duplicación.

---

## 11. Preguntas abiertas

- **Inconsistencia de pago en dos pasos:** si el sistema falla entre la creación del `MovimientoFinanciero` y la actualización del balance de la `CuentaCorriente`, el estado queda inconsistente (el ingreso se registró pero el balance no bajó, o viceversa). ¿Se debe envolver el cobro en una transacción de base de datos explícita (`BeginTransactionAsync`) similar a la que ya existe en `ProcesarVentaAsync`?
- **Historial completo vs. historial pendiente:** actualmente el detalle muestra todas las ventas fiadas del cliente (incluso las ya "saldadas" por pagos previos), porque no se lleva un estado por-venta de si fue pagada. ¿Se quiere agregar en el futuro un marcado por-venta de "pagada / parcialmente pagada / pendiente"?
- **Soft delete y ventas:** al eliminar un cliente con deuda pendiente o ventas fiadas históricas, esas ventas siguen existiendo en la tabla `Ventas` con el `ClienteId` del cliente eliminado. ¿Es correcto mantener ese historial huérfano, o se requiere algún tipo de anonimización o vinculación visual?
- **Condición de IVA en el PDF:** la condición de IVA del cliente no se imprime en el estado de cuenta PDF actual. ¿Debería incluirse para documentos de mayor formalidad?
- **Búsqueda por email:** actualmente el filtro de búsqueda en la UI cubre nombre, teléfono y CUIT, pero no el email. ¿Se desea agregar el email al filtro de búsqueda?
