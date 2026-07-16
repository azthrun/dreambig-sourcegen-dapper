using DreamBig.SourceGen.Dapper.Internal;
using Shouldly;
using Xunit;

namespace DreamBig.SourceGen.Dapper.Tests;

public sealed class PagedResultTests
{
    [Fact]
    public void ShouldExposeItemsAndPagingMetadata()
    {
        var items = new[] { "a", "b" };

        var result = new PagedResult<string>(items, totalCount: 42, skip: 10, take: 2);

        result.Items.ShouldBe(items);
        result.TotalCount.ShouldBe(42);
        result.Skip.ShouldBe(10);
        result.Take.ShouldBe(2);
    }

    [Fact]
    public void ShouldThrowWhenItemsAreNull()
    {
        Should.Throw<ArgumentNullException>(() => new PagedResult<string>(null!, totalCount: 0, skip: 0, take: 0));
    }
}
