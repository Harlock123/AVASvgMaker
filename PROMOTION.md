# Promotion

Where this project has been announced, what was said, and the text to reuse. Kept because the
wording is reconstructed from scratch every release otherwise, and because the claims have to
stay consistent across places that are updated months apart.

Not linked from the README: this is material for whoever is doing the announcing, not for
someone trying to use the app.

## Claims that need to stay consistent

**Visio is deliberately asymmetric.** Say the app *reads* or *opens* `.vsdx`, and list writing
it among the exports. The importer is tested against genuine Visio files; what the exporter
writes has only ever been opened by our own importer. When someone confirms an exported file
opens in Visio proper, this and the repository description both want strengthening - the
README's "Not yet implemented" list carries the same note.

**Numbers that are checked, not rounded.** 96 stencils. Six builds: Windows, macOS and Linux,
x64 and arm64 each. MIT. Avalonia UI and .NET 9.

**Nothing to install** is worth saying plainly - a single self-contained executable, no
account, no telemetry, no network access at all. It is what most of the alternatives cannot
say.

## Done

| Where | What | When |
|---|---|---|
| GitHub repository | Description, homepage, 19 topics, social preview card | v1.11.0 |
| winget | Manifest written and validated - see [manifests/README.md](manifests/README.md) | v1.11.0 |

The winget manifest still has to be opened as a pull request against `microsoft/winget-pkgs`
by hand. Writing it and submitting it are different steps.

## Still to do

- **AlternativeTo** - text below, ready to paste
- **Awesome lists** - `awesome-dotnet`, `awesome-avalonia`, and any awesome-list for diagram
  tools. Each is a pull request adding one line, and each has its own house style to match
- **Reddit** - r/dotnet, r/csharp, r/opensource. Read each subreddit's self-promotion rule
  first; some want a flair, some want you to have posted before
- **Hacker News** - Show HN, if you want it. One shot, and Tuesday-to-Thursday mornings US
  time do better than weekends

## AlternativeTo

Submitted through "Suggest an app". Moderators check whether the submitter is the developer,
so say so - developer submissions are accepted, undeclared ones get pulled.

**Name**

    AVASvgMaker

**Homepage URL**

    https://github.com/Harlock123/AVASvgMaker

**Short description**

    An open source, offline diagram editor that opens Visio .vsdx files and exports SVG,
    PNG, PDF and Mermaid.

**Full description**

    AVASvgMaker is a desktop diagram editor for Windows, macOS and Linux. You drag shapes
    from a toolbox of 96 stencils onto a page, label them, and join them with connectors
    that route themselves around whatever is in the way.

    It opens Visio .vsdx drawings, and saves out as SVG, PNG, BMP, PDF, Mermaid and .vsdx.
    Its own .avadiag format holds the things an exported picture cannot. Pages behave like
    paper: several per document, each with its own size, margin, colour, watermark, header
    and footer. Shapes can be grouped, rotated, gradient-filled, carry data fields, and be
    arranged with smart guides, rulers, containers and swimlanes.

    It ships as a single self-contained executable - the runtime is inside the one file, so
    there is nothing to install and nothing to uninstall. It runs entirely offline, with no
    account, no telemetry and no network access of any kind.

    Built with Avalonia UI and .NET 9. MIT licensed.

**Platforms** - Windows, Mac, Linux (x64 and arm64 builds for each)

**Licensing model** - Open Source (MIT), free

**Tags**

    diagram  diagramming  flowchart  flowchart-maker  uml  bpmn  svg-editor
    vector-graphics  visio-alternative  offline  portable  no-account-required
    cross-platform  open-source  mermaid  swimlanes

`visio-alternative` is the one that earns its place. People search AlternativeTo for "Visio"
far more than for "diagram editor", and reading `.vsdx` is the thing almost none of the free
alternatives do. If anything gets trimmed, not that.

**Alternative to**, in rough order of how strongly it applies

    Microsoft Visio, draw.io (diagrams.net), Lucidchart, yEd Graph Editor,
    Dia, LibreOffice Draw, Pencil Project, OmniGraffle, SmartDraw

**Relation to the app** - Developer.

**Screenshots to upload** - `Images/overview.png`, `Images/swimlanes.png`,
`Images/mermaid-before-after.png`, `Images/visio-roundtrip.png`. Those four cover the
distinctive claims rather than repeating the same view.

## Announcement blurb

For an email, a forum post or a release note. Written for v1.11.0; the version number and the
last paragraph are the parts that go stale.

> **AVASvgMaker** is a Visio-style diagram editor I have been building - open source, MIT, and
> running on Windows, macOS and Linux.
>
> You drag shapes from a toolbox of 96 stencils onto a page, label them, and join them with
> connectors that route themselves around whatever is in the way. It opens Visio `.vsdx`
> drawings, and exports SVG, PNG, BMP, PDF and Mermaid. Pages behave like paper - several per
> document, each with its own size, margin, colour, watermark, header and footer - and shapes
> can be grouped, rotated, gradient-filled, given data fields, and arranged with smart guides,
> containers and swimlanes.
>
> There is nothing to install. Download the file for your machine, unpack it and run it: the
> runtime and everything else it needs are inside the one executable. It works entirely
> offline, with no account and no telemetry.
>
> - Download: https://github.com/Harlock123/AVASvgMaker/releases/latest
> - Source and README: https://github.com/Harlock123/AVASvgMaker
> - User guide: https://github.com/Harlock123/AVASvgMaker/blob/main/USERGUIDE.md
>
> On Linux and macOS the archive keeps the executable bit; if it goes missing on the way,
> `chmod +x AVASvgMaker`. macOS will want the file cleared from quarantine before it will open
> something downloaded from the web.
