# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers the features, file formats, project
layout and implementation notes in depth; this file is the short version of how to build, test and change it safely.

## What this is

A Visio-style diagram editor in C# on .NET 10 and Avalonia 11.2, with no MVVM framework: custom `Control`
subclasses draw themselves. It saves its own `.avadiag` format, exports SVG, PDF, PNG/JPEG/WebP/BMP, Mermaid and
`.vsdx`, and imports SVG, Visio drawings and Mermaid flowcharts. One self-contained executable per platform.

## Commands

```bash
dotnet build AVASvgMaker.sln                      # needs the .NET 10 SDK (no global.json)
dotnet test AVASvgMaker.Tests                     # headless (Avalonia.Headless): no window opens
dotnet run --project AVASvgMaker/AVASvgMaker.csproj
AVASVG_SHOOT=1 dotnet test AVASvgMaker.Tests --filter FullyQualifiedName~Screenshots   # refresh Images/ window shots
./build.sh [--version 1.2.0] [--targets "win-x64 osx-arm64"] [--mode aot]               # publish into dist/
./make-guide.sh                                   # rebuild USERGUIDE.pdf from USERGUIDE.md (Tools/GuideBuilder)
```

`nuget.config` adds `packages/` as a package source: it holds `SyntaxColorizer` 1.1.0 (the Mermaid dialog's
highlighter), which is not on nuget.org yet. Keep it until it is.

## Layout

- `AVASvgMaker/Models/`: the shape classes. Each knows its geometry, hit testing and its SVG element.
  - `DiagramShape.cs` is the base; `ConnectorShape.cs` handles glue, ports and end caps.
  - `StencilCatalogue.cs` lists the stencils; `StencilPath.cs` is the unit-square path language.
  - `Chip.cs`/`ChipShape.cs` and `Gate.cs`/`GateShape.cs` are the electronics stencils.
- `AVASvgMaker/Engine/`: document state, files and exporters, with no UI.
  - `DiagramDocument.cs` and `DiagramPage.cs` hold pages and shapes; `UndoStack.cs` keeps snapshots.
  - `DiagramFile.cs` reads and writes `.avadiag` (`CurrentVersion`).
  - `ConnectorRouter.cs` routes connectors around obstacles.
  - `Svg*`, `Pdf*`, `Raster*`, `Mermaid*` and `Visio*` files do import and export.
  - `Preferences.cs` and `StencilLibrary.cs` keep settings and saved shapes.
- `AVASvgMaker/Views/`: self-drawing controls and dialogs. `DrawingCanvas.cs` is the page and its tools.
- `AVASvgMaker.Tests/`: xUnit 2 tests driving the real window headlessly; `Harness.cs` has the helpers.
- `Tools/GuideBuilder/`: turns `USERGUIDE.md` into `USERGUIDE.pdf` with Skia.
- `manifests/`: the winget manifest submitted for each version (the package itself lives in microsoft/winget-pkgs).

## Rules that keep it working

- **Old files must keep opening.** Every change to `.avadiag` bumps `DiagramFile.CurrentVersion`, adds fields as
  optional, and keeps `FileFormatTests.EveryOlderVersionStillOpens` passing for every version.
- **Screen and export agree.** Text wrapping is done in `DiagramShape.WrapText`, so the line breaks drawn on screen
  are the ones written to SVG. Keep new drawing code and its exporters in step.
- **Tests never touch the user's files.** Settings and saved stencils live in `ApplicationData/AVASvgMaker`
  (`settings.json`, `stencils.json`). Tests point `StencilLibrary.Path` and `Preferences.Path` at a temporary folder
  and put them back afterwards.
- **Tests drive real input.** Drag tests press, move and release through the headless platform rather than
  calling the method a drag would call. Keep new interaction tests that way.
- **The test project is never published.** `build.sh` and the workflow publish `AVASvgMaker/AVASvgMaker.csproj`
  by name. AOT builds (`--mode aot`) are not single files and cannot cross operating systems.

## Making a change

- Add or update tests with each change. `dotnet test AVASvgMaker.Tests` must pass.
- Update `README.md` and `USERGUIDE.md` to match; regenerate `USERGUIDE.pdf` with `./make-guide.sh`.
- If the window changes, refresh the screenshots with the command above. The close-ups in `Images/` are taken by
  hand.

## CI and releases

`.github/workflows/release.yml` runs only on a `v*` tag or a manual run, not on every push.

- It runs the tests, then `./build.sh` publishes win-x64/arm64, linux-x64/arm64 and osx-x64/arm64.
- It also builds the user guide PDF for that version.
- On a tag it makes a draft release and publishes it only once every file is attached.
- A manual run can also build ahead-of-time native binaries on a runner of each OS; these are not released.
