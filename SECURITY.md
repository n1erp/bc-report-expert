# Security policy

## Supported versions

**rex-2.0-baboon** is the current release. Security fixes target the default branch.

## Reporting a vulnerability

Do not open a public issue for a sensitive report.

Email **mehditaher01@outlook.com** with:

- What is wrong
- Steps to reproduce
- Impact, if you know it
- Whether you want a coordinated disclosure timeline

## Known considerations

- Copilot API keys are encrypted with Windows DPAPI for the current user (`dpapi:` values in `%AppData%\ReportExpert\copilot.json`). A copy of that file on another machine or user account does not reveal the key.
- Preview uses `ReportSafetyPolicy.Trusted`, so a layout's `<Code>` block runs, as it does in Business Central. Open layouts you trust.
- Do not commit API keys, `.env` files, or customer report data.
