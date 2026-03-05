using System.Collections.Generic;
using DreamBig.SourceGen.Dapper.Internal;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

public sealed class GeneratedProcedureResultTests
{
    [Fact]
    public void ShouldStoreRowsAndOutputs()
    {
        var rows = new List<int> { 1, 2, 3 };
        var outputs = new Dictionary<string, object?>
        {
            ["@total"] = 3,
            ["@status"] = "ok",
        };

        var result = new GeneratedProcedureResult<int>(rows, outputs);

        result.Rows.Count.ShouldBe(3);
        result.Rows[0].ShouldBe(1);
        result.OutputValues.Count.ShouldBe(2);
        result.OutputValues["@total"].ShouldBe(3);
        result.OutputValues["@status"].ShouldBe("ok");
    }
}
