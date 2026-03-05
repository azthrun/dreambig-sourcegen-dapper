using System;
using System.Data;
using DreamBig.SourceGen.Dapper.Attributes;
using DreamBig.SourceGen.Dapper.Extensions;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

public sealed class GeneratedDapperExtensionsTests
{
    [Fact]
    public void ShouldBuildStoredProcedureParameters()
    {
        var metadata = new DbParamAttribute("@total")
        {
            Direction = DbParamDirection.Output,
            DbType = DbType.Int32,
            Size = 4,
        };

        var parameters = GeneratedDapperExtensions.BuildParameters(
        [
            ("@customerId", 42, null),
            ("@total", 0, metadata),
        ]);

        parameters.ParameterNames.ShouldContain("customerId");
        parameters.ParameterNames.ShouldContain("total");
    }

    [Fact]
    public void ShouldThrowForNullConnectionOnQuery()
    {
        Action act = () => GeneratedDapperExtensions.QueryGenerated<int>(null!, "SELECT 1");
        Should.Throw<ArgumentNullException>(act);
    }

    [Fact]
    public void ShouldThrowForNullConnectionOnExecute()
    {
        Action act = () => GeneratedDapperExtensions.ExecuteGenerated(null!, "UPDATE dbo.Customers SET Name = @name", new { name = "x" });
        Should.Throw<ArgumentNullException>(act);
    }
}
