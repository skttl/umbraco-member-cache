namespace Umbraco.Community.MemberCache;

/// <summary>Selects member IDs whose raw data should be cached before startup completes.</summary>
public interface IMemberSeedProvider
{
    /// <summary>Enumerates IDs without constructing published models.</summary>
    IAsyncEnumerable<int> GetMemberIdsAsync(CancellationToken cancellationToken = default);
}
