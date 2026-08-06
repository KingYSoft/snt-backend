# Repository Guidelines

## Project Structure & Module Organization

This is a .NET 8 backend solution (`SntBackend.sln`) built on ABP/Facade conventions. Source projects live under `src/`: `SntBackend.Application` contains application services and DTOs, `SntBackend.DomainService` contains domain services/background work, `SntBackend.DomainService.Share` contains shared constants, PO models, and repository abstractions, `SntBackend.SqlServer` contains EF Core/SQL Server integration, and `SntBackend.Web.Host` is the runnable ASP.NET Core host. Tests live in `test/SntBackend.Tests`. Utility code generation lives in `tools/SntBackend.EntityGenerate`.

## Build, Test, and Development Commands

- `dotnet restore SntBackend.sln` restores NuGet packages.
- `dotnet build SntBackend.sln` builds all projects.
- `pwsh ./build.ps1 -Configuration Release` runs the repository build script with single-node MSBuild.
- `dotnet test test/SntBackend.Tests/SntBackend.Tests.csproj` runs the xUnit test suite.
- `dotnet run --project src/SntBackend.Web.Host/SntBackend.Web.Host.csproj` starts the API host locally. Configure connection strings in `src/SntBackend.Web.Host/appsettings.Development.json`; use the `.example` file as a template.

## Coding Style & Naming Conventions

Use C# conventions with four-space indentation, PascalCase for public types and members, camelCase for locals, and `_camelCase` for private fields. Keep application services paired with interfaces, for example `BillingApplication` and `IBillingApplication`. Place request/response DTOs in feature-specific `Dto` folders and follow existing suffixes such as `Input`, `Output`, `CreateInput`, and `TblOutput`. Generated files use the `.generate.cs` suffix; avoid hand-editing generated code unless the generator is also updated.

## Testing Guidelines

Tests use xUnit with `Facade.AspNetCore.TestBase` and Shouldly available. Add tests under `test/SntBackend.Tests/<Feature>` and derive fixture classes from `SntBackendTestBase` when dependency resolution is needed. Existing naming uses `<Feature>Application_Tests` classes and `[Fact]` methods such as `Check_Test`; follow that style for consistency. Run `dotnet test` before opening a PR.

## Commit & Pull Request Guidelines

Recent history uses short imperative messages, often `add ...`, `fix ...`, or concise Chinese summaries, with feature branches such as `feature/shp-page`. Keep commits focused and describe the user-visible change or bug fixed. Pull requests should include a summary, testing performed, related issue or requirement, and any configuration/database notes. Include screenshots only when API-facing Swagger or UI-visible behavior changes.

## Security & Configuration Tips

Do not commit real secrets or production connection strings. Keep local overrides in `appsettings.Development.json` and update `appsettings.Development.json.example` when a new required setting is introduced.
