# Dashboard Rebranding Visual Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Aplicar al dashboard una estética visual fiel a la referencia aprobada, con predominio violeta sobre superficies claras, y dejar los tokens globales listos para extender el cambio al resto del sistema.

**Architecture:** La implementación se apoya en un cambio de theme global en `app.css` y una recomposición controlada de `MainLayout`, `NavMenu` y `Home`. La lógica del dashboard no cambia; solo cambia la presentación y la jerarquía visual.

**Tech Stack:** .NET 8 MAUI Blazor Hybrid, Razor Components, Tailwind CSS v4, xUnit.

---

### Task 1: Documentación y guardarraíl mínimo

**Files:**
- Create: `docs/plans/2026-04-14-dashboard-rebranding-design.md`
- Create: `docs/plans/2026-04-14-dashboard-rebranding-plan.md`
- Create: `Sistema de Stock.Tests/DashboardVisualContractTests.cs`

**Step 1:** Escribir una prueba que lea `Sistema de Stock/wwwroot/css/app.css` y verifique la presencia de los tokens violeta, azul, naranja y superficies claras.  
**Step 2:** Escribir una prueba que lea `Sistema de Stock/Components/Pages/Home.razor` y verifique la nueva estructura visual del dashboard.  
**Step 3:** Ejecutar solo esas pruebas para confirmar que fallan antes de implementar.  

### Task 2: Theme global y layout

**Files:**
- Modify: `Sistema de Stock/wwwroot/css/app.css`
- Modify: `Sistema de Stock/Components/Layout/MainLayout.razor`
- Modify: `Sistema de Stock/Components/Layout/NavMenu.razor`

**Step 1:** Reemplazar la paleta clara verdosa actual por tokens basados en la referencia: violeta dominante, azul de apoyo, naranja comercial, rojo suave y neutros perlados.  
**Step 2:** Redefinir `glass-panel`, `premium-card`, tipografía, sombras y bordes para lograr el acabado brillante de la referencia.  
**Step 3:** Ajustar el layout principal y el menú lateral para que el estado activo y el header respondan a la nueva identidad.  

### Task 3: Dashboard

**Files:**
- Modify: `Sistema de Stock/Components/Pages/Home.razor`

**Step 1:** Rediseñar el bloque superior con hero, buscador visual y KPIs en cards claras.  
**Step 2:** Reordenar los paneles de rotación, alertas y actividad reciente con mayor profundidad visual y mejor jerarquía.  
**Step 3:** Mantener intacta la lógica de carga y datos.  

### Task 4: Verificación

**Files:** (no code)

**Step 1:** Ejecutar `dotnet test "Sistema de Stock.Tests/Sistema de Stock.Tests.csproj"` y confirmar verde.  
**Step 2:** Ejecutar `dotnet build "Sistema de Stock.sln"` y confirmar compilación exitosa.  
**Step 3:** Revisar el diff final para asegurar que no se pisaron cambios funcionales ajenos.
