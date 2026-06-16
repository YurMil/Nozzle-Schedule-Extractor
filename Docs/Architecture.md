# Nozzle Schedule Extractor Architecture

## Goals

The project is structured for incremental growth without changing the existing VVD parsing behavior. New report sources, parser variants, export formats, and UI surfaces should be added through small modules instead of edits across the whole codebase.

## Layers

```text
App/
  Program.cs                 CLI entry point
  ExtractionService.cs       Use-case orchestration
  Ports.cs                   Interfaces owned by the application layer

Domain/
  Model/                     Nozzle schedule model and table columns
  Parsing/                   Report text parsers and domain extraction rules

Infrastructure/
  Pdf/                       PDF text extraction adapters
  Reports/                   File-system report discovery
  Export/                    TSV, XLSX, and future output writers

Gui/
  MainForm.cs                WinForms shell
  SolidWorksTableInserter.cs SOLIDWORKS COM integration

Shared/
  TextUtil.cs                Cross-cutting text/format helpers

Tests/
  ParserRegressionTests.cs   Parser regression harness
  Fixtures/                  Extracted text samples
```

## Dependency Direction

```text
CLI / GUI
   |
   v
App use cases
   |
   v
App ports/interfaces  <---  Infrastructure adapters
   |
   v
Domain model and parser
```

The application layer owns the workflow and interfaces. Infrastructure implements file-system, PDF, and export details. Domain parsing is kept independent from UI and file writing.

## Extension Points

| Capability | Add/modify | Notes |
|---|---|---|
| New PDF/text extractor | Implement `IReportTextExtractor` in `Infrastructure/Pdf` | Keep Python/tool-specific code out of `ExtractionService`. |
| New report finder | Implement `IReportFinder` in `Infrastructure/Reports` | Useful for recursive folder search, database-backed jobs, or naming variants. |
| New parser | Implement `INozzleScheduleParser` in `Domain/Parsing` | Add regression fixtures before changing parser behavior. |
| New output format | Implement `INozzleScheduleWriter` in `Infrastructure/Export` | Register it in `ExtractionService.CreateDefault`. |
| New UI | Call `ExtractionService.CreateDefault(...).ExecuteFolder/ExecutePdf` | Avoid duplicating extraction logic in UI code. |

## Current Default Pipeline

`ExtractionService.CreateDefault` wires:

1. `WPatternReportFinder`
2. `PypdfReportTextExtractor`
3. `VvdNozzleParser`
4. `TsvScheduleWriter`
5. `XlsxScheduleWriter`

The legacy static methods `ExtractionService.RunFolder` and `ExtractionService.RunPdf` are kept as compatibility facades for the existing CLI and GUI.

## Development Rules

- Put workflow orchestration in `App`, not in UI or infrastructure.
- Put parsing rules in `Domain/Parsing`; test each behavior with a text fixture.
- Put external dependencies behind a port from `App/Ports.cs`.
- Add exporters as `INozzleScheduleWriter` implementations.
- Keep `Shared` small. If a helper becomes domain-specific, move it into `Domain`.

## Build Targets

- `build.ps1` builds the CLI executable.
- `build-gui.ps1` builds the WinForms/SOLIDWORKS executable.
- `build-tests.ps1` builds and runs parser regression tests.
