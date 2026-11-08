# Report Expert

**Rex – Version 2: Baboon** — Windows desktop designer for RDLC layouts.

<img src="Assets/v2.0%20Baboon/logo.png" alt="Report Expert logo" width="240">

Open an RDLC layout, change its tables and groups, preview it, and save it.

## Download

[Microsoft Store](https://apps.microsoft.com/detail/9nwqjz08pqzd)


## Build

[.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```bash
git clone https://github.com/n1erp/bc-report-expert.git
cd bc-report-expert
dotnet build ReportExpert.slnx
dotnet run --project src/ReportExpert.App
```

Tests: `dotnet test`. The WPF test projects need Windows.

[Release notes](docs/v2.0-Baboon.md) · [Docs](docs/README.md) · [Contributing](CONTRIBUTING.md)

## License

[MIT](LICENSE) — Copyright 2026 N1ERP
