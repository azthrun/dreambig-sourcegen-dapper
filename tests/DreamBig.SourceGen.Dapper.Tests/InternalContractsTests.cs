using System.Data;
using DreamBig.SourceGen.Dapper.Internal;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

public sealed class InternalContractsTests
{
    [Fact]
    public void GeneratedTransactionContext_ShouldExposeCurrentTransaction()
    {
        var context = new TestTransactionContext();
        context.CurrentTransaction.ShouldBeNull();
    }

    private sealed class TestTransactionContext : IGeneratedTransactionContext
    {
        public IDbTransaction? CurrentTransaction => null;
    }
}
