using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.MemberCache;

/// <summary>Looks up published member read models, including ModelsBuilder models when available.</summary>
/// <remarks>These cached models must not be used to make authentication or authorization decisions.</remarks>
public interface IPublishedMemberLookup
{
    Task<IPublishedContent?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IPublishedContent?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPublishedContent>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IPublishedContent>> GetByUsernamesAsync(IEnumerable<string> usernames, CancellationToken cancellationToken = default);
}

internal interface IRawMemberSeeder
{
    Task SeedAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
}
