# Contributing

## Development Flow

1. Create a branch for your change.
2. Run `dotnet restore IGRes.sln`.
3. Run `dotnet build IGRes.sln`.
4. Run `dotnet test IGRes.sln`.
5. Keep UI changes consistent with the existing rounded visual language and safety-first copy.

## Pull Request Expectations

- Keep changes focused.
- Add or update tests when behavior changes.
- Do not include local build outputs, captured session material, or `user-data`.
- Prefer clear user-facing copy and explicit capability messaging.

## Code Style

- Use nullable reference types.
- Keep MVVM boundaries clear.
- Put risky provider-specific logic in infrastructure layers.
