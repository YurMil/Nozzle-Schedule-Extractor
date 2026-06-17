# Nozzle Schedule Extractor

Small C# extractor for VVD / Visual Vessel Design style PDF reports.

## Structure

See `Docs/Architecture.md` for the module map and extension points.

## Source Priority

1. `Bill of Materials` is the primary source for nozzle identity:
   - flange component gives `NOZZLE TYPE`, `PRESSURE CLASS`, and flange `STANDARD`;
   - nozzle neck component gives service/name, nominal size, `D x t`, material number;
   - reinforcement ring component gives ring service, ring geometry, material;
   - reinforcement pad is used only as fallback and must not overwrite the nozzle neck material.
2. `Nozzle List`, `MAWP`, and `Test Pressure` tables are fallback sources for missing description, size, flange type, class, and standard.
3. `Nozzle Loads` / `Table NOZZLE LOADS` provide loads only. VVD local notation is mapped to:
   - `Fl` -> `Fx`
   - `Fc` -> `Fy`
   - `Fz` -> `Fz`
   - `Mx` -> `Mx`
   - `My` -> `My`
   - `Mt` -> `Mz`
4. `DATA FOR NOZZLE` detail pages fill missing geometry, material, and loads.

## Python dependencies

PDF text extraction shells out to Python. Install the packages once:

```powershell
pip install -r requirements.txt
```

`pdfplumber` is the primary, layout-aware extractor (it reconstructs table rows
from word coordinates); `pypdf` is the automatic fallback if `pdfplumber` is
unavailable or yields no text.

## Build

```powershell
.\src\NozzleScheduleExtractor\build.ps1
.\src\NozzleScheduleExtractor\build-gui.ps1
```

Regression tests:

```powershell
.\src\NozzleScheduleExtractor\build-tests.ps1
```

## CI/CD

- `CI` builds the console extractor and runs parser regression tests on every push and pull request.
- `Release` packages the console extractor on `v*` tags or manual workflow dispatch.
- `GUI Build` is manual and expects a self-hosted Windows runner with SOLIDWORKS installed.

## Run

Console:

```powershell
.\src\NozzleScheduleExtractor\bin\NozzleScheduleExtractor.exe "C:\wt\Nozzle List\Test folder 01" W2402601
```

GUI:

```powershell
.\src\NozzleScheduleExtractor\bin\NozzleScheduleExtractor.Gui.exe
```

The program picks the newest `W#######*.pdf` matching the optional prefix and writes
`*_nozzle_schedule.xlsx` plus `*_nozzle_schedule.tsv` next to the PDF. It also writes
`*_nozzle_review.tsv` listing validation findings (bad geometry, DN/OD mismatch,
source conflicts, missing fields) and a confidence level per nozzle. In the Excel
file, cells with a validation warning are highlighted.

In the GUI, use `Run Folder` to auto-pick the newest matching report in a folder,
or `Run PDF` to parse the selected PDF directly. The lower table previews the
same rows that are written to Excel, and the log panel shows each processing step.

Use `Insert SW` after extraction to insert the current preview as a general table
into the active SOLIDWORKS drawing document. SOLIDWORKS must already be running
with a `.SLDDRW` document active. The command does not save the drawing.
