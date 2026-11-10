using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Sync;

namespace Umbraco.Community.MemberCache;

internal sealed class MemberCacheNotificationHandler(MemberCacheInvalidator invalidator) :
    INotificationHandler<MemberCacheRefresherNotification>,
    INotificationHandler<ContentTypeCacheRefresherNotification>,
    INotificationHandler<DataTypeCacheRefresherNotification>,
    INotificationHandler<ContentCacheRefresherNotification>,
    INotificationHandler<MediaCacheRefresherNotification>
{
    // Refreshers publish synchronously on every node, so completion must include invalidation.
    public void Handle(MemberCacheRefresherNotification notification)
    {
        IEnumerable<int>? ids = notification.MessageType switch
        {
            MessageType.RefreshByPayload when notification.MessageObject is MemberCacheRefresher.JsonPayload[] payloads => payloads.Select(payload => payload.Id),
            MessageType.RefreshById or MessageType.RemoveById when notification.MessageObject is int id => [id],
            MessageType.RefreshByInstance or MessageType.RemoveByInstance when notification.MessageObject is IMember member => [member.Id],
            _ => null
        };
        if (ids is null) invalidator.InvalidateAllAsync().GetAwaiter().GetResult();
        else invalidator.InvalidateMembersAsync(ids).GetAwaiter().GetResult();
    }

    public void Handle(ContentTypeCacheRefresherNotification notification) => invalidator.InvalidateAllAsync().GetAwaiter().GetResult();
    public void Handle(DataTypeCacheRefresherNotification notification) => invalidator.InvalidateAllAsync().GetAwaiter().GetResult();
    public void Handle(ContentCacheRefresherNotification notification) => invalidator.ClearModels();
    public void Handle(MediaCacheRefresherNotification notification) => invalidator.ClearModels();
}
