# DreamBig.SourceGen.Dapper

`DreamBig.SourceGen.Dapper` provides compile-time SQL generation for Dapper users who want to avoid raw SQL in application code.

## Features

- Attribute-driven repository generation (`[DbRepository]`)
- Entity mapping attributes (`[DbTable]`, `[DbColumn]`, `[DbKey]`, `[DbIgnore]`)
- Generated CRUD methods
- Generated stored procedure execution with output parameter support
- Generated INNER/OUTER joins from method-level attributes
- SQL Server-first generation model

## Target Frameworks

Runtime library supports:

- `net5.0`
- `net6.0`
- `net7.0`
- `net8.0`
- `net10.0`

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

The source generator emits `ICustomerRepositoryGenerated`, with SQL and Dapper calls already implemented.

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

[DbQuery(From = "[dbo].[Customers] c", Where = "c.[IsActive] = @isActive", OrderBy = "c.[Id] DESC")]
[DbJoin(JoinType.Left, "[dbo].[Orders] o", "c.Id", "o.CustomerId")]
Task<IEnumerable<CustomerSummary>> QueryActive(bool isActive, CancellationToken cancellationToken);
```

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

// generated type: IAppUnitOfWorkGenerated
var uow = new IAppUnitOfWorkGenerated(() => new SqlConnection(connectionString));

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

- SQL dialect support is SQL Server-first.
- Stored procedures support one mapped result set plus output parameter capture.
- Complex projection/multi-mapping scenarios are not yet generated.

## Future Dialect Migration Plan

The generator architecture is designed to evolve into provider-specific SQL emitters (`SqlServer`, `PostgreSql`, `MySql`) while preserving the same attribute contracts.
