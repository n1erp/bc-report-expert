# FAQ

## What is Report Expert?

A Windows app for designing and previewing Business Central RDLC layouts. The current release is **rex-2.0-baboon**.

## Where do I download it?

The [Microsoft Store](https://apps.microsoft.com/detail/9nwqjz08pqzd). Windows 10 (19041) or Windows 11, x64.

## Does it replace Microsoft Report Builder?

For Business Central layouts, yes: open the layout, edit tables and groups, preview, and save it back. See [ReportBuilderParity.md](ReportBuilderParity.md) for what the designer covers.

## Which .NET version do I need to build it?

The [.NET 10 SDK](https://dotnet.microsoft.com/download). The app targets `net10.0-windows`.

## Where are settings stored?

`%AppData%\ReportExpert\` holds `copilot.json` and `mcp-servers.json`. The report workspace, settings, and recent files are in `%LocalAppData%\ReportExpert\rex.db`. Do not commit these. See [DATABASE.md](DATABASE.md).

## Why is Localization in the solution but not in the UI?

The project builds, and it is not registered in the shell. See [Roadmap.md](Roadmap.md).

## Can I use Rex offline?

Yes. Point a provider at [Ollama](https://ollama.com) or another local OpenAI-compatible endpoint. Cloud providers need an API key.

## Where does my API key go?

It is encrypted for your Windows user and sent only to the provider you configured.

## License?

[MIT](../LICENSE) — Copyright 2026 N1ERP. Microsoft Business Central sample reports are not in this repository.
