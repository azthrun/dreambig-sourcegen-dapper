using System;
using System.Collections;
using System.Data;
using Dapper;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Extensions;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

#pragma warning disable CS8767
public sealed class GeneratedDapperExtensionsGuardsTests
{
    [Fact]
    public void BuildParameters_ShouldUseMetadataNameInsteadOfTupleName()
    {
        var metadata = new DbParamAttribute("@actualName");

        var parameters = GeneratedDapperExtensions.BuildParameters(
        [
            ("ignoredName", 42, metadata),
        ]);

        parameters.ParameterNames.ShouldContain("actualName");
        parameters.ParameterNames.ShouldNotContain("ignoredName");
    }

    [Fact]
    public void QueryGeneratedAsync_ShouldThrowForNullConnection()
    {
        Should.Throw<ArgumentNullException>(() => GeneratedDapperExtensions.QueryGeneratedAsync<int>(null!, "SELECT 1"));
    }

    [Fact]
    public void ExecuteGeneratedAsync_ShouldThrowForNullConnection()
    {
        Should.Throw<ArgumentNullException>(() => GeneratedDapperExtensions.ExecuteGeneratedAsync(null!, "SELECT 1"));
    }

    [Fact]
    public void QueryStoredProcedureGenerated_ShouldThrowForNullConnection()
    {
        var parameters = new DynamicParameters();

        Should.Throw<ArgumentNullException>(() => GeneratedDapperExtensions.QueryStoredProcedureGenerated<int>(
            null!,
            "usp_test",
            parameters,
            Array.Empty<string>()));
    }

    [Fact]
    public void QueryStoredProcedureGenerated_ShouldThrowForNullParameters()
    {
        var connection = new FakeDbConnection();

        Should.Throw<ArgumentNullException>(() => GeneratedDapperExtensions.QueryStoredProcedureGenerated<int>(
            connection,
            "usp_test",
            null!,
            Array.Empty<string>()));
    }

    [Fact]
    public void QueryStoredProcedureGeneratedAsync_ShouldThrowForNullConnection()
    {
        var parameters = new DynamicParameters();

        Should.Throw<ArgumentNullException>(() => GeneratedDapperExtensions.QueryStoredProcedureGeneratedAsync<int>(
            null!,
            "usp_test",
            parameters,
            Array.Empty<string>()));
    }

    [Fact]
    public void QueryStoredProcedureGeneratedAsync_ShouldThrowForNullParameters()
    {
        var connection = new FakeDbConnection();

        Should.Throw<ArgumentNullException>(() => GeneratedDapperExtensions.QueryStoredProcedureGeneratedAsync<int>(
            connection,
            "usp_test",
            null!,
            Array.Empty<string>()));
    }

    private sealed class FakeDbConnection : IDbConnection
    {
        private string _connectionString = string.Empty;

        public string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public int ConnectionTimeout => 0;

        public string Database => "Fake";

        public ConnectionState State => ConnectionState.Closed;

        public IDbTransaction BeginTransaction()
            => throw new NotSupportedException();

        public IDbTransaction BeginTransaction(IsolationLevel il)
            => throw new NotSupportedException();

        public void ChangeDatabase(string databaseName)
            => throw new NotSupportedException();

        public void Close()
        {
        }

        public IDbCommand CreateCommand()
            => throw new NotSupportedException();

        public void Open()
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
#pragma warning restore CS8767
