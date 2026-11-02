namespace Meridian.Api.Shared.Data.Entities;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public Role Role { get; set; }
}
