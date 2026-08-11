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
- Provider-specific SQL for SQL Server, PostgreSQL, SQLite, and MySQL/MariaDB

## Install

Choose one provider package:

- SQL Server: `DreamBig.SourceGen.Dapper.SqlServer`
- PostgreSQL: `DreamBig.SourceGen.Dapper.PostgreSql`
- SQLite: `DreamBig.SourceGen.Dapper.Sqlite`
- MySQL / MariaDB: `DreamBig.SourceGen.Dapper.MySql`

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

The runtime packages target `net8.0` and `net10.0`, so any application on .NET 8 or later can use them.

The source generator itself targets `netstandard2.0`, as required by the Roslyn analyzer host.

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

For PostgreSQL, use `AddDreamBigDapperPostgreSql(configuration)` instead; for SQLite, `AddDreamBigDapperSqlite(configuration)`; for MySQL/MariaDB, `AddDreamBigDapperMySql(configuration)`.

The generator also emits `AddDreamBigDapperGenerated(IServiceCollection)`, which registers generated repositories and Unit of Work types as scoped services.

Configuration sections:

- SQL Server: `DreamBig:Dapper:SqlServer`
- PostgreSQL: `DreamBig:Dapper:PostgreSql`
- SQLite: `DreamBig:Dapper:Sqlite`
- MySQL / MariaDB: `DreamBig:Dapper:MySql`

Both provider packages also expose overloads for raw connection strings and custom connection string factories.

## How Generation Works

### Repository Conventions

- `[DbRepository]` marks an interface for generation.
- Repository methods are classified by name unless they use `[DbQuery]`, `[DbStoredProcedure]`, or `[DbOperation]`.
- Supported CRUD-style prefixes are:
  - `Insert*`
  - `Update*`
  - `Delete*`
  - `GetById*` (natural `Get{Entity}ById` naming also works)
  - `GetAll*`
  - `GetPage*`
  - `Count*`
  - `Exists*`
- Repository methods must return `Task` or `Task<T>`.
- Every repository method must include a `CancellationToken` parameter.

### Filtered Conventions

`By{Property}` clauses generate typed `WHERE` filters. Chain multiple properties with `And`; parameters bind to properties positionally:

```csharp
Task<Customer?> GetCustomerByEmail(string email, CancellationToken ct);
Task<IEnumerable<Customer>> GetCustomersByEmailAndIsActive(string email, bool isActive, CancellationToken ct);
Task<int> DeleteCustomerByEmail(string email, CancellationToken ct);
Task<int> CountCustomersByIsActive(bool isActive, CancellationToken ct);      // Task<int> or Task<long>
Task<bool> ExistsCustomerByEmail(string email, CancellationToken ct);
```

Property names are validated against the entity at compile time (diagnostic `DBSGD027`). `Count*` and `Exists*` resolve the entity from the method name (`CountCustomers` -> `Customer`) or from `[DbOperation(Entity = ...)]`.

### Bulk Operations

Write methods accept collections. Inserts and updates execute the statement per item through Dapper's multi-execute, and plural `By` clauses with enumerable parameters generate `IN` filters:

```csharp
Task<int> InsertCustomers(IEnumerable<Customer> entities, CancellationToken ct);
Task<int> DeleteCustomersByIds(IEnumerable<int> ids, CancellationToken ct);   // WHERE [Id] IN @ids
Task<IEnumerable<Customer>> GetCustomersByIds(IReadOnlyList<int> ids, CancellationToken ct);
```

### Streaming

Read methods can return `IAsyncEnumerable<T>` to stream rows without buffering (uses Dapper's unbuffered query; requires a `DbConnection`-based provider connection, which both provider packages supply):

```csharp
IAsyncEnumerable<Customer> GetAllCustomers(CancellationToken ct);
IAsyncEnumerable<Customer> GetCustomersByEmail(string email, CancellationToken ct);

[DbQuery(From = "Customers", Where = "IsActive = @isActive")]
IAsyncEnumerable<Customer> QueryActive(bool isActive, CancellationToken ct);
```

Streaming is supported for `GetAll*`, `GetBy*`, and `[DbQuery]` methods.

### Explicit Operations

`[DbOperation]` overrides name conventions entirely, so methods can be named freely:

```csharp
[DbOperation(DbOperationKind.GetById)]
Task<Customer?> Find(int id, CancellationToken ct);

[DbOperation(DbOperationKind.Count, Entity = typeof(Customer))]
Task<int> HowMany(CancellationToken ct);
```

When no `By` clause is present, filter properties for `GetBy`, `Count`, and `Exists` operations are inferred from parameter names.

### Insert Returning Identity

Set `ReturnIdentity = true` to return the database-generated key instead of the affected row count (`OUTPUT INSERTED` on SQL Server, `RETURNING` on PostgreSQL/SQLite, a batched `LAST_INSERT_ID()` select on MySQL/MariaDB):

```csharp
[DbOperation(DbOperationKind.Insert, ReturnIdentity = true)]
Task<int> InsertCustomer(Customer entity, CancellationToken ct);
```

### Paging

`GetPage*` methods take two parameters that are bound by name, not position:

- The skip parameter must be named `skip` or `offset`.
- The take parameter must be named `take`, `limit`, `pageSize`, or `fetch`.

Parameter order does not matter. Unrecognized names produce diagnostic `DBSGD025` instead of silently guessing.

By default pages are ordered by the entity key. To order by another property, add `[DbQuery]` with only ordering members:

```csharp
[DbQuery(OrderBy = "Name", OrderByDirection = OrderByDirection.Desc)]
Task<IEnumerable<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
```

`OrderBy` takes a CLR property name on the entity; unknown names produce diagnostic `DBSGD016`.

Return `PagedResult<T>` instead of `IEnumerable<T>` to get the total row count alongside the page in a single round trip:

```csharp
Task<PagedResult<Customer>> GetPageCustomers(int skip, int take, CancellationToken ct);

var page = await repository.GetPageCustomers(20, 10, ct);
// page.Items, page.TotalCount, page.Skip, page.Take
```

### Generated SQL Constants

Every generated repository exposes the exact SQL it executes as constants in a nested `Sql` class:

```csharp
Console.WriteLine(CustomerRepositoryGenerated.Sql.InsertCustomer);
// INSERT INTO [dbo].[Customers] ([full_name], [Email]) VALUES (@Name, @Email);
```

Use these to review generated SQL, log it, or assert on it in your own tests. For stored procedure methods the constant holds the resolved procedure name.

### Entity Mapping

- `[DbTable]` names the table and can optionally specify `Schema` and `PrimaryKey`.
- `[DbColumn]` maps a CLR property to a SQL column.
- `[DbKey]` marks the key property used by update, delete, and get-by-id operations.
- `[DbIgnore]` excludes a property from generated SQL.
- `[DbRowVersion]` marks a database-generated concurrency token (for example SQL Server `rowversion`). The column is excluded from INSERT and UPDATE SET clauses and appended to the WHERE clause of updates and entity-based deletes, so a stale write affects zero rows — check the returned row count to detect conflicts.
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
- Every `@parameter` referenced in a query string must match a method parameter name; unknown references produce diagnostic `DBSGD026` at compile time.
- `JoinColumnA` and `JoinColumnB` must match CLR property names on the joined entities.
- For PostgreSQL, `DbRepository(CaseSensitive = false)` emits unquoted identifiers for consumers who want unquoted SQL.
- MySQL/MariaDB always emits backtick-quoted identifiers; `CaseSensitive` has no effect for that provider (see [MySQL / MariaDB dialect notes](#mysql--mariadb-dialect-notes)).

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
| `[DbRowVersion]` | Marks a concurrency token used in update/delete WHERE clauses |
| `[DbQuery]` | Declares generated query composition |
| `[DbJoin]` | Declares typed joins for a query method |
| `[DbStoredProcedure]` | Declares a stored procedure call |
| `[DbParam]` | Declares stored procedure parameter metadata |
| `[DbOperation]` | Declares the operation explicitly, overriding name conventions |
| `AddDreamBigDapperSqlServer(...)` | Registers SQL Server connection and DI support |
| `AddDreamBigDapperPostgreSql(...)` | Registers PostgreSQL connection and DI support |
| `AddDreamBigDapperMySql(...)` | Registers MySQL/MariaDB connection and DI support |
| `AddDreamBigDapperGenerated()` | Registers generated repositories and Unit of Work types |
| `GeneratedProcedureResult<T>` | Wraps rows and output parameter values from a stored procedure |
| `PagedResult<T>` | Wraps one page of rows plus the total row count |

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
| `DBSGD016` | OrderBy column is invalid | Use a CLR property name that exists on the entity |
| `DBSGD018` | Query reference is ambiguous | Qualify the column with an alias |
| `DBSGD019` | Join alias is duplicated | Provide unique aliases |
| `DBSGD020` | Join source alias is invalid | Make sure the alias was introduced by an earlier join |
| `DBSGD021` | Multiple `ORDER BY` clauses were configured | Keep only one `ORDER BY` source |
| `DBSGD025` | GetPage parameters cannot be identified | Name the parameters `skip`/`offset` and `take`/`limit`/`pageSize`/`fetch` |
| `DBSGD026` | Query references an unknown SQL parameter | Fix the `@parameter` name to match a method parameter |
| `DBSGD027` | Convention property is invalid | Use a CLR property name that exists on the entity in the `By` clause |
| `DBSGD028` | Query parameter is unused | Reference the parameter in the query string or remove it |
| `DBSGD029` | MySQL identity key type is unsupported | Use an integer auto-increment key (`int`, `long`, `short`, `byte`, or an unsigned variant) with `ReturnIdentity = true` on MySQL/MariaDB |

## MySQL / MariaDB Dialect Notes

Reference `DreamBig.SourceGen.Dapper.MySql` for MySQL 5.7+ or MariaDB 10.3+.

- Identifiers are always backtick-quoted (`` `Column` ``); `DbRepository(CaseSensitive = ...)` has no effect on this provider.
- MySQL/MariaDB have no schema concept separate from the database; `Schema` values on `[DbTable]`, `[DbQuery]`, and `[DbJoin]` are ignored, the same as SQLite.
- `ReturnIdentity = true` emits a batched `INSERT ...; SELECT LAST_INSERT_ID();` since plain MySQL has no `OUTPUT`/`RETURNING` clause. This is restricted at compile time to entities whose key is an integer auto-increment column (diagnostic `DBSGD029` otherwise).
- MariaDB's native `RETURNING` (10.5+) is intentionally not used, so MySQL and MariaDB share one generated SQL path.

## Integration Testing with SQLite

The SQLite provider makes generated repositories testable without database infrastructure. Reference `DreamBig.SourceGen.Dapper.Sqlite` from a test project, create an in-memory database, and exercise the same generated code paths your application uses:

```csharp
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await connection.ExecuteAsync("CREATE TABLE Customers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);");

using var transaction = connection.BeginTransaction();
ICustomerRepository repository = new CustomerRepositoryGenerated(connection, transaction);

var id = await repository.InsertCustomer(new Customer { Name = "Ada" }, ct);
```

SQLite dialect notes:

- SQLite has no schemas; `Schema` values on `[DbTable]`, `[DbQuery]`, and `[DbJoin]` are ignored.
- `[DbStoredProcedure]` methods are not usable because SQLite has no stored procedures.
- `AddDreamBigDapperSqlite` registers scoped connections, so with `Data Source=:memory:` every scope opens a fresh, empty database. For DI-based tests use a shared in-memory database — `Data Source=TestDb;Mode=Memory;Cache=Shared` — and hold one connection open for the lifetime of the test so the database survives between scopes.
- Insert-returning-identity uses `RETURNING`, which requires SQLite 3.35+ (bundled with `Microsoft.Data.Sqlite`).

## Known Limitations

- SQL dialect support is limited to SQL Server, PostgreSQL, SQLite, and MySQL/MariaDB.
- Stored procedures currently support one mapped result set plus output parameter capture.
- Complex projection and multi-mapping scenarios are not generated yet.
