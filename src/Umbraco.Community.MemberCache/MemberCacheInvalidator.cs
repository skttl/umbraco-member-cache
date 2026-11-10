namespace Umbraco.Community.MemberCache;

internal sealed class MemberCacheInvalidator(PublishedMemberLookup lookup)
{
    public Task InvalidateMembersAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();
        return lookup.InvalidateAsync(ids.Where(id => id > 0).Distinct().ToArray(), cancellationToken);
    }

    public Task InvalidateAllAsync(CancellationToken cancellationToken = default) => lookup.InvalidateAsync(null, cancellationToken);
    public void ClearModels() => lookup.ClearModels();
}
