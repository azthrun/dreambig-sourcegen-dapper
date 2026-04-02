# DreamBig.SourceGen.Dapper

`DreamBig.SourceGen.Dapper` provides compile-time SQL generation for Dapper users who want to keep repository code small, typed, and consistent.

It generates repository implementations, Unit of Work wrappers, and DI registrations from attributes and interface conventions.

## What You Get

- Repository generation from `[DbRepository]`
- Entity mapping with `[DbTable]`, `[DbColumn]`, `[DbKey]`, and `[DbIgnore]`
- Generated CRUD operations
- Generated query composition with `[DbQuery]` and `[DbJoin]`
- Generated stored procedure execution with output parameter support
- Generated Unit of Work implementations
- Provider-specific SQL for SQL Server and PostgreSQL

## Install

Choose one provider package:

- SQL Server: `DreamBig.SourceGen.Dapper.SqlServer`
- PostgreSQL: `DreamBig.SourceGen.Dapper.PostgreSql`

### Package Reference

```xml
<ItemGroup>
  <PackageReference Include="DreamBig.SourceGen.Dapper.SqlServer" Version="x.y.z" />
</ItemGroup>
```

or:

```xml
<ItemGroup>
  <PackageReference Include="DreamBig.SourceGen.Dapper.PostgreSql" Version="x.y.z" />
</ItemGroup>
```

## Runtime Support

This release targets `net10.0` only.

If your application is on an earlier target, that is currently the main adoption limit to be aware of before you invest in the attribute model.

## Quick Start

1. Mark an entity with `[DbTable]`.
2. Mark a repository interface with `[DbRepository]`.
3. Register the provider package and the generated services.
4. Inject the repository and call the interface.

### Entity and Repository

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    [DbColumn("full_name")]
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> UpdateCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> DeleteCustomer(int id, CancellationToken cancellationToken);
    Task<Customer?> GetByIdCustomer(int id, CancellationToken cancellationToken);
    Task<IEnumerable<Customer>> GetAllCustomers(CancellationToken cancellationToken);
}
```

The generator emits `CustomerRepositoryGenerated`.

### DI Registration

```csharp
using DreamBig.SourceGen.Dapper.SqlServer;

services.AddDreamBigDapperSqlServer(configuration);
services.AddDreamBigDapperGenerated();
```

For PostgreSQL, use `AddDreamBigDapperPostgreSql(configuration)` instead.

The generator also emits `AddDreamBigDapperGenerated(IServiceCollection)`, which registers generated repositories and Unit of Work types as scoped services.

Configuration sections:

- SQL Server: `DreamBig:Dapper:SqlServer`
- PostgreSQL: `DreamBig:Dapper:PostgreSql`

Both provider packages also expose overloads for raw connection strings and custom connection string factories.

## How Generation Works

### Repository Conventions

- `[DbRepository]` marks an interface for generation.
- Repository methods are classified by name unless they use `[DbQuery]` or `[DbStoredProcedure]`.
- Supported CRUD-style prefixes are:
  - `Insert*`
  - `Update*`
  - `Delete*`
  - `GetById*`
  - `GetAll*`
  - `GetPage*`
- Repository methods must return `Task` or `Task<T>`.
- Every repository method must include a `CancellationToken` parameter.

### Entity Mapping

- `[DbTable]` names the table and can optionally specify `Schema` and `PrimaryKey`.
- `[DbColumn]` maps a CLR property to a SQL column.
- `[DbKey]` marks the key property used by update, delete, and get-by-id operations.
- `[DbIgnore]` excludes a property from generated SQL.
- `DbTableAttribute.PrimaryKey` can be used instead of `[DbKey]` when you cannot modify the entity source.

### Delete Method Resolution

`Delete*` methods infer the entity from the method name:

- `DeleteCustomer` -> `Customer`
- `DeleteCustomerById` -> `Customer`
- `DeleteCustomers` -> `Customer`

The method name should still mirror the entity name closely. If the convention is too ambiguous for your domain, prefer a query or stored-procedure method instead of relying on delete-name inference.

### Query Generation

- `[DbQuery]` defines query composition.
- `[DbJoin]` adds typed join definitions and can be repeated to chain joins.
- `Where`, `On`, and `OrderBy` support `alias.Property` syntax.
- Bare property names only work when they are unique across the joined tables.
- `JoinColumnA` and `JoinColumnB` must match CLR property names on the joined entities.
- For PostgreSQL, `DbRepository(CaseSensitive = false)` emits unquoted identifiers for consumers who want unquoted SQL.

### Stored Procedures

- `[DbStoredProcedure]` marks a method as a stored-procedure call.
- `[DbParam]` adds parameter metadata, including direction, DbType, and size.
- Stored procedures return `GeneratedProcedureResult<T>` when rows and output parameters are both needed.

### Unit of Work

- `[DbUnitOfWork]` marks a read-only interface that exposes generated repositories as properties.
- Repository properties must be read-only and must point to interfaces marked with `[DbRepository]`.
- The generated Unit of Work also manages transactions and exposes `BeginTransactionAsync`, `CommitAsync`, and `RollbackAsync`.

## Public API Reference

| API | Purpose |
| --- | --- |
| `[DbRepository]` | Marks an interface for repository generation |
| `[DbUnitOfWork]` | Marks an interface for Unit of Work generation |
| `[DbTable]` | Maps an entity type to a table |
| `[DbColumn]` | Maps a property to a column |
| `[DbKey]` | Marks the entity key |
| `[DbIgnore]` | Excludes a property from SQL generation |
| `[DbQuery]` | Declares generated query composition |
| `[DbJoin]` | Declares typed joins for a query method |
| `[DbStoredProcedure]` | Declares a stored procedure call |
| `[DbParam]` | Declares stored procedure parameter metadata |
| `AddDreamBigDapperSqlServer(...)` | Registers SQL Server connection and DI support |
| `AddDreamBigDapperPostgreSql(...)` | Registers PostgreSQL connection and DI support |
| `AddDreamBigDapperGenerated()` | Registers generated repositories and Unit of Work types |
| `GeneratedProcedureResult<T>` | Wraps rows and output parameter values from a stored procedure |

## Examples

### CRUD

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> UpdateCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> DeleteCustomer(int id, CancellationToken cancellationToken);
    Task<Customer?> GetByIdCustomer(int id, CancellationToken cancellationToken);
    Task<IEnumerable<Customer>> GetAllCustomers(CancellationToken cancellationToken);
}
```

### Query and Join

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbRepository]
public interface ICustomerReadRepository
{
    [DbQuery(From = "Customers", Schema = "dbo", Where = "customers.IsActive = @isActive", OrderBy = "customers.Id")]
    [DbJoin(
        JoinType = JoinType.Left,
        JoinTableA = typeof(Customer),
        JoinTableB = typeof(Order),
        JoinColumnA = "Id",
        JoinColumnB = "CustomerId",
        AliasA = "customers",
        AliasB = "orders")]
    Task<IEnumerable<CustomerSummary>> QueryActive(bool isActive, CancellationToken cancellationToken);
}
```

### Stored Procedure

```csharp
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Internal;

[DbStoredProcedure("usp_customer_summary", Schema = "dbo")]
Task<GeneratedProcedureResult<CustomerSummary>> GetSummary(
    [DbParam("@customerId", DbType = DbType.Int32)] int customerId,
    [DbParam("@total", Direction = DbParamDirection.Output)] int total,
    CancellationToken cancellationToken);
```

### Unit of Work

```csharp
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    ICustomerRepository Customers { get; }
    IOrderRepository Orders { get; }
}

var uow = new AppUnitOfWorkGenerated(() => new SqlConnection(connectionString));

await uow.BeginTransactionAsync(cancellationToken: cancellationToken);
try
{
    await uow.Customers.UpdateCustomer(customer, cancellationToken);
    await uow.Orders.DeleteOrder(orderId, cancellationToken);
    await uow.CommitAsync(cancellationToken);
}
catch
{
    await uow.RollbackAsync(cancellationToken);
    throw;
}
```

## Common Mistakes and Diagnostics

| ID | What it means | Most likely fix |
| --- | --- | --- |
| `DBSGD001` | Entity is missing a key | Add `[DbKey]` or set `DbTable(PrimaryKey = ...)` |
| `DBSGD002` | Method shape is unsupported | Rename to a supported prefix or use `[DbQuery]` / `[DbStoredProcedure]` |
| `DBSGD006` | Async return type is required | Change the method to `Task` or `Task<T>` |
| `DBSGD007` | `CancellationToken` is required | Add a `CancellationToken` parameter |
| `DBSGD008` | Unit of Work member is invalid | Use only read-only repository properties |
| `DBSGD009` | Unit of Work repository type is invalid | Point the property at an interface marked with `[DbRepository]` |
| `DBSGD010` | Unit of Work has no repositories | Add at least one repository property |
| `DBSGD011` | Repository generation failed | Fix the repository diagnostics first |
| `DBSGD012` | Duplicate Unit of Work property names | Rename one of the properties |
| `DBSGD015` | Join column is invalid | Use a CLR property name that exists on the joined entity |
| `DBSGD018` | Query reference is ambiguous | Qualify the column with an alias |
| `DBSGD019` | Join alias is duplicated | Provide unique aliases |
| `DBSGD020` | Join source alias is invalid | Make sure the alias was introduced by an earlier join |
| `DBSGD021` | Multiple `ORDER BY` clauses were configured | Keep only one `ORDER BY` source |

## Known Limitations

- SQL dialect support is limited to SQL Server and PostgreSQL.
- Stored procedures currently support one mapped result set plus output parameter capture.
- Complex projection and multi-mapping scenarios are not generated yet.
