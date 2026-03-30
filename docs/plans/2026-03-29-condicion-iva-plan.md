# Condición IVA en Clientes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Agregar condición fiscal (IVA) al cliente, visible en alta/edición y compatible con la base existente.

**Architecture:** Propiedad enum en `Cliente` mapeada a `TEXT` con default `ConsumidorFinal`; `InitializeDatabaseAsync` añade la columna si falta; formularios Blazor muestran un `<select>` requerido y tarjetas/detalle exhiben el valor.

**Tech Stack:** .NET 8 MAUI Blazor Hybrid, EF Core SQLite, Tailwind UI, CommunityToolkit.Maui.

---

### Task 1: Modelo y schema

**Files:**
- Modify: `Sistema de Stock/Models/AppModels.cs`
- Modify: `Sistema de Stock/Data/StockDbContext.cs`

**Step 1:** Añadir enum `CondicionIva` con 7 valores (ResponsableInscripto, Monotributista, MonotributoSocial, Exento, ConsumidorFinal, NoResponsable, SujetoNoCategorizado).  
**Step 2:** Agregar propiedad `CondicionIva CondicionIva { get; set; } = CondicionIva.ConsumidorFinal;` en `Cliente`.  
**Step 3:** En `OnModelCreating`, mapear `CondicionIva` como `TEXT` y default `ConsumidorFinal`.  
**Step 4:** En `InitializeDatabaseAsync`, `PRAGMA table_info(Clientes)` y si no existe columna `CondicionIva`, ejecutar `ALTER TABLE Clientes ADD COLUMN CondicionIva TEXT NOT NULL DEFAULT 'ConsumidorFinal';`.  
**Step 5:** Build rápido: `dotnet build "Sistema de Stock.sln"`.

### Task 2: UI crear/editar cliente

**Files:**
- Modify: `Sistema de Stock/Components/Pages/Clientes.razor`

**Step 1:** En el formulario modal, añadir `<select>` (InputSelect) enlazado a `editingItem.CondicionIva`, requerido, con las 7 opciones legibles.  
**Step 2:** En `OpenEdit` copiar también `CondicionIva` al modelo de edición; en `OpenNew` inicializar default `ConsumidorFinal`.  
**Step 3:** Mostrar el valor en la tarjeta/listado (badge o texto “Cond. IVA: ...”) y en el modal de detalle si aplica.  
**Step 4:** Build: `dotnet build "Sistema de Stock.sln"`.

### Task 3: Verificación manual

**Files:** (no code)  

**Step 1:** Crear cliente con cada condición y guardar; verificar en lista/detalle.  
**Step 2:** Editar cliente existente y cambiar condición; reabrir y confirmar persistencia.  
**Step 3:** Abrir clientes previos (pre-columna) y confirmar que aparecen como “Consumidor Final”.  
**Step 4:** Rebuild final si hubo cambios: `dotnet build "Sistema de Stock.sln"`.
