namespace DreamBig.SourceGen.Dapper.Internal;

/// <summary>
/// Wraps one page of rows together with the total row count for pager rendering.
/// </summary>
/// <typeparam name="T">Row type.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagedResult{T}"/> class.
    /// </summary>
    /// <param name="items">Rows on this page.</param>
    /// <param name="totalCount">Total row count across all pages.</param>
    /// <param name="skip">Number of rows skipped before this page.</param>
    /// <param name="take">Requested page size.</param>
    public PagedResult(IReadOnlyList<T> items, long totalCount, int skip, int take)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        TotalCount = totalCount;
        Skip = skip;
        Take = take;
    }

    /// <summary>
    /// Gets the rows on this page.
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the total row count across all pages.
    /// </summary>
    public long TotalCount { get; }

    /// <summary>
    /// Gets the number of rows skipped before this page.
    /// </summary>
    public int Skip { get; }

    /// <summary>
    /// Gets the requested page size.
    /// </summary>
    public int Take { get; }
}
