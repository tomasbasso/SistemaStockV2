# SistemaStockV2

.NET MAUI Blazor Hybrid · EF Core · SQLite local (`stock.db`)
App de escritorio single-user, offline. Sin autenticación, sin multi-usuario,
sin ningún concepto de tenant.

---

## Capa de datos

**`decimal` se persiste como TEXT en SQLite.**
SQLite no tiene tipo decimal nativo. Se guarda como TEXT para no perder
precisión, pero eso rompe el ordenamiento y las agregaciones en SQL: `ORDER BY`
y `SUM` sobre esas columnas comparan alfabéticamente.
→ Materializá con `.ToList()` antes de ordenar o agregar por montos.
→ Nunca compares rangos de precio del lado del servidor.

**No usar EF Core migrations.**
El esquema evoluciona a mano: `PRAGMA table_info(...)` para chequear si la
columna existe, y `ALTER TABLE` condicional si falta. Es lo que permite que la
app actualice bases ya instaladas en las PC de los clientes sin perder datos.
→ Si necesitás un cambio de esquema, seguí el patrón existente.
→ Nunca corras `dotnet ef migrations add`.

**`DbContext` registrado como Transient.**
En MAUI el contenedor vive todo el ciclo de la app; con Scoped o Singleton el
change tracker acumula entidades stale entre pantallas.
→ Si ves datos desactualizados en una vista, sospechá del ciclo de vida antes
  que de la query.

---

## MAUI

**`FileSaver` y `FilePicker` requieren MainThread.**
Llamarlos desde un hilo de background falla, a veces en silencio.
→ Envolvé siempre en `MainThread.InvokeOnMainThreadAsync(...)`.

**No cambiar el `LicenseType` de QuestPDF.**
Está fijado a propósito por licenciamiento. No es un detalle de configuración.

---

## Código muerto: ignoralo

Hubo una migración a Supabase (PostgreSQL + RLS + `TenantId` + autenticación)
que se **revirtió a propósito**. Quedaron archivos residuales que la app no usa:
`migrate_to_supabase.sql`, `part_*.sql` y similares.

→ No los leas para inferir la arquitectura. Describen un estado que ya no existe.
→ Si ves `TenantId`, RLS o Supabase en un `.sql`, es residuo, no diseño actual.
→ No los uses como referencia para cambios de esquema. El patrón vigente es
  PRAGMA + ALTER TABLE sobre SQLite.

---

## Prohibiciones

- No crear `tailwind.config.js` — el proyecto usa Tailwind v4, se configura en CSS.
- No commitear credenciales, connection strings ni tokens. Van en variables de entorno.
- No tocar `AGENTS.md` — es para WARP, no para vos.

---

## Cuando algo no está acá

Preguntá antes de asumir. Este archivo cubre las trampas conocidas, no todo el
proyecto. La estructura de carpetas, las dependencias y la arquitectura los
podés leer del `.sln`, `MauiProgram.cs` y el código.