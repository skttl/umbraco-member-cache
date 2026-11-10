using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Sync;

namespace Umbraco.Community.MemberCache.Tests;

public class NotificationTests
{
    [Theory]
    [InlineData(MessageType.RefreshById)]
    [InlineData(MessageType.RemoveById)]
    [InlineData(MessageType.RefreshByPayload)]
    [InlineData(MessageType.RefreshAll)]
    [InlineData(MessageType.RefreshByJson)]
    public async Task Member_refresh_messages_replace_models_and_raw_snapshots(MessageType messageType)
    {
        using var node = new CacheCoreTests.Node();
        node.Source.Members[1] = CacheCoreTests.Snapshot(1, "old");
        var old = await node.Lookup.GetByUsernameAsync("old");
        node.Source.Members[1] = CacheCoreTests.Snapshot(1, "new");
        object message = messageType switch
        {
            MessageType.RefreshById or MessageType.RemoveById => 1,
            MessageType.RefreshByPayload => new[] { new MemberCacheRefresher.JsonPayload(1, "new", false) },
            _ => string.Empty
        };
        new MemberCacheNotificationHandler(node.Invalidator).Handle(new MemberCacheRefresherNotification(message, messageType));
        Assert.Null(await node.Lookup.GetByUsernameAsync("old"));
        var current = await node.Lookup.GetByUsernameAsync("new");
        Assert.NotSame(old, current);
        Assert.Equal("new", current!.Name);
        Assert.Equal(2, node.Source.LoadCount);
    }

    [Fact]
    public async Task Deleted_payload_does_not_require_loading_deleted_member()
    {
        using var node = new CacheCoreTests.Node();
        node.Source.Members[1] = CacheCoreTests.Snapshot(1, "old");
        await node.Lookup.GetByIdAsync(1);
        node.Source.Members.TryRemove(1, out _);
        new MemberCacheNotificationHandler(node.Invalidator).Handle(new MemberCacheRefresherNotification(
            new[] { new MemberCacheRefresher.JsonPayload(1, "old", true) }, MessageType.RefreshByPayload));
        Assert.Equal(1, node.Source.LoadCount);
        Assert.Null(await node.Lookup.GetByIdAsync(1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Schema_refresh_retires_models_and_raw_snapshots(bool contentType)
    {
        using var node = new CacheCoreTests.Node();
        node.Source.Members[1] = CacheCoreTests.Snapshot(1, "old");
        var old = await node.Lookup.GetByIdAsync(1);
        var handler = new MemberCacheNotificationHandler(node.Invalidator);
        if (contentType) handler.Handle(new ContentTypeCacheRefresherNotification(string.Empty, MessageType.RefreshAll));
        else handler.Handle(new DataTypeCacheRefresherNotification(string.Empty, MessageType.RefreshAll));
        Assert.NotSame(old, await node.Lookup.GetByIdAsync(1));
        Assert.Equal(2, node.Source.LoadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Content_and_media_refresh_retire_models_but_retain_raw_snapshots(bool content)
    {
        using var node = new CacheCoreTests.Node();
        node.Source.Members[1] = CacheCoreTests.Snapshot(1, "old");
        var old = await node.Lookup.GetByIdAsync(1);
        var handler = new MemberCacheNotificationHandler(node.Invalidator);
        if (content) handler.Handle(new ContentCacheRefresherNotification(string.Empty, MessageType.RefreshAll));
        else handler.Handle(new MediaCacheRefresherNotification(string.Empty, MessageType.RefreshAll));
        Assert.NotSame(old, await node.Lookup.GetByIdAsync(1));
        Assert.Equal(1, node.Source.LoadCount);
    }
}
