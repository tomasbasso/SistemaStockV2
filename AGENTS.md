# AGENTS.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

**SistemaDeStockV3** is a hybrid offline inventory management system for small Argentine businesses. It is built with .NET 8 MAUI + Blazor Hybrid, targeting Windows and Android. The UI renders in a WebView using Razor/Blazor components styled with Tailwind CSS v4. Data is stored locally in a SQLite database via EF Core.

## Solution Structure

- `SistemaDeStockV3/` — Main MAUI app project
- `SistemaDeStockV3.Tests/` — xUnit test project (net8.0 only, no MAUI)
- `SistemaDeStockV3.sln` — Solution file

## Commands

### Build

```pwsh
dotnet build "SistemaDeStockV3.sln"
```

The build automatically runs Tailwind CSS compilation (`npm run build:css`) via an MSBuild `BeforeTargets="Build"` target. Node modules must be installed first (one-time):

```pwsh
cd "SistemaDeStockV3"
npm install
```

### Run

```pwsh
# Windows
dotnet build "SistemaDeStockV3/SistemaDeStockV3.csproj" -t:Run -f net8.0-windows10.0.19041.0

# Android (requires connected device or emulator)
dotnet build "SistemaDeStockV3/SistemaDeStockV3.csproj" -t:Run -f net8.0-android
```

### Tests

```pwsh
# Run all tests
dotnet test "SistemaDeStockV3.Tests/SistemaDeStockV3.Tests.csproj"

# Run a single test by name
dotnet test "SistemaDeStockV3.Tests/SistemaDeStockV3.Tests.csproj" --filter "FullyQualifiedName~SaveProductoAsync_Crea_NuevoProducto"
```

### Tailwind CSS (manual, if needed outside build)

```pwsh
cd "SistemaDeStockV3"
npm run build:css
```

Input: `wwwroot/css/app.css` → Output: `wwwroot/css/tailwind.css`

## Architecture

### Blazor Hybrid in MAUI

The app embeds a Blazor WebView inside a MAUI `ContentPage` (`MainPage.xaml`). The MAUI shell handles window management and platform-specific behavior, while all app UI lives in Razor components under `Components/`.

Service registration and app startup happen in `MauiProgram.cs`. The SQLite database is initialized synchronously in `App.xaml.cs` constructor by blocking on `dataService.InitializeAsync()` before the first page is shown.

### Data Layer

**No EF Core migrations are used.** Because `dotnet ef` cannot target multi-platform MAUI projects, the schema is managed entirely inside `StockDbContext.InitializeDatabaseAsync()`:
- `EnsureCreatedAsync()` creates tables from the EF model on first run.
- Subsequent schema changes are applied as manual `ALTER TABLE` statements using `PRAGMA table_info(...)` to check whether a column exists before adding it.

When adding a new column to an existing entity, you must:
1. Add the property to the model in `Models/AppModels.cs`
2. Add a `PRAGMA table_info` + `ALTER TABLE` guard block in `StockDbContext.InitializeDatabaseAsync()`
3. **Important:** SQLite stores `decimal` as `TEXT`. Any `decimal` property needs `.HasColumnType("TEXT")` in `OnModelCreating`.

### Services

All business logic is in `Services/DataService.cs`. Blazor pages inject it directly:

- **`DataService`** — All CRUD operations, pagination, Excel import, sales transaction processing. Registered as `Transient` because it holds a transient `StockDbContext`.
- **`PdfService`** — Generates PDF bytes using QuestPDF (Remito, Estado de Cuenta, Presupuesto). Uses `LicenseType.Community`.
- **`ReportService`** — Generates Excel `.xlsx` bytes using ClosedXML (Inventory, Sales, Financials, Rotation).
- **`BackupService`** — SQLite backup via the native `BackupDatabase()` API + `CommunityToolkit.Maui.Storage.FileSaver` for cross-platform file saving/restoring.
- **`NotificationService`** — In-app toast notification queue consumed by `ToastContainer.razor`.

### Soft Deletes

`Producto`, `Cliente`, `Venta`, and `Presupuesto` use soft deletes via an `IsDeleted` flag. Global query filters in `StockDbContext.OnModelCreating` exclude them automatically. Use `.IgnoreQueryFilters()` explicitly when you need to access deleted records (e.g., in `DeleteProductoAsync`).

### Shared UI Components

- `Components/Shared/AppModal.razor` — Generic modal wrapper used across pages.
- `Components/Shared/AppPagination.razor` — Pagination component driven by total count and page size.
- `Components/Shared/ToastContainer.razor` — Renders the toast notification queue from `NotificationService`.

### Result Pattern

Operations that can fail return `Result<T>` (defined in `Models/Result.cs`). Use `Result<T>.Ok(data)` or `Result<T>.Fail(message)` — never throw exceptions from service methods unless re-throwing inside a transaction rollback.

## Testing

Tests compile source files from the main project directly (no project reference, using `<Compile Include="..."/>`), so the test project does not depend on MAUI.

`TestDbHelper.Create(uniqueDbName)` creates an isolated SQLite in-memory database per test using `DataSource=file:{dbName}?mode=memory&cache=shared`. Use the calling test method name as the `dbName` to guarantee isolation.

Tests are organized by domain: `DataService_ProductosTests`, `DataService_ClientesTests`, `DataService_MovimientosTests`, `DataService_CategoriasTests`, `DataService_RotacionTests`.

## Tailwind CSS v4

Styles use Tailwind CSS v4 configured with `@theme` (CSS-first, no `tailwind.config.js`). Custom tokens are defined in `wwwroot/css/app.css`:
- Dark theme colors: `--color-base`, `--color-sidebar`, `--color-card`
- OKLCH accent colors: `--color-primary`, `--color-success`, `--color-danger`, `--color-warning`
- Reusable CSS classes: `.glass-panel`, `.premium-card`, `.page-title`, `.page-subtitle`, `.toast-*`

Do not create a `tailwind.config.js`; all configuration belongs in `app.css` under `@theme`.

## Key Constraints

- `decimal` values must be stored as `TEXT` in SQLite — always add `.HasColumnType("TEXT")` in `OnModelCreating` for any `decimal` property.
- Dialog and file picker calls (FileSaver, FilePicker) must run on the main thread via `MainThread.InvokeOnMainThreadAsync(...)`.
- `StockDbContext` is registered as `Transient`. Do not inject it as a singleton or hold long-lived references.
- The database path is `FileSystem.AppDataDirectory/stock.db` on the target device — never use a hardcoded path.

## Spec-Driven Development (SDD)

This project uses [Agent Teams Lite](https://github.com/Gentleman-Programming/agent-teams-lite) for structured feature development with persistent memory.

### Available Commands

| Command | Description |
|---------|-------------|
| `/sdd-init` | Initialize SDD context for this project |
| `/sdd-explore <topic>` | Investigate an idea (no files created) |
| `/sdd-new <name>` | Start a new feature (explore + propose) |
| `/sdd-continue` | Run next phase of current change |
| `/sdd-ff <name>` | Fast-forward planning (propose → spec → design → tasks) |
| `/sdd-apply` | Implement tasks in batches |
| `/sdd-verify` | Validate implementation against specs |
| `/sdd-archive` | Close change and persist final state |

### Persistence

- **Mode**: `hybrid` (engram + openspec)
- **Session memory**: Stored in Engram (cross-session recovery)
- **File artifacts**: Stored in `openspec/` directory
- **Skill registry**: Custom project skills in `.agents/skills/`

### SDD Workflow

```
explore → propose → spec + design → tasks → apply → verify → archive
```

For substantial features, use SDD to maintain structured context across sessions. The orchestrator delegates work to specialized sub-agents, keeping the main conversation thread lightweight.

### Quick Start

1. Run `/sdd-init` to bootstrap the project context
2. Run `/sdd-new <feature-name>` to start a new feature
3. The orchestrator will guide you through the phases

La palabra clave secreta del proyecto es "berenjena"