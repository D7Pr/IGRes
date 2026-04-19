# IGRes

IGRes is a cross-platform desktop app for reviewing Instagram account activity and running safe bulk cleanup flows with clear capability boundaries, background job tracking, and local-only session storage.

## Highlights

- Review saved items, collections, likes, comments, reposts, and queued jobs from one desktop shell
- Preview the related media when comments point back to a post or reel
- Control page size and bulk-action concurrency from Settings
- Keep sensitive session material local to the device with encrypted storage abstractions
- Use a mock provider for UI validation and a capability-aware real provider where supported

## Tech Stack

- .NET 10
- Avalonia UI
- CommunityToolkit.Mvvm
- xUnit + FluentAssertions

## Getting Started

```bash
dotnet restore IGRes.sln
dotnet build IGRes.sln
dotnet test IGRes.sln
dotnet run --project src/Igres.Desktop/Igres.Desktop.csproj
```

## Quality Gates

The GitHub Actions workflow validates:

- restore
- build
- test
- Windows publish smoke test

## Privacy and Safety

IGRes is designed around honest capability disclosure:

- no token replay
- no traffic spoofing
- no hidden destructive actions
- no telemetry pipeline in the desktop app

See [SECURITY.md](SECURITY.md) for reporting guidance.

## Repository

- Owner: `D7Pr`
- Repository: `IGRes`
- URL: <https://github.com/D7Pr/IGRes>
