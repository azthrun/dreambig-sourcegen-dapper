# DreamBig.SourceGen.Dapper

`DreamBig.SourceGen.Dapper` provides compile-time SQL generation for Dapper users who want to avoid raw SQL in application code.

## Features

- Attribute-driven repository generation (`[DbRepository]`)
- Entity mapping attributes (`[DbTable]`, `[DbColumn]`, `[DbKey]`, `[DbIgnore]`)
- Generated CRUD methods
- Generated stored procedure execution with output parameter support
- Generated INNER/OUTER joins from method-level attributes
- Provider-specific SQL generation (SQL Server and PostgreSQL)

## Target Frameworks

Runtime library supports:

- `net10.0`

## Install

- SQL Server: `DreamBig.SourceGen.Dapper.SqlServer`
- PostgreSQL: `DreamBig.SourceGen.Dapper.PostgreSql`

## DI Setup

SQL Server:

```csharp
using DreamBig.SourceGen.Dapper.SqlServer;

services.AddDreamBigDapperSqlServer(configuration);
services.AddDreamBigDapperGenerated();
```

PostgreSQL:

```csharp
using DreamBig.SourceGen.Dapper.PostgreSql;

services.AddDreamBigDapperPostgreSql(configuration);
services.AddDreamBigDapperGenerated();
```

Configuration sections:

- SQL Server: `DreamBig:Dapper:SqlServer`
- PostgreSQL: `DreamBig:Dapper:PostgreSql`

Overloads are available for raw connection strings and custom connection string factories.

## Quick Start

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

The source generator emits `CustomerRepositoryGenerated`, with SQL and Dapper calls already implemented.

## Primary Key Without `[DbKey]`

You can define the primary key directly on `DbTableAttribute` using `PrimaryKey`.
This is useful when the entity class comes from another team/package and you do not want to modify the entity source.

```csharp
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbTable("Customers", Schema = "dbo", PrimaryKey = "CustomerId")]
public sealed class ExternalCustomer
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface IExternalCustomerRepository
{
    Task<int> UpdateCustomer(ExternalCustomer entity, CancellationToken cancellationToken);
    Task<int> DeleteCustomer(int customerId, CancellationToken cancellationToken);
    Task<ExternalCustomer?> GetByIdCustomer(int customerId, CancellationToken cancellationToken);
}
```

`PrimaryKey` can match either:
- the CLR property name (`CustomerId`), or
- the mapped SQL column name when `DbColumnAttribute` is used.

## Join Example

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbJoin(
    JoinType = JoinType.Left,
    JoinTableA = typeof(Customer),
    JoinTableB = typeof(Order),
    JoinColumnA = "Id",
    JoinColumnB = "CustomerId",
    AliasA = "customers",
    AliasB = "orders",
    SchemaB = "sales",
    On = "orders.IsDeleted = false",
    Where = "customers.IsActive = @isActive",
    OrderBy = "customers.Id",
    OrderByDirection = OrderByDirection.Desc)]
Task<IEnumerable<CustomerSummary>> QueryActive(bool isActive, CancellationToken cancellationToken);
```

Chained joins can be expressed by adding multiple `[DbJoin]` attributes and referencing an earlier alias:

```csharp
[DbJoin(
    JoinType = JoinType.Left,
    JoinTableA = typeof(Customer),
    JoinTableB = typeof(Order),
    JoinColumnA = "Id",
    JoinColumnB = "CustomerId",
    AliasA = "customers",
    AliasB = "orders")]
[DbJoin(
    JoinType = JoinType.Left,
    JoinTableA = typeof(Order),
    JoinTableB = typeof(OrderLine),
    JoinColumnA = "Id",
    JoinColumnB = "OrderId",
    AliasA = "orders",
    AliasB = "orderLines",
    On = "orderLines.IsArchived = false",
    Where = "orderLines.OrderId = @orderId")]
Task<IEnumerable<CustomerSummary>> QueryCustomerOrders(int orderId, CancellationToken cancellationToken);
```

Notes:
- The generator assigns readable aliases from table names by default, such as `customers`, `orders`, and `orderLines`.
- `Where`, `On`, and `OrderBy` support `alias.Property` syntax and map CLR property names to SQL columns.
- Bare property names are only valid when they are unique across the joined tables.
- `JoinColumnA` and `JoinColumnB` must match CLR property names on `JoinTableA` and `JoinTableB` (validated at compile time).
- Multiple `[DbJoin]` attributes can be chained to build larger join graphs.

## Stored Procedure Example

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

## Unit Of Work Example

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    ICustomerRepository Customers { get; }
    IOrderRepository Orders { get; }
}

// generated type: AppUnitOfWorkGenerated
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

Notes:
- `Insert*`, `Update*`, `Delete*`, and stored procedure methods require an active transaction and throw `InvalidOperationException` otherwise.
- Read/query methods can execute with or without an active transaction.

## Known Limitations (v1)

- SQL dialect support is limited to SQL Server and PostgreSQL.
- Stored procedures support one mapped result set plus output parameter capture.
- Complex projection/multi-mapping scenarios are not yet generated.

## Dialect Packages

The generator emits SQL based on the provider-specific package you install (`DreamBig.SourceGen.Dapper` for SQL Server, `DreamBig.SourceGen.Dapper.PostgreSql` for PostgreSQL) while preserving the same attribute contracts.
