# SKU Майстер Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver a runnable Windows app with configurable SKU reconciliation, previews, CSV/XLSX export, and GitHub release packaging.

**Architecture:** Core owns string tables and transformation rules. Infrastructure owns file adapters, settings and export. WPF desktop orchestrates background work with MVVM. Velopack supplies release installation and updates.

**Tech Stack:** .NET 10, WPF, CsvHelper, NPOI, Velopack, xUnit.

**Spec:** docs/superpowers/specs/2026-09-08-sku-status-design.md

## Global Constraints

- Windows 10/11 x64; Ukrainian UI; no application server or file uploads.
- Preserve rows, additional columns, text SKUs and actual-change counting.
- Input B/V headers off by default. Output CSV by default. User chooses destination.
- Source data is never overwritten. No release publication without user direction.

## Task 1: Core reconciliation and contracts

Files: src/SkuMaster.Core/{Models.cs,StatusOperation.cs}; tests/SkuMaster.Tests/StatusOperationTests.cs.

Interfaces: TableData(string[] Headers, IReadOnlyList<string[]> Rows, string Delimiter=";", string EncodingName="utf-8-bom"); RuleSettings; OperationResult; ITableOperation.Execute(TableData, IReadOnlySet<string>, RuleSettings, CancellationToken).

- [ ] Create project references and tests before implementation. Test union availability with literal rows `001/unavailable`, `002/other`, `003/empty`; expected statuses empty, other, unavailable. Assert source remains unchanged and counters are 1 restored, 1 unavailable, 1 unchanged.
- [ ] Run `.tools/dotnet/dotnet test tests/SkuMaster.Tests`; confirm missing-operation failure. Add public models and a temporary operation throwing NotImplementedException to obtain a behavioral red, then implement rule loop with ordinal SKU set, trim-only normalization, cloned rows and cancellation.
- [ ] Add and run cases for custom replacement, duplicate/empty SKUs, unchanged target statuses and ambiguous column headers. Expected counts use literal hand-derived values.

## Task 2: File adapters and settings

Files: src/SkuMaster.Infrastructure/{FileService.cs,SettingsStore.cs}; tests/SkuMaster.Tests/FileServiceTests.cs.

Interfaces: FileService.ReadSite(path,CsvInputSettings,CancellationToken) => TableData; ReadSource(path,SourceSettings,CancellationToken) => SourceData; GetSheets(path) => string[]; Export(table,path,ExportSettings,sourcePaths,CancellationToken); SettingsStore.Load() => (AppSettings Settings,string? Warning), Save(AppSettings).

- [ ] Write tests constructing actual CSV and NPOI XLS/XLSX fixtures. CSV `sku;status\r\n001;"a;b"` must yield `001` and `a;b`; explicit numeric Excel mask `0000` for 12 must yield `0012`.
- [ ] Run tests against throwing service stubs; implement strict parsing, validated row widths, ambiguity rejection, configurable encoding/delimiter, header skip, sheet selection, formula rejection and source warnings.
- [ ] Test actual export and reread with renamed headers, no header, extra columns, Unicode, protected source path and existing destination on encoding failure; implement adjacent temporary file replacement.
- [ ] Test persisted settings and corrupted JSON recovery; implement schema version validation and atomic save.

## Task 3: Desktop workflow

Files: src/SkuMaster.Desktop/{App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs,MainViewModel.cs,ObservableObject.cs,Program.cs}; tests/SkuMaster.Desktop.Tests/WorkflowTests.cs.

Interfaces: MainViewModel exposes AppSettings, file paths, IsBusy, HasResult, UnsavedResult, Summary, Changes, Warnings, AnalyzeAsync, SaveAsync, Invalidate. Consumes the exact Core and Infrastructure interfaces above.

- [ ] Create workflow tests proving edits invalidate a result, canceled work leaves no exportable stale state and successful save clears dirty state. Run red before implementation.
- [ ] Implement three labeled source selectors, settings tab with operation and export controls, progress/cancel, results cards, filterable virtualized grid, warnings and SaveFileDialog.
- [ ] Show Ukrainian errors for invalid files; guard close/restart with unsaved results. Run tests and WPF build, then a fixture-based smoke path.

## Task 4: Updates and distributable

Files: src/SkuMaster.Desktop/UpdateService.cs; scripts/{build.ps1,package.ps1}; .github/workflows/release.yml; README.md.

- [ ] Implement optional build-configured repository with Velopack GithubSource; default unset reports unavailable without network request. Check startup and manual; background download; apply only after explicit restart agreement and dirty/busy guard.
- [ ] Package self-contained win-x64 with vpk, configured release feed; workflow uses tag version and repository URL. Test source configuration validation locally; full update test requires published versions and is reported separately.
- [ ] Run all tests, Release build and self-contained publish. Smoke launch, exercise sample files and inspect rendered window. Review changes, fix findings and document exact remaining external prerequisites.

## Execution ledger

- Empty repository: no baseline code or commits. Work in the current project on codex/sku-master; no extra worktree required for this new project.
- SDK absent; installing local .NET 10 into ignored .tools.
- Spec reviewed for consistency; source formula detection uses NPOI cell metadata for XLS and XLSX.
- Task 1 complete: Core rules and workflow state tested red then green; 9 initial tests.
- Task 2 complete: delegated file/settings adapter implementation; 16 tests passed after behavioral red. Parent added 2 numeric SKU regression cases and fixed General formatting.
- Task 3 complete: native Ukrainian WPF workflow; 7 integration/UI/feed-validation cases pass. Metadata assignment occurs before releasing the busy state. UI screenshots visually inspected.
- Task 4 complete locally: build and package scripts plus tag-triggered GitHub workflow. Self-contained executable, setup and portable ZIP created. Live GitHub update verification and installer execution remain external verification prerequisites, explicitly documented in docs/verification.md.
- Final Release verification: 34 tests pass, zero compiler warnings/errors. Independent review and scoped re-review completed.
