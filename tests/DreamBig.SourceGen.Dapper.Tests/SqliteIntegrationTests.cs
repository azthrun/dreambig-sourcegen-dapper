using Dapper;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Internal;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

[DbTable("Customers")]
public sealed class SqliteCustomer
{
    [DbKey]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

[DbRepository]
public interface ISqliteCustomerRepository
{
    [DbOperation(DbOperationKind.Insert, ReturnIdentity = true)]
    Task<int> InsertCustomer(SqliteCustomer entity, CancellationToken cancellationToken);

    Task<SqliteCustomer?> GetCustomerById(int id, CancellationToken cancellationToken);

    Task<IEnumerable<SqliteCustomer>> GetAllCustomers(CancellationToken cancellationToken);

    Task<SqliteCustomer?> GetCustomerByEmail(string email, CancellationToken cancellationToken);

    Task<int> UpdateCustomer(SqliteCustomer entity, CancellationToken cancellationToken);

    Task<int> CountSqliteCustomers(CancellationToken cancellationToken);

    Task<bool> ExistsSqliteCustomerByEmail(string email, CancellationToken cancellationToken);

    Task<PagedResult<SqliteCustomer>> GetPageCustomers(int skip, int take, CancellationToken cancellationToken);

    Task<int> DeleteSqliteCustomer(int id, CancellationToken cancellationToken);
}

public sealed class SqliteIntegrationTests
{
    [Fact]
    public async Task ShouldRoundTripGeneratedRepositoryAgainstInMemorySqlite()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "CREATE TABLE Customers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Email TEXT NOT NULL);");

        using var transaction = connection.BeginTransaction();
        ISqliteCustomerRepository repository = new SqliteCustomerRepositoryGenerated(connection, transaction);

        var adaId = await repository.InsertCustomer(
            new SqliteCustomer { Name = "Ada", Email = "ada@example.com" },
            CancellationToken.None);
        adaId.ShouldBe(1);

        var graceId = await repository.InsertCustomer(
            new SqliteCustomer { Name = "Grace", Email = "grace@example.com" },
            CancellationToken.None);
        graceId.ShouldBe(2);

        var byId = await repository.GetCustomerById(adaId, CancellationToken.None);
        byId.ShouldNotBeNull();
        byId.Name.ShouldBe("Ada");

        var byEmail = await repository.GetCustomerByEmail("grace@example.com", CancellationToken.None);
        byEmail.ShouldNotBeNull();
        byEmail.Id.ShouldBe(graceId);

        byId.Name = "Ada Lovelace";
        (await repository.UpdateCustomer(byId, CancellationToken.None)).ShouldBe(1);
        (await repository.GetCustomerById(adaId, CancellationToken.None))!.Name.ShouldBe("Ada Lovelace");

        (await repository.GetAllCustomers(CancellationToken.None)).Count().ShouldBe(2);
        (await repository.CountSqliteCustomers(CancellationToken.None)).ShouldBe(2);
        (await repository.ExistsSqliteCustomerByEmail("ada@example.com", CancellationToken.None)).ShouldBeTrue();
        (await repository.ExistsSqliteCustomerByEmail("nobody@example.com", CancellationToken.None)).ShouldBeFalse();

        var page = await repository.GetPageCustomers(1, 5, CancellationToken.None);
        page.TotalCount.ShouldBe(2);
        page.Items.Count.ShouldBe(1);
        page.Items[0].Name.ShouldBe("Grace");

        (await repository.DeleteSqliteCustomer(adaId, CancellationToken.None)).ShouldBe(1);
        (await repository.CountSqliteCustomers(CancellationToken.None)).ShouldBe(1);
    }

    [Fact]
    public void GeneratedSqlShouldUseSqliteDialect()
    {
        SqliteCustomerRepositoryGenerated.Sql.GetAllCustomers.ShouldBe(
            "SELECT \"Id\" AS \"Id\", \"Name\" AS \"Name\", \"Email\" AS \"Email\" FROM \"Customers\";");
        SqliteCustomerRepositoryGenerated.Sql.InsertCustomer.ShouldContain("RETURNING \"Id\";");
        SqliteCustomerRepositoryGenerated.Sql.GetPageCustomers.ShouldContain("LIMIT @take OFFSET @skip;");
        SqliteCustomerRepositoryGenerated.Sql.CountSqliteCustomers.ShouldBe("SELECT COUNT(*) FROM \"Customers\";");
    }
}
