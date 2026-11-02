using Meridian.Api.Shared.Data.Entities;

namespace Meridian.Api.Features.Users;

public class UserDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required Role Role { get; init; }

    public static UserDto From(User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Role = user.Role,
    };
}
