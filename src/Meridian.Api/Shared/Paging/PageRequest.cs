using FluentValidation;

namespace Meridian.Api.Shared.Paging;

public class PageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public class PageRequestValidator : AbstractValidator<PageRequest>
{
    public const int MaxPageSize = 100;

    /// <summary>Keeps (page - 1) * pageSize inside an int.</summary>
    public const int MaxPage = 1_000_000;

    public PageRequestValidator()
    {
        RuleFor(r => r.Page).InclusiveBetween(1, MaxPage);
        RuleFor(r => r.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
