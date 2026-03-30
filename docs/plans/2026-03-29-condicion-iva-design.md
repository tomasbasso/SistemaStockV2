# Condición IVA en Clientes - Design

**Objetivo:** Registrar y mostrar la condición fiscal (IVA) de cada cliente en creación y edición, compatible con bases existentes.

**Enfoque:** Opción 1 (enum en `Cliente`):
- Enum `CondicionIva`: ResponsableInscripto, Monotributista, MonotributoSocial, Exento, ConsumidorFinal, NoResponsable, SujetoNoCategorizado.
- Propiedad nueva en `Cliente`: `CondicionIva CondicionIva { get; set; } = CondicionIva.ConsumidorFinal`.
- Mapeo EF: columna `TEXT`, default `'ConsumidorFinal'`.
- Migración manual: en `InitializeDatabaseAsync` agregar columna con `ALTER TABLE Clientes ADD COLUMN CondicionIva TEXT NOT NULL DEFAULT 'ConsumidorFinal';` si no existe.

**UI:**
- Formulario crear/editar cliente: `select` requerido con las 7 opciones.
- Listado y detalle de cliente: mostrar “Cond. IVA: <valor>”.

**Compatibilidad con datos existentes:** columna se agrega con default `ConsumidorFinal`, sin romper registros previos.

**Pruebas manuales mínimas:**
1) Crear cliente con cada opción; verificar visualización en card/detalle.
2) Editar cliente existente y cambiar condición; guardar y refrescar.
3) Abrir clientes previos (sin valor) y confirmar se muestran como Consumidor Final.
