# Agents

## Coding workflow

- Always run `dotnet build` after code changes.
- Always run `dotnet test` after successful build to validate behavior.
- Always launch the WPF application after successful tests and verify that its process remains running through startup initialization.
- Do not report completion of implementation until build, tests, and the WPF startup smoke check have passed (or clearly explain why any check cannot run).
