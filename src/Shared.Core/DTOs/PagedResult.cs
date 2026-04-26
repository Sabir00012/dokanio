namespace Shared.Core.DTOs;

/// <summary>
/// Generic paginated result wrapper used by repository and service query methods.
/// Carries both the current page of items and the metadata needed to build navigation controls.
/// </summary>
/// <typeparam name="T">The type of items in the result set.</typeparam>
public class PagedResult<T>
{
    /// <summary>Items on the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>Zero-based page index.</summary>
    public int Page { get; init; }

    /// <summary>Maximum number of items per page.</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of items across all pages.</summary>
    public int TotalCount { get; init; }

    /// <summary>Total number of pages.</summary>
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;

    /// <summary>Whether there is a page after the current one.</summary>
    public bool HasNextPage => Page < TotalPages - 1;

    /// <summary>Whether there is a page before the current one.</summary>
    public bool HasPreviousPage => Page > 0;

    /// <summary>Creates an empty result for the given page parameters.</summary>
    public static PagedResult<T> Empty(int page = 0, int pageSize = 20) =>
        new() { Page = page, PageSize = pageSize, TotalCount = 0 };

    /// <summary>Creates a <see cref="PagedResult{T}"/> from a pre-fetched list and total count.</summary>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int totalCount, int page, int pageSize) =>
        new() { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize };
}
