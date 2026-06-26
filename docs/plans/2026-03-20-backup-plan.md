# Backup Google Drive Folder Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement hybrid manual/automatic backups of `stock.db` to a user-selected local folder (Drive-synced), with 15-file retention and 24h auto-check.

**Architecture:** Extend `BackupService` with a safe SQLite snapshot (`BackupDatabase`), preference persistence, and retention. Update `Configuracion.razor` to manage the folder, show last backup, and trigger runs. Fire-and-forget auto-check on app startup via `MainLayout`.

**Tech Stack:** .NET 8 MAUI Blazor Hybrid, `Microsoft.Data.Sqlite`, `CommunityToolkit.Maui.Storage`, `Microsoft.Maui.Storage.Preferences`.

---

### Task 1: Service logic (BackupService)

**Files:**
- Modify: `SistemaDeStockV3/Services/BackupService.cs`

**Step 1: Add preference keys and helpers**  
Add constants for `Backup.TargetFolder` and `Backup.LastRunUtc`. Add parsers for stored DateTime (use UTC).

**Step 2: Implement `ExecuteBackupToFolderAsync(targetFolder, isAutomatic)`**  
- Validate folder and db existence; early fail messages.  
- Clear SQLite pools + `GC.Collect()` to release handles.  
- Create temp path in `FileSystem.CacheDirectory`.  
- Use `SqliteConnection.BackupDatabase` from `_dbPath` to temp file.  
- Copy temp to `targetFolder` as `Backup_Stock_yyyyMMdd_HHmm.db`; delete temp.  
- Update `Preferences` (folder + last run UTC) on success.  
- Enforce retention: keep latest 15 files matching `Backup_Stock_*.db` (order by creation, delete rest).  
- Return `Result<string>` with success/fail details.

**Step 3: Implement `CheckAndRunAutoBackupAsync()`**  
- Read folder + last run from `Preferences`. If no folder, return fail.  
- If elapsed < 24h, return ok message (no-op).  
- Otherwise call `ExecuteBackupToFolderAsync(folder, true)`.  
- Handle exceptions and bubbles as `Result<string>`.

**Step 4: Quick build sanity**  
Run: `dotnet build "SistemaDeStockV3.sln"` (expect success).

### Task 2: UI for configuration page

**Files:**
- Modify: `SistemaDeStockV3/Components/Pages/Configuracion.razor`

**Step 1: Inject `BackupService` only; keep existing config fields**  
Remove old export/restore buttons; add backup section UI with:  
- Readonly textbox showing `selectedFolder`.  
- Button “Seleccionar Carpeta” using `FolderPicker` via `MainThread.InvokeOnMainThreadAsync`. Persist folder to `Preferences` and state.  
- Text “Último respaldo exitoso” showing stored timestamp or “Nunca”.  
- Button “Respaldar ahora” calling `ExecuteBackupToFolderAsync(folder, false)`; disabled if no folder.  
- Inline alerts for success/error.

**Step 2: Load initial state**  
On init, load folder + last backup from `Preferences` and show.

**Step 3: Build check**  
Run: `dotnet build "SistemaDeStockV3.sln"` (expect success).

### Task 3: Auto-trigger on startup

**Files:**
- Modify: `SistemaDeStockV3/Components/Layout/MainLayout.razor`

**Step 1: Inject `BackupService`**  
Fire-and-forget in `OnInitializedAsync`: `_ = Task.Run(() => Backup.CheckAndRunAutoBackupAsync());`.

**Step 2: Build check**  
Run: `dotnet build "SistemaDeStockV3.sln"` (expect success).

---

Plan complete and saved to `docs/plans/2026-03-20-backup-plan.md`. Execution options:
1. Subagent-driven here (new subagent per task, review in between).
2. Parallel session using superpowers:executing-plans.
