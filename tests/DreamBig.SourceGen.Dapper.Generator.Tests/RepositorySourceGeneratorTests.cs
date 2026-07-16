using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using DreamBig.SourceGen.Dapper.Generator.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Generator.Tests;

public sealed class RepositorySourceGeneratorTests
{
    [Fact]
    public void ShouldGenerateCrudImplementation()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

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
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("INSERT INTO [dbo].[Customers]");
        generated.ShouldContain("UPDATE [dbo].[Customers] SET");
        generated.ShouldContain("DELETE FROM [dbo].[Customers]");
        generated.ShouldContain("SELECT [Id] AS [Id], [full_name] AS [Name], [Email] AS [Email] FROM [dbo].[Customers]");
        generated.ShouldContain("public async global::System.Threading.Tasks.Task<int> InsertCustomer");
        generated.ShouldContain("public async global::System.Threading.Tasks.Task<global::Demo.Customer?> GetByIdCustomer");
        generated.ShouldContain("public static IServiceCollection AddDreamBigDapperGenerated");
        generated.ShouldContain("services.TryAddScoped<global::Demo.ICustomerRepository, global::Demo.CustomerRepositoryGenerated>();");
    }

    [Fact]
    public void ShouldGeneratePostgreSqlPagingAndSchemaDefaults()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<IEnumerable<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source, "PostgreSql");
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("FROM \\\"public\\\".\\\"Customers\\\"");
        generated.ShouldContain("LIMIT @take OFFSET @skip");
    }

    [Fact]
    public void ShouldStripInterfacePrefixFromGeneratedRepository()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("CustomerOrders")]
public sealed class CustomerOrder
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerOrderRepository
{
    Task<int> InsertCustomerOrder(CustomerOrder entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public sealed partial class CustomerOrderRepositoryGenerated");
        generated.ShouldNotContain("ICustomerOrderRepositoryGenerated");
    }

    [Fact]
    public void ShouldDisablePostgreSqlCaseSensitiveQuotingWhenConfigured()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository(CaseSensitive = false)]
public interface ICustomerRepository
{
    Task<IEnumerable<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source, "PostgreSql");
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("FROM public.Customers");
        generated.ShouldContain("SELECT Id AS Id, Name AS Name");
        generated.ShouldNotContain("\"public\"");
        generated.ShouldNotContain("\"Id\"");
    }

    [Fact]
    public void ShouldGenerateJoinAndStoredProcedureMethods()
    {
        const string source = """
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Internal;

namespace Demo;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

[DbTable("Orders", Schema = "dbo")]
public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbQuery(From = "Customers", Schema = "dbo")]
    [DbJoin(JoinType = JoinType.Left, JoinTableA = typeof(Customer), JoinTableB = typeof(Order), JoinColumnA = "Id", JoinColumnB = "CustomerId", AliasA = "customers", AliasB = "orders", SchemaB = "sales", Where = "customers.IsActive = @isActive", OrderBy = "customers.Id", OrderByDirection = OrderByDirection.Desc)]
    Task<IEnumerable<Customer>> QueryActive(bool isActive, CancellationToken cancellationToken);

    [DbStoredProcedure("usp_customer_summary", Schema = "dbo")]
    Task<GeneratedProcedureResult<Customer>> GetSummary([DbParam("@customerId", DbType = DbType.Int32)] int customerId, [DbParam("@total", Direction = DbParamDirection.Output)] int total, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("LEFT OUTER JOIN [sales].[Orders] orders ON customers.[Id] = orders.[CustomerId]");
        generated.ShouldContain("FROM [dbo].[Customers] customers");
        generated.ShouldContain("WHERE (customers.[IsActive] = @isActive)");
        generated.ShouldContain("ORDER BY customers.[Id] DESC");
        generated.ShouldContain("[dbo].[usp_customer_summary]");
        generated.ShouldContain("System.Data.ParameterDirection.Output");
        generated.ShouldContain("QueryStoredProcedureGeneratedAsync<");
    }

    [Fact]
    public void ShouldReportInvalidJoinColumnDiagnostic()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbTable("Orders", Schema = "dbo")]
public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbQuery(From = "[dbo].[Customers]")]
    [DbJoin(JoinType = JoinType.Left, JoinTableA = typeof(Customer), JoinTableB = typeof(Order), JoinColumnA = "Missing", JoinColumnB = "CustomerId")]
    Task<IEnumerable<Customer>> QueryActive(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD015").ShouldBeTrue();
    }

    [Fact]
    public void ShouldRespectQualifiedFromWithoutApplyingSchema()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", Schema = "sales")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbQuery(From = "[dbo].[Customers]", Schema = "sales", OrderBy = "Id")]
    Task<IEnumerable<Customer>> QueryActive(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("FROM [dbo].[Customers] customers");
    }

    [Fact]
    public void ShouldUseReadableAliasesForJoinOverride()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbQuery(From = "Customers", Schema = "dbo", Join = "INNER JOIN [dbo].[Orders] orders ON customers.Id = orders.CustomerId", Where = "customers.Id = @id")]
    Task<IEnumerable<Customer>> QueryActive(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("FROM [dbo].[Customers] customers INNER JOIN [dbo].[Orders] orders ON customers.Id = orders.CustomerId WHERE (customers.[Id] = @id)");
        generated.ShouldNotContain(" t0 ");
    }

    [Fact]
    public void ShouldReportAmbiguousBareWhereReferenceAcrossJoinedTables()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbTable("Orders", Schema = "dbo")]
public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbJoin(JoinType = JoinType.Left, JoinTableA = typeof(Customer), JoinTableB = typeof(Order), JoinColumnA = "Id", JoinColumnB = "CustomerId", Where = "Id = @id")]
    Task<IEnumerable<Customer>> QueryActive(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD018").ShouldBeTrue();
    }

    [Fact]
    public void ShouldGenerateChainedJoinsWithReadableAliases()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", Schema = "dbo")]
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbTable("Orders", Schema = "dbo")]
public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
}

[DbTable("OrderLines", Schema = "dbo")]
public sealed class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbJoin(JoinType = JoinType.Left, JoinTableA = typeof(Customer), JoinTableB = typeof(Order), JoinColumnA = "Id", JoinColumnB = "CustomerId", AliasA = "customers", AliasB = "orders")]
    [DbJoin(JoinType = JoinType.Left, JoinTableA = typeof(Order), JoinTableB = typeof(OrderLine), JoinColumnA = "Id", JoinColumnB = "OrderId", AliasA = "orders", AliasB = "orderLines", Where = "orderLines.OrderId = @orderId")]
    Task<IEnumerable<Customer>> QueryActive(int orderId, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("FROM [dbo].[Customers] customers");
        generated.ShouldContain("LEFT OUTER JOIN [dbo].[Orders] orders ON customers.[Id] = orders.[CustomerId]");
        generated.ShouldContain("LEFT OUTER JOIN [dbo].[OrderLines] orderLines ON orders.[Id] = orderLines.[OrderId]");
        generated.ShouldContain("WHERE (orderLines.[OrderId] = @orderId)");
    }

    [Fact]
    public void ShouldRequireExplicitAliasesWhenRepeatedTablesWouldCollide()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Employees", Schema = "dbo")]
public sealed class Employee
{
    public int Id { get; set; }
    public int ManagerId { get; set; }
}

[DbRepository]
public interface IEmployeeReadRepository
{
    [DbJoin(JoinType = JoinType.Left, JoinTableA = typeof(Employee), JoinTableB = typeof(Employee), JoinColumnA = "ManagerId", JoinColumnB = "Id")]
    Task<IEnumerable<Employee>> QueryManagers(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD019").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportMissingKeyDiagnostic()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> UpdateCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD001").ShouldBeTrue();
    }

    [Fact]
    public void ShouldUseDbTablePrimaryKeyWithoutDbKeyAttribute()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", PrimaryKey = "Id")]
public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> UpdateCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD001").ShouldBeFalse();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("WHERE [Id] = @Id;");
    }

    [Fact]
    public void ShouldReportAsyncReturnTypeRequiredDiagnostic()
    {
        const string source = """
using System.Threading;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", PrimaryKey = "Id")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    int UpdateCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD006").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportCancellationTokenRequiredDiagnostic()
    {
        const string source = """
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", PrimaryKey = "Id")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> UpdateCustomer(Customer entity);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD007").ShouldBeTrue();
    }

    [Fact]
    public void ShouldPreserveNonNullableReturnTypeAsDefined()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", PrimaryKey = "Id")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<Customer> GetByIdCustomer(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public async global::System.Threading.Tasks.Task<global::Demo.Customer> GetByIdCustomer");
        generated.ShouldNotContain("public async global::System.Threading.Tasks.Task<global::Demo.Customer?> GetByIdCustomer");
    }

    [Fact]
    public void ShouldGenerateUnitOfWorkImplementation()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", PrimaryKey = "Id")]
public sealed class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbTable("Orders")]
public sealed class Order
{
    [DbKey]
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
    Task<IEnumerable<Customer>> GetAllCustomers(CancellationToken cancellationToken);
}

[DbRepository]
public interface IOrderRepository
{
    Task<int> DeleteOrder(int id, CancellationToken cancellationToken);
}

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    ICustomerRepository Customers { get; }
    IOrderRepository Orders { get; }
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public sealed partial class AppUnitOfWorkGenerated");
        generated.ShouldContain("BeginTransactionAsync");
        generated.ShouldContain("CommitAsync");
        generated.ShouldContain("RollbackAsync");
        generated.ShouldContain("public global::Demo.ICustomerRepository Customers => _Customers ??= new CustomerRepositoryGenerated");
        generated.ShouldContain("public global::Demo.IOrderRepository Orders => _Orders ??= new OrderRepositoryGenerated");
        generated.ShouldContain("public CustomerRepositoryGenerated(");
        generated.ShouldContain("transactionContext)");
        generated.ShouldContain("EnsureTransactionRequired(\"InsertCustomer\")");
        generated.ShouldContain("EnsureTransactionRequired(\"DeleteOrder\")");
        generated.ShouldContain("var transaction = ResolveTransaction();");
        generated.ShouldContain("services.TryAddScoped<global::Demo.IAppUnitOfWork, global::Demo.AppUnitOfWorkGenerated>();");
    }

    [Fact]
    public void ShouldReportUnitOfWorkMemberInvalidDiagnostic()
    {
        const string source = """
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    int InvalidMethod();
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD008").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportUnitOfWorkRepositoryTypeInvalidDiagnostic()
    {
        const string source = """
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

public interface INotRepository
{
}

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    INotRepository InvalidRepository { get; }
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD009").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportUnitOfWorkContainsNoRepositoriesDiagnostic()
    {
        const string source = """
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbUnitOfWork]
public interface IAppUnitOfWork
{
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD010").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportUnitOfWorkRepositoryGenerationFailedDiagnostic()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> UpdateCustomer(Customer entity, CancellationToken cancellationToken);
}

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    ICustomerRepository Customers { get; }
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD011").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportUnitOfWorkDuplicatePropertyDiagnostic()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers", PrimaryKey = "Id")]
public sealed class Customer
{
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> DeleteCustomer(int id, CancellationToken cancellationToken);
}

[DbUnitOfWork]
public interface IAppUnitOfWork
{
    ICustomerRepository Customers { get; }
    ICustomerRepository customers { get; }
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD012").ShouldBeTrue();
    }

    [Fact]
    public void ShouldGenerateSameNamedRepositoriesAcrossNamespaces()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace DemoA
{
    [DbTable("Customers")]
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
    }
}

namespace DemoB
{
    [DbTable("Customers")]
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
    }
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("services.TryAddScoped<global::DemoA.ICustomerRepository, global::DemoA.CustomerRepositoryGenerated>();");
        generated.ShouldContain("services.TryAddScoped<global::DemoB.ICustomerRepository, global::DemoB.CustomerRepositoryGenerated>();");
    }

    [Fact]
    public void ShouldNotRewriteColumnNamesInsideStringLiterals()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    [DbColumn("status_code")]
    public string Status { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    [DbQuery(Where = "Status = 'Status'")]
    Task<IEnumerable<Customer>> QueryActive(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("[status_code] = 'Status'");
    }

    [Fact]
    public void ShouldBindDeleteEntityParameterToKeyColumn()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> DeleteAsync(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public const string DeleteAsync = \"DELETE FROM [dbo].[Customers] WHERE [Id] = @Id;\";");
        generated.ShouldContain("ExecuteGeneratedAsync(Sql.DeleteAsync, entity, transaction");
    }

    [Fact]
    public void ShouldUseDeclaredGetPageParameterNames()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<IEnumerable<Customer>> GetPageCustomers(int offset, int pageSize, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY");
        generated.ShouldContain("new { offset, pageSize }");
    }

    [Fact]
    public void ShouldIncludeClientAssignedKeyInInsert()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey(Generated = false)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("INSERT INTO [dbo].[Customers] ([Id], [Name]) VALUES (@Id, @Name)");
    }

    [Fact]
    public void ShouldReportEntityWithNoWritableColumns()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD023").ShouldBeTrue();
    }

    [Fact]
    public void ShouldReportUnresolvedDeleteEntity()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbRepository]
public interface IOrderRepository
{
    Task<int> DeleteOrder(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD022").ShouldBeTrue();
    }

    [Fact]
    public void ShouldSupportValueTaskReturnTypes()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    ValueTask<IEnumerable<Customer>> GetAllCustomers(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public async global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<global::Demo.Customer>> GetAllCustomers");
    }

    [Fact]
    public void ShouldWarnOnAmbiguousOperationName()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertOrUpdateCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD024" && d.Severity == DiagnosticSeverity.Warning).ShouldBeTrue();
    }

    [Fact]
    public void ShouldBindGetPageParametersByNameRegardlessOfOrder()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<IEnumerable<Customer>> GetPageCustomers(int take, int skip, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY");
    }

    [Fact]
    public void ShouldReportDiagnosticWhenGetPageParameterNamesAreUnrecognized()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<IEnumerable<Customer>> GetPageCustomers(int first, int second, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD025" && d.Severity == DiagnosticSeverity.Error).ShouldBeTrue();
    }

    [Fact]
    public void ShouldHonorOrderByOnGetPage()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    [DbColumn("full_name")]
    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    [DbQuery(OrderBy = "Name", OrderByDirection = OrderByDirection.Desc)]
    Task<IEnumerable<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("ORDER BY [full_name] DESC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY");
    }

    [Fact]
    public void ShouldReportDiagnosticWhenGetPageOrderByColumnIsUnknown()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    [DbQuery(OrderBy = "MissingColumn")]
    Task<IEnumerable<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD016" && d.Severity == DiagnosticSeverity.Error).ShouldBeTrue();
    }

    [Fact]
    public void ShouldEmitSqlConstantsForGeneratedMethods()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

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
    Task<Customer?> GetByIdCustomer(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public static class Sql");
        generated.ShouldContain("public const string InsertCustomer = \"INSERT INTO [dbo].[Customers] ([Name]) VALUES (@Name);\";");
        generated.ShouldContain("public const string GetByIdCustomer = \"SELECT [Id] AS [Id], [Name] AS [Name] FROM [dbo].[Customers] WHERE [Id] = @id;\";");
        generated.ShouldContain("ExecuteGeneratedAsync(Sql.InsertCustomer, entity, transaction");
        generated.ShouldContain("QueryGeneratedAsync<global::Demo.Customer?>(Sql.GetByIdCustomer, new { id }, transaction");
    }

    [Fact]
    public void ShouldReportDiagnosticForUnknownQueryParameter()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public bool IsActive { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    [DbQuery(From = "Customers", Where = "IsActive = @isActiv")]
    Task<IEnumerable<Customer>> QueryActive(bool isActive, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD026" && d.Severity == DiagnosticSeverity.Error).ShouldBeTrue();
    }

    [Fact]
    public void ShouldNotReportKnownQueryParametersOrLiteralAtSigns()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    [DbQuery(From = "Customers", Where = "IsActive = @isActive AND Email <> 'admin@example.com'")]
    Task<IEnumerable<Customer>> QueryActive(bool isActive, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void ShouldSupportNaturalGetByIdNaming()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<Customer?> GetCustomerById(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("SELECT [Id] AS [Id], [Name] AS [Name] FROM [dbo].[Customers] WHERE [Id] = @id;");
        generated.ShouldContain("return rows.FirstOrDefault();");
    }

    [Fact]
    public void ShouldSupportExplicitOperationAttribute()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    [DbOperation(DbOperationKind.GetById)]
    Task<Customer?> Find(int id, CancellationToken cancellationToken);

    [DbOperation(DbOperationKind.Count, Entity = typeof(Customer))]
    Task<int> HowMany(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public const string Find = \"SELECT [Id] AS [Id], [Name] AS [Name] FROM [dbo].[Customers] WHERE [Id] = @id;\";");
        generated.ShouldContain("public const string HowMany = \"SELECT COUNT_BIG(*) FROM [dbo].[Customers];\";");
        generated.ShouldContain("return (int)rows.FirstOrDefault();");
    }

    [Fact]
    public void ShouldGenerateGetByPropertyConventions()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    [DbColumn("email_address")]
    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<Customer?> GetCustomerByEmail(string email, CancellationToken cancellationToken);
    Task<IEnumerable<Customer>> GetCustomersByEmailAndIsActive(string email, bool isActive, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("WHERE [email_address] = @email;");
        generated.ShouldContain("WHERE [email_address] = @email AND [IsActive] = @isActive;");
        generated.ShouldContain("new { email, isActive }");
    }

    [Fact]
    public void ShouldGenerateDeleteByPropertyConvention()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> DeleteCustomerByEmail(string email, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("DELETE FROM [dbo].[Customers] WHERE [Email] = @email;");
    }

    [Fact]
    public void ShouldGenerateCountAndExistsConventions()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public bool IsActive { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> CountCustomers(CancellationToken cancellationToken);
    Task<long> CountCustomersByIsActive(bool isActive, CancellationToken cancellationToken);
    Task<bool> ExistsCustomerByIsActive(bool isActive, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public const string CountCustomers = \"SELECT COUNT_BIG(*) FROM [dbo].[Customers];\";");
        generated.ShouldContain("SELECT COUNT_BIG(*) FROM [dbo].[Customers] WHERE [IsActive] = @isActive;");
        generated.ShouldContain("return (int)rows.FirstOrDefault();");
        generated.ShouldContain("return rows.FirstOrDefault() > 0;");
    }

    [Fact]
    public void ShouldReportDiagnosticForUnknownConventionProperty()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    Task<Customer?> GetCustomerByMissing(string missing, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD027" && d.Severity == DiagnosticSeverity.Error).ShouldBeTrue();
    }

    [Fact]
    public void ShouldGenerateInsertReturningIdentity()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    [DbOperation(DbOperationKind.Insert, ReturnIdentity = true)]
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("INSERT INTO [dbo].[Customers] ([Name]) OUTPUT INSERTED.[Id] VALUES (@Name);");
        generated.ShouldContain("QueryGeneratedAsync<int>(Sql.InsertCustomer, entity, transaction");
        generated.ShouldContain("return rows.FirstOrDefault();");
    }

    [Fact]
    public void ShouldGenerateInsertReturningIdentityForPostgreSql()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    [DbOperation(DbOperationKind.Insert, ReturnIdentity = true)]
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source, "PostgreSql");
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("VALUES (@Name) RETURNING \\\"Id\\\";");
    }

    [Fact]
    public void ShouldGeneratePagedResultWithTotalCount()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Internal;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<PagedResult<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY; SELECT COUNT_BIG(*) FROM [dbo].[Customers];");
        generated.ShouldContain("QueryPagedGeneratedAsync<global::Demo.Customer>(Sql.GetPageCustomers, new { skip, take }, skip, take, transaction");
    }

    [Fact]
    public void ShouldApplyRowVersionToWrites()
    {
        const string source = """
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DbRowVersion]
    [DbColumn("row_version")]
    public byte[] Version { get; set; } = System.Array.Empty<byte>();
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> UpdateCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> DeleteCustomer(Customer entity, CancellationToken cancellationToken);
    Task<int> DeleteCustomerById(int id, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("INSERT INTO [dbo].[Customers] ([Name]) VALUES (@Name);");
        generated.ShouldContain("UPDATE [dbo].[Customers] SET [Name] = @Name WHERE [Id] = @Id AND [row_version] = @Version;");
        generated.ShouldContain("public const string DeleteCustomer = \"DELETE FROM [dbo].[Customers] WHERE [Id] = @Id AND [row_version] = @Version;\";");
        generated.ShouldContain("public const string DeleteCustomerById = \"DELETE FROM [dbo].[Customers] WHERE [Id] = @id;\";");
    }

    [Fact]
    public void ShouldGenerateBatchInsertFromEnumerableParameter()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> InsertCustomers(IEnumerable<Customer> entities, CancellationToken cancellationToken);
    Task<int> UpdateCustomers(IReadOnlyList<Customer> entities, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("INSERT INTO [dbo].[Customers] ([Name]) VALUES (@Name);");
        generated.ShouldContain("ExecuteGeneratedAsync(Sql.InsertCustomers, entities, transaction");
        generated.ShouldContain("UPDATE [dbo].[Customers] SET [Name] = @Name WHERE [Id] = @Id;");
        generated.ShouldContain("ExecuteGeneratedAsync(Sql.UpdateCustomers, entities, transaction");
    }

    [Fact]
    public void ShouldGenerateInClauseForEnumerableFilterParameters()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerRepository
{
    Task<int> DeleteCustomersByIds(IEnumerable<int> ids, CancellationToken cancellationToken);
    Task<IEnumerable<Customer>> GetCustomersByIds(IReadOnlyList<int> ids, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("DELETE FROM [dbo].[Customers] WHERE [Id] IN @ids;");
        generated.ShouldContain("FROM [dbo].[Customers] WHERE [Id] IN @ids;");
    }

    [Fact]
    public void ShouldReportWarningForUnusedQueryParameter()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public bool IsActive { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    [DbQuery(From = "Customers", Where = "IsActive = @isActive")]
    Task<IEnumerable<Customer>> QueryActive(bool isActive, string unusedFilter, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD028"
            && d.Severity == DiagnosticSeverity.Warning
            && d.GetMessage().Contains("unusedFilter")).ShouldBeTrue();
    }

    [Fact]
    public void ShouldGenerateAsyncStreamMethods()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    IAsyncEnumerable<Customer> GetAllCustomers(CancellationToken cancellationToken);
    IAsyncEnumerable<Customer> GetCustomersByEmail(string email, CancellationToken cancellationToken);

    [DbQuery(From = "Customers", Where = "IsActive = @isActive")]
    IAsyncEnumerable<Customer> QueryActive(bool isActive, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("public global::System.Collections.Generic.IAsyncEnumerable<global::Demo.Customer> GetAllCustomers(");
        generated.ShouldNotContain("public async global::System.Collections.Generic.IAsyncEnumerable");
        generated.ShouldContain("return _connection.QueryStreamGenerated<global::Demo.Customer>(Sql.GetAllCustomers, transaction: transaction, cancellationToken: cancellationToken);");
        generated.ShouldContain("return _connection.QueryStreamGenerated<global::Demo.Customer>(Sql.GetCustomersByEmail, new { email }, transaction, cancellationToken: cancellationToken);");
        generated.ShouldContain("return _connection.QueryStreamGenerated<global::Demo.Customer>(Sql.QueryActive, new { isActive }, transaction, cancellationToken: cancellationToken);");
    }

    [Fact]
    public void ShouldRejectAsyncStreamForUnsupportedOperations()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

[DbTable("Customers")]
public sealed class Customer
{
    [DbKey]
    public int Id { get; set; }
}

[DbRepository]
public interface ICustomerRepository
{
    IAsyncEnumerable<Customer> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.Any(d => d.Id == "DBSGD002" && d.Severity == DiagnosticSeverity.Error).ShouldBeTrue();
    }

    [Fact]
    public void ShouldGenerateSqliteDialectSql()
    {
        const string source = """
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DreamBig.SourceGen.Dapper.Attributes;

namespace Demo;

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
    [DbOperation(DbOperationKind.Insert, ReturnIdentity = true)]
    Task<int> InsertCustomer(Customer entity, CancellationToken cancellationToken);
    Task<Customer?> GetCustomerById(int id, CancellationToken cancellationToken);
    Task<IEnumerable<Customer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);
    Task<int> CountCustomers(CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source, "Sqlite");
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        // SQLite has no schemas, so the explicit Schema = "dbo" is ignored.
        generated.ShouldContain("INSERT INTO \\\"Customers\\\" (\\\"Name\\\") VALUES (@Name) RETURNING \\\"Id\\\";");
        generated.ShouldContain("FROM \\\"Customers\\\" WHERE \\\"Id\\\" = @id;");
        generated.ShouldContain("LIMIT @take OFFSET @skip;");
        generated.ShouldContain("SELECT COUNT(*) FROM \\\"Customers\\\";");
    }

    private static GeneratorResult RunGenerator(string source, string? dialect = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .Select(static assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .Concat(
            [
                MetadataReference.CreateFromFile(typeof(object).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(DreamBig.SourceGen.Dapper.Attributes.DbRepositoryAttribute).Assembly.Location),
            ])
            .Distinct()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new RepositorySourceGenerator();
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(dialect);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], optionsProvider: optionsProvider);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var runResult = driver.GetRunResult();

        return new GeneratorResult(
            runResult.Diagnostics,
            runResult.GeneratedTrees.ToImmutableArray());
    }

    private sealed class GeneratorResult(ImmutableArray<Diagnostic> diagnostics, ImmutableArray<SyntaxTree> generatedTrees)
    {
        public ImmutableArray<Diagnostic> Diagnostics { get; } = diagnostics;

        public ImmutableArray<SyntaxTree> GeneratedTrees { get; } = generatedTrees;
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(string? dialect)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(dialect))
            {
                options["build_property.DreamBigDapperDialect"] = dialect!;
            }

            _globalOptions = new TestAnalyzerConfigOptions(options);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;
    }

    private sealed class TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> options) : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _options = options;

        public override bool TryGetValue(string key, out string value)
        {
            if (_options.TryGetValue(key, out var found))
            {
                value = found ?? string.Empty;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
