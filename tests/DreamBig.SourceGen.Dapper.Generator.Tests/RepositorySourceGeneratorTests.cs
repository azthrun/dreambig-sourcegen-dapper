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
        generated.ShouldContain("services.AddScoped<global::Demo.ICustomerRepository, global::Demo.CustomerRepositoryGenerated>();");
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
        generated.ShouldContain("services.AddScoped<global::Demo.IAppUnitOfWork, global::Demo.AppUnitOfWorkGenerated>();");
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
