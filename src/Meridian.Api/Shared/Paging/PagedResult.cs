using Microsoft.EntityFrameworkCore;

namespace Meridian.Api.Shared.Paging;

public class PagedResult<T>
{
    public required List<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int Total { get; init; }

    /// <summary>The query must already be ordered.</summary>
    public static async Task<PagedResult<T>> From(IQueryable<T> query, PageRequest request, CancellationToken ct)
    {
        return new PagedResult<T>
        {
            Total = await query.CountAsync(ct),
            Items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct),
            Page = request.Page,
            PageSize = request.PageSize,
        };
    }
}
