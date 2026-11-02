namespace Meridian.Api.Shared.Data.Entities;

public class MemberState
{
    public string CountryCode { get; set; } = null!;
    public string CountryName { get; set; } = null!;
    public DateOnly MemberFrom { get; set; }
    public DateOnly? MemberTo { get; set; }
}
