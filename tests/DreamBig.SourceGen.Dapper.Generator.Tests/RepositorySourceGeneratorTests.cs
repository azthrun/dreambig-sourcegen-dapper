using System.Collections.Immutable;
using System.Reflection;
using DreamBig.SourceGen.Dapper.Generator.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

public sealed class CustomerSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

[DbRepository]
public interface ICustomerReadRepository
{
    [DbQuery(From = "[dbo].[Customers] c", Where = "c.[IsActive] = @isActive", OrderBy = "c.[Id] DESC")]
    [DbJoin(JoinType.Left, "[dbo].[Orders] o", "c.Id", "o.CustomerId")]
    Task<IEnumerable<CustomerSummary>> QueryActive(bool isActive, CancellationToken cancellationToken);

    [DbStoredProcedure("usp_customer_summary", Schema = "dbo")]
    Task<GeneratedProcedureResult<CustomerSummary>> GetSummary([DbParam("@customerId", DbType = DbType.Int32)] int customerId, [DbParam("@total", Direction = DbParamDirection.Output)] int total, CancellationToken cancellationToken);
}
""";

        var result = RunGenerator(source);
        result.Diagnostics.ShouldBeEmpty();

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));

        generated.ShouldContain("LEFT OUTER JOIN [dbo].[Orders] o ON c.Id = o.CustomerId");
        generated.ShouldContain("[dbo].[usp_customer_summary]");
        generated.ShouldContain("System.Data.ParameterDirection.Output");
        generated.ShouldContain("QueryStoredProcedureGeneratedAsync<");
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

    private static GeneratorResult RunGenerator(string source)
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
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var runResult = driver.GetRunResult();

        return new GeneratorResult(
            runResult.Diagnostics,
            runResult.GeneratedTrees.ToImmutableArray());
    }

    private sealed class GeneratorResult
    {
        public GeneratorResult(ImmutableArray<Diagnostic> diagnostics, ImmutableArray<SyntaxTree> generatedTrees)
        {
            Diagnostics = diagnostics;
            GeneratedTrees = generatedTrees;
        }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public ImmutableArray<SyntaxTree> GeneratedTrees { get; }
    }
}
