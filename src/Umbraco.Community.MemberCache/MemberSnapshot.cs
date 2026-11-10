using System.Text.Json;

namespace Umbraco.Community.MemberCache;

// Only public read-model data belongs in this transport; never add identity credentials or published objects.
internal sealed record MemberSnapshot
{
    public int Id { get; init; }
    public Guid Key { get; init; }
    public int TypeId { get; init; }
    public string TypeAlias { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public DateTime CreateDate { get; init; }
    public DateTime UpdateDate { get; init; }
    public int CreatorId { get; init; }
    public int SortOrder { get; init; }
    public int ParentId { get; init; } = -1;
    public int Level { get; init; } = 1;
    public string? Path { get; init; }
    public bool IsApproved { get; init; }
    public bool IsLockedOut { get; init; }
    public DateTime? LastLoginDate { get; init; }
    public DateTime? LastLockoutDate { get; init; }
    public DateTime? LastPasswordChangeDate { get; init; }
    public Dictionary<string, JsonElement> Values { get; init; } = [];
}
