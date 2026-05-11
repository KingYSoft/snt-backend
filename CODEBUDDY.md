# CODEBUDDY.md

This file provides guidance to CodeBuddy Code when working with code in this repository.

## Build & Run Commands

```bash
# Build the entire solution
dotnet build SntBackend.sln

# Build with specific configuration
dotnet build SntBackend.sln -c Release

# Run the web host (entry point)
dotnet run --project src/SntBackend.Web.Host

# Run tests
dotnet test test/SntBackend.Tests/SntBackend.Tests.csproj

# Run a single test by name
dotnet test test/SntBackend.Tests/SntBackend.Tests.csproj --filter "FullyQualifiedName~HealthApplication_Tests"

# Run the entity code generator tool
dotnet run --project tools/SntBackend.EntityGenerate
```

The server runs on Kestrel at `http://*:21021` (configured in `hosting.json`).

## Solution Structure & Dependency Graph

```
SntBackend.DomainService.Share    (leaf - entities, interfaces, constants)
    ↑
SntBackend.SqlServer              (Dapper query repo impl, EF Core DbContext)
    ↑
SntBackend.DomainService          (domain services, AutoMapper, Quartz, background workers)
    ↑
SntBackend.Application            (app services with raw SQL via Dapper)
    ↑
SntBackend.Web.Core               (JWT auth, SignalR, Swagger, filters, middleware pipeline)
    ↑
SntBackend.Web.Host               (executable entry, controllers, hubs, appsettings)

SntBackend.Tests                  (xunit + Shouldly, refs Application layer)
SntBackend.EntityGenerate         (standalone code-gen tool, refs Facade.Dapper.SqlServer)
```

## Architecture

**Framework:** .NET 8.0 on ABP Framework with the Facade commercial wrapper (v6.0.4), providing base modules for Dapper, AutoMapper, Quartz, NLog, and AspNetCore.

**Business domain:** Logistics/freight forwarding — shipments, consolidations, containers, accounting (AR/AP billing), and write-off/reconciliation.

### Dual Repository Strategy

- **Dapper (primary, runtime):** `IAppSqlServerRepository` (extends Facade's `ISqlServerQueryRepository`) is what all application services inject and use. All data access is raw SQL via Dapper's `QueryAsync`, `QueryFirstOrDefaultAsync`, `QueryMultipleAsync`, `ExecuteScalarAsync`.
- **EF Core (generated, scaffolding):** The code generator produces per-entity repository interfaces and implementations (e.g., `IAccChargeCodeRepository`, `AccChargeCodeRepository`), but no application code currently uses them. They are registered via ABP convention and available for future CRUD use.

### Application Service Pattern

All app services follow this pattern:
1. Interface inherits `ISntBackendApplicationBase` → implementation extends `SntBackendApplicationBase`
2. Constructor injects `IAppSqlServerRepository`
3. Methods write parameterized raw SQL with `DynamicParameters`
4. Paginated queries use `OFFSET ... FETCH NEXT` with `QueryMultipleAsync` (count + data)

### Key Cross-Cutting Concerns

- **JWT Auth:** Tokens created in `UserController.Login`, encrypted with `SimpleStringCipher` using `AppConsts.DefaultPassPhrase`. SignalR clients pass `enc_auth_token` query param, decrypted by `QueryStringTokenResolver` in `AuthConfigurer`.
- **SignalR:** Hub at `/signalr`. ABP's `SignalRRealTimeNotifier` is replaced with `NewSignalRRealTimeNotifier` (fixes EntityType serialization disconnect issue).
- **Request debounce:** `DebounceActionFilter` provides 2-second deduplication on `[Debounce]`-attributed endpoints, using MD5 hash cache keys.
- **Model binding:** Auto-trim strings (`TrimStringModelBinder`), number-to-string JSON conversion (`NumberToStringJsonConverter`).

### Code Generation

`SntBackend.EntityGenerate` reads SQL Server schema metadata and generates files with `.generate.cs` suffix:
- Entity classes (`DomainService.Share/Po/Po.generate.cs`)
- Repository interfaces (`DomainService.Share/Repositories/IPoRepository.generate.cs`)
- EF Core repository implementations (`SqlServer/.../PoRepository.generate.cs`)
- Input/Output DTOs and AutoMapper profiles (`Application/Generates/Dto/`)

**Do not edit `.generate.cs` files** — regenerate them instead.

### ABP Module System

Each project has an `*Module.cs` file defining its ABP module with `[DependsOn(...)]` attributes. The startup chain is: `SntBackendWebHostModule` → `SntBackendWebCoreModule` → `SntBackendApplicationModule` → `SntBackendDomainServiceModule` → `SntBackendSqlServerModule` → `SntBackendDomainServiceShareModule`.

Key module configurations:
- `SntBackendWebCoreModule.PreInitialize()`: registers `FacadeConfiguration`, JWT auth, localization (EN + zh-CN), UoW 30s timeout
- `SntBackendWebHostModule.PreInitialize()`: sets `Clock.Provider = Local`, Facade licence key
- `SntBackendDomainServiceModule`: registers `ClearLoggerWorker` (hourly, deletes logs >90 days)

## Configuration

- `appsettings.json` in `SntBackend.Web.Host`: SQL Server connection string, JWT settings, CORS origins, Facade app name
- `SntBackendConsts.cs` in `DomainService.Share`: `MultiTenancyEnabled = false`, `ConnectionStringName = "Default"`
- Multi-tenancy is disabled; no Redis is configured
- Swagger is available in Development/Test only (OpenAPI 2.0 format)
- Quartz scheduling is configured but `StartQuartz()` is commented out in `SntBackendDomainServiceModule`
