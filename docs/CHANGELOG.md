# Changelog

All notable changes to Report Expert (REX) are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow
[Semantic Versioning](https://semver.org/). Every release has a codename; the version and codename come
from `Directory.Build.props`.

Release notes: [v2.0-Baboon.md](v2.0-Baboon.md).

## [2.0.0] "Baboon" — 2026-11-07

The second public release, **rex-2.0-baboon**. Baboon turns REX from an RDLC previewer with a minimal layout
editor into a full RDLC designer for Business Central: a report engine with revisions, a visual designer with
tablix grouping, SQLite storage, a much larger MCP server, multi-provider Rex, and an error-reporting path
to GitHub.

### Added

#### Report design engine (`ReportExpert.Reporting.Design`)
- `IReportDesignService` (`services.AddReportDesign()`): open, edit, validate, render and save RDLC layouts,
  every change stored as a revision with undo/redo and an audit entry.
- Layout, table, chart (27 types), filter, image, parameter, dataset and property editors, plus
  `ReportItemOperations` for align, size, centre, spacing and order on one or many items.
- Linked files: `OpenFileAsync`, `SaveToFileAsync` (atomic write, previous file kept as `.bak`),
  `AttachDataAsync`. A file changed outside REX becomes a new revision when it is reopened.
- `ReportSafetyPolicy`: **Trusted** (desktop default — keeps `<Code>`, subreports and query text) and
  **Strict** (embedded data sources only, no custom code).
- Business Central data provider: rows from an attached BC XML (`DataItems` format, flattened as the BC
  runtime does), JSON or CSV file, a same-named file next to the RDLC, or generated sample data.
- Field type inference for untyped BC fields from how the layout uses them (aggregates, conversions —
  including `Convert.ToInt32`, `Integer.Parse`, `CBool`, `Convert.ToBoolean` — comparisons, Hidden
  expressions, images), so sample data has the right types.
- Page setup model (`DesignPageSetup`), blank-page detection (body wider than page minus margins) and
  **Fit body to page**.
- Batch property changes as one revision (`SetPropertiesAsync`).

#### Tablix structure (Report Builder parity)
- `TablixStructure`: parent, child and adjacent row/column groups; group header/footer rows and row-header
  columns (or headerless groups); totals before/after any group; delete a group only or with its rows;
  insert rows/columns inside or outside a group; delete rows/columns; merge and split cells.
- Group properties: group expressions, sorting, filters, visibility and drilldown toggle, page breaks, page
  name, variables; static member options (`KeepWithGroup`, `RepeatOnNewPage`, `KeepTogether`, hidden,
  toggle, hide-if-no-rows).
- Every structural change keeps the tablix valid for the report processor (header levels, corner,
  `KeepWithGroup` runs, spans) and is one revision. Covered by a seeded fuzz test that renders after every
  random command.

#### Report center and visual designer
- Report center with report list, search and one-click import of the workspace's RDLC layouts; Data,
  Parameters, Layout, Images, History and Checks panels.
- **Design** surface: rulers in the report's units (cm with mm ticks, or inches), ⅛-inch grid, header/body/
  footer bands with resizable edges, drag to move, handle to resize, 1/16-inch snapping, snaplines (Alt moves
  freely), keyboard nudge and resize, multi-select.
- Toolbox (text box, image, line, rectangle, table, list, matrix, 27 charts, bands) with search; drag or click
  to add. **Report data** pane: fields, parameters and built-in fields, drag or double-click to insert.
- **Row Groups / Column Groups** pane with right-click commands; tablix drawn with corner, header cells and
  merged cells; tablix cell menu; Tablix group, Delete group and Group properties dialogs.
- **Format toolbar**: font, size, bold/italic/underline, colour, horizontal and vertical alignment, number
  format — applied to the whole selection as one revision.
- **Properties pane**: categorized or A–Z, search, an fx editor on every expression property; only
  properties and arrange operations that apply to the selection are shown (no "That change does not apply"
  errors), and nothing already on the format toolbar is repeated.
- **Expression editor** with fields, parameters, built-in fields, operators and functions (double-click or F2
  on a text box).
- **Page setup** dialog with live preview (paper, orientation, margins, columns).
- **Placeholders** view (`[Field]`, `[@Parameter]`, `[&Global]`, `«Expr»`) and **Hidden items** toggle.
- The designer draws the formatting that renders: font family, italic, underline/strikeout, text colour,
  background colour, vertical alignment and borders, on text boxes, rectangles and tablix cells.
- Items nested in rectangles and tablix cells (Business Central list layouts) are drawn where they render,
  and moving them keeps their position relative to the container.
- Tablix cell text uses the text box's own font size and fits its row.

#### Preview and export
- Preview window and preview side panel rendering PDF with the chosen parameter values (WebView2), F5 to
  render again, parameter values in the Parameters tooltip.
- One **Export** menu: PDF ("print-ready"), Excel workbook and Word document.
- Sample image for picture fields: a neutral 240×180 "Sample image" placeholder.
- Settings › Preview › **Sample data**: quick picks (10, 25, 50, 100, 250) or any value from 1 to 500,
  applied to the designer preview and the RDLC preview.

#### Rex (Copilot)
- Several AI providers at once — Groq, Google Gemini, Mistral, OpenRouter, Anthropic Claude, OpenAI, Azure
  OpenAI and local Ollama — each with its own key and model; Rex uses the active one.
- Current model catalogues per provider, best default first (for example `gemini-3.5-flash`,
  `claude-sonnet-5-5`, `gpt-6.1-sol`, `openai/gpt-oss-120b`, `mistral-large-latest`).
- Settings › **Test** checks the key, finds a model that answers (switching from a retired, busy or
  paid-only model) and loads the models the key can use into the model list.
- Two-step model picker in the composer: providers on the left, models on the right, search across all
  providers, "Current" and "Saved for provider" tags, set-up shortcut for providers without a working key.
- Agent mode, tool approval, chat history per report, attachments (images, PDF text) and dictation.

#### MCP server (`rdlc-mcp`)
- 48 tools (13 read-only), up from 31: design-model tools (`get_rdl_design_model`, `get_rdl_data_preview`,
  `place_rdl_report_item`, `move_rdl_report_item`, `set_rdl_item_property`, `arrange_rdl_report_items`,
  `add_rdl_group`, `add_rdl_total_row`, `export_rdl_report`) and eight `*_rdl_tablix_*` structure tools over
  the same engine as the designer. Every write offers `dry_run` and keeps a backup.

#### Storage
- One SQLite database, `%LocalAppData%\ReportExpert\rex.db`, for settings, recent items and the report
  workspace (reports, revisions, audit log, clipboard, attached data files). WAL mode, so the app and the
  MCP server can share it. See [docs/DATABASE.md](docs/DATABASE.md).
- First start imports `settings.json`, `recent.json`, `recent-projects.json` and older workspace folders.

#### Error reporting and open source
- Every unexpected error, and every preview or export failure, opens an error dialog with the message,
  technical details (versions, operation, exception chain with stack traces), **Copy details** and
  **Report a bug** (a prefilled GitHub issue). Personal paths and API keys are removed from the details.
- When the report engine names a failing expression, the dialog quotes it and lists the fields it reads,
  their type and how many rows are empty, with a hint for the usual cause.
- GitHub issue forms: bug report, feature request, RDLC compatibility, question, documentation.
- Settings › **Open source** card linking to the repository, issues and new-issue forms.

#### Look and feel
- Navy app bar and navigation drawer, SafiPay-derived palette for light and dark, and four contrast themes:
  Aquatic, Desert, Dusk and Night Sky.

### Changed
- Version is 2.0.0, shown as **rex-2.0-baboon** (after 1.0 "Marmoset"). The installer, MSIX manifest,
  MCP package and Settings use that version.
- Repository links point to `github.com/n1erp/bc-report-expert` (README, MCP package metadata, MCP registry
  name `io.github.n1erp/rdlc-mcp`).
- The default sample row count follows Settings in the designer preview (it was fixed at 25).
- The designer toolbar is icon-only for Page setup, Placeholders and Hidden items (tooltips explain them);
  the "RDLC" tag next to the file name is gone.
- The preview toolbar keeps Parameters and Export only; Refresh moved to F5.
- Rex's footer reads "REX can make mistakes."
- The Data and Parameters tabs of the report center are hidden for this release (their functions are in the
  Report data pane and the Properties pane).
- Settings no longer has a Default export folder; save dialogs open in the folder last exported to.

### Fixed
- Testing a Gemini key failed with "The requested operation requires an element of type 'Object', but the
  target element has type 'Array'": provider error bodies in array form are now read correctly, and
  retired models (for example `gemini-2.5-flash` for new users) are replaced automatically.
- Rex chat no longer fails on reasoning models that reject `temperature`; it retries without it.
- Sample rows ignored the Settings value in the designer preview.
- Text colour and other formatting set from the toolbar did not show in the designer (only in the PDF).
- Items inside rectangles and list cells were drawn at the top-left of the body, over each other.
- Tablix cell text was cut off in thin Business Central rows.
- The sample picture was a semi-transparent blue pixel stretched over the image area.
- Arrange operations that do not apply to the selection raised "That change does not apply to the selected
  item".
- Backup file names could collide within the same millisecond (MCP server).
- The validator reported group scopes in aggregates as unknown datasets.

### Security
- AI provider API keys are encrypted at rest with Windows DPAPI for the current user (`dpapi:` values in
  `copilot.json`); plain-text keys from 1.0 are encrypted on first load.
- `System.Security.Cryptography.Pkcs` and `.Xml` are pinned to 10.0.13 in the preview module (ReportViewer
  pulled 10.0.0, which has published advisories).
- Error reports mask Groq (`gsk_…`), Google (`AIza…`), OpenAI/Anthropic (`sk-…`) and GitHub tokens.

### Removed
- The "Open source?" button in the app bar (the Settings card replaces it).

### Known issues
- PDF rendering and most WPF tests need Windows (ReportViewer measures text with GDI+).
- Three `ReportExpert.Mcp.Client.Tests` cases use Windows paths and `%VAR%` expansion and fail on Linux.
- Colours set by an expression, and black text, are drawn in the theme's text colour in the designer; very
  dark colours can be hard to read on dark themes.
- Projects not referenced by the app (`Application`, `Infrastructure`, `Plugins`, `Shared`,
  `Modules.Localization`, `Reporting.DummyData/Expressions/Parser/Renderer`, `RdlcDesigner`) are kept as
  scaffolding; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#live-projects-and-scaffolding).

## [1.0.0] "Marmoset"

The first public release.

### Added
- Workspace: open an AL project folder; browse RDLC layouts, AL source, Excel and Word layouts.
- RDLC preview: sample data or Business Central XML, PDF in an embedded viewer.
- Minimal clean-room RDLC layout designer (toolbox, data and parameters panes, expressions, lines and images).
- AL source viewer with TextMate highlighting.
- Copilot (Rex) that edits reports through approved tool calls, and the `rdlc-mcp` server (31 tools).
- Settings: theme, preview defaults, Copilot provider.
- Per-user installer, portable ZIP and MSIX package.

[2.0.0]: https://apps.microsoft.com/detail/9nwqjz08pqzd
[1.0.0]: https://github.com/n1erp/bc-report-expert
