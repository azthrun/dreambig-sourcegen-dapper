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
    int InsertCustomer(Customer entity);
    int UpdateCustomer(Customer entity);
    int DeleteCustomer(int id);
    Customer? GetByIdCustomer(int id);
    IEnumerable<Customer> GetAllCustomers();
}
```

The source generator emits `ICustomerRepositoryGenerated`, with SQL and Dapper calls already implemented.

## Join Example

```csharp
[DbQuery(From = "[dbo].[Customers] c", Where = "c.[IsActive] = @isActive", OrderBy = "c.[Id] DESC")]
[DbJoin(JoinType.Left, "[dbo].[Orders] o", "c.Id", "o.CustomerId")]
IEnumerable<CustomerSummary> QueryActive(bool isActive);
```

## Stored Procedure Example

```csharp
[DbStoredProcedure("usp_customer_summary", Schema = "dbo")]
GeneratedProcedureResult<CustomerSummary> GetSummary(
    [DbParam("@customerId", DbType = DbType.Int32)] int customerId,
    [DbParam("@total", Direction = DbParamDirection.Output)] int total);
```

## Known Limitations (v1)

- SQL dialect support is SQL Server-first.
- Stored procedures support one mapped result set plus output parameter capture.
- Complex projection/multi-mapping scenarios are not yet generated.

## Future Dialect Migration Plan

The generator architecture is designed to evolve into provider-specific SQL emitters (`SqlServer`, `PostgreSql`, `MySql`) while preserving the same attribute contracts.
