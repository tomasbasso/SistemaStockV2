# Dashboard Rebranding Visual - Design

**Objetivo:** Reemplazar la paleta clara verdosa actual por una estética fiel a la referencia aprobada: vidrio blanco, acento violeta dominante y apoyos azul y naranja, comenzando por la pestaña Dashboard.

**Dirección visual aprobada:** Opción 2, "copia literal + adaptación stock". La interfaz debe conservar la información de ventas, deuda, inventario, alertas y actividad del sistema, pero con la atmósfera de la imagen de referencia.

**Paleta base:**
- Violeta principal para foco, estado activo, bordes destacados y gráficas.
- Azul brillante para métricas secundarias, líneas de apoyo y acentos informativos.
- Naranja suave para variaciones comerciales, avisos y una parte de las cards KPI.
- Rojo suave para alertas negativas.
- Blancos fríos y grises perlados para fondos, superficies y vidrio.

**Aplicación inicial:**
- `MainLayout.razor`: fondo perla con halos violetas/azules, header superior más limpio y brillante.
- `NavMenu.razor`: sidebar glass claro con activo violeta saturado similar a la referencia.
- `Home.razor`: tarjetas KPI, paneles principales, alertas y actividad reciente redibujados con bordes suaves, sombras difusas y jerarquía más cercana a la imagen.
- `wwwroot/css/app.css`: tokens globales para empezar a heredar la nueva identidad en las demás pestañas.

**Restricciones:**
- No tocar todavía el resto de las páginas funcionales más allá de la herencia natural del theme.
- Mantener todos los datos actuales del dashboard.
- Evitar una copia exacta del layout de SaaS genérico; adaptar la composición a stock, ventas y alertas existentes.

**Verificación mínima:**
1. Prueba automatizada de contrato sobre tokens de color y clases esperadas del dashboard.
2. `dotnet test` del proyecto de pruebas.
3. `dotnet build` de la solución para asegurar que la app sigue compilando.
