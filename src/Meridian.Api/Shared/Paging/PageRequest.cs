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

    public PageRequestValidator()
    {
        RuleFor(r => r.Page).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
