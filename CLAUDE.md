# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build solution
dotnet build DreamBig.SourceGen.Dapper.sln

# Run all tests
dotnet test DreamBig.SourceGen.Dapper.sln

# Run single test (replace test name)
dotnet test --filter "FullyQualifiedName~ShouldGenerateCrudImplementation"

# Build in Release configuration
dotnet build -c Release
```

## Architecture Overview

This is a **C# source generator** library that generates Dapper repository implementations at compile-time. The solution has three main projects:

### Project Structure

- **DreamBig.SourceGen.Dapper.Generator** (`netstandard2.0`) - The Roslyn source generator that emits repository code
- **DreamBig.SourceGen.Dapper.Abstractions** (`net8.0`) - Runtime attributes and extension methods consumed by generated code
- **DreamBig.SourceGen.Dapper.Package** - Packaging project for NuGet distribution

### Test Projects

- **DreamBig.SourceGen.Dapper.Tests** - Tests for runtime extensions and internal types
- **DreamBig.SourceGen.Dapper.Generator.Tests** - Tests for source generator output and diagnostics

### Key Attributes

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[DbRepository]` | Interface | Marks interface for repository generation |
| `[DbUnitOfWork]` | Interface | Marks interface for Unit of Work pattern generation |
| `[DbTable]` | Class | Maps entity to SQL table (supports `TableName`, `Schema`, `PrimaryKey`) |
| `[DbColumn]` | Property | Maps property to column name |
| `[DbKey]` | Property | Marks primary key property |
| `[DbIgnore]` | Property | Excludes property from SQL operations |
| `[DbQuery]` | Method | Custom query with `From`, `Where`, `OrderBy` |
| `[DbJoin]` | Method | Declares JOIN operations |
| `[DbStoredProcedure]` | Method | Marks method as stored procedure call |
| `[DbParam]` | Parameter | Configures stored procedure parameters |

### Generated Patterns

- Repository methods must return `Task<T>` and accept `CancellationToken` as last parameter
- Generated class naming: `IRepositoryName` → `IRepositoryNameGenerated`
- Unit of Work naming: `IUnitOfWorkName` → `IUnitOfWorkNameGenerated`
- CRUD methods: `Insert*`, `Update*`, `Delete*`, `GetById*`, `GetAll*`
- Write operations require active transaction (enforced at runtime)

### Diagnostics

The generator emits diagnostics for:
- Missing primary key (`DBSGD001`)
- Non-async return types (`DBSGD006`)
- Missing `CancellationToken` (`DBSGD007`)
- Invalid Unit of Work members (`DBSGD008`-`DBSGD012`)

## Development Notes

- Uses **xunit** + **Shouldly** for testing
- Source generator uses Roslyn APIs (`IIncrementalGenerator`)
- SQL dialect is SQL Server-first
- Nullable reference types enabled globally
- TreatWarningsAsErrors enabled
