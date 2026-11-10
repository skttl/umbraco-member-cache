using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.MemberCache;

internal static class MemberSnapshotMapper
{
    public static MemberSnapshot Capture(IMember member) => new()
    {
        Id = member.Id, Key = member.Key, TypeId = member.ContentTypeId, TypeAlias = member.ContentType.Alias,
        Name = member.Name, Username = member.Username, Email = member.Email,
        CreateDate = member.CreateDate, UpdateDate = member.UpdateDate, CreatorId = member.CreatorId,
        SortOrder = member.SortOrder, ParentId = member.ParentId, Level = member.Level, Path = member.Path,
        IsApproved = member.IsApproved, IsLockedOut = member.IsLockedOut,
        LastLoginDate = member.LastLoginDate, LastLockoutDate = member.LastLockoutDate,
        LastPasswordChangeDate = member.LastPasswordChangeDate,
        Values = member.Properties.ToDictionary(p => p.Alias, p => JsonSerializer.SerializeToElement(p.GetValue()))
    };

    public static Member Restore(MemberSnapshot snapshot, IMemberType type)
    {
        var member = new Member(type)
        {
            Id = snapshot.Id, Key = snapshot.Key, Name = snapshot.Name, Email = snapshot.Email,
            Username = snapshot.Username, CreateDate = snapshot.CreateDate, UpdateDate = snapshot.UpdateDate,
            CreatorId = snapshot.CreatorId, SortOrder = snapshot.SortOrder, ParentId = snapshot.ParentId,
            Level = snapshot.Level, Path = snapshot.Path!, IsApproved = snapshot.IsApproved,
            IsLockedOut = snapshot.IsLockedOut, LastLoginDate = snapshot.LastLoginDate,
            LastLockoutDate = snapshot.LastLockoutDate, LastPasswordChangeDate = snapshot.LastPasswordChangeDate
        };
        foreach (var (alias, value) in snapshot.Values)
        {
            var property = member.Properties[alias];
            if (property is null) continue; // A removed property can still exist in an older raw snapshot.
            object? raw = value.ValueKind == JsonValueKind.Null ? null : property.ValueStorageType switch
            {
                ValueStorageType.Integer => value.GetInt32(),
                ValueStorageType.Decimal => value.GetDecimal(),
                ValueStorageType.Date => value.GetDateTime(),
                ValueStorageType.Nvarchar or ValueStorageType.Ntext => value.GetString(),
                _ => throw new NotSupportedException($"Unsupported member property storage: {property.ValueStorageType}.")
            };
            member.SetValue(alias, raw);
        }
        return member;
    }
}

internal interface IMemberCacheSource
{
    Task<MemberSnapshot?> LoadAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemberSnapshot>> LoadManyAsync(int[] ids, CancellationToken cancellationToken);
    Task<int?> FindIdAsync(string username, CancellationToken cancellationToken);
    Task<IPublishedContent?> PublishAsync(MemberSnapshot snapshot, CancellationToken cancellationToken);
    Type GetModelType(string alias);
}

internal sealed class UmbracoMemberCacheSource(IServiceScopeFactory scopes, IPublishedModelFactory models) : IMemberCacheSource
{
    public Task<MemberSnapshot?> LoadAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopes.CreateScope();
        // An unnotified node's repository cache must not populate a new shared generation with stale data.
        using var readScope = scope.ServiceProvider.GetRequiredService<ICoreScopeProvider>()
            .CreateCoreScope(repositoryCacheMode: RepositoryCacheMode.None, autoComplete: true);
        var member = scope.ServiceProvider.GetRequiredService<IMemberService>().GetById(id);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(member is null ? null : MemberSnapshotMapper.Capture(member));
    }

    public Task<int?> FindIdAsync(string username, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopes.CreateScope();
        using var readScope = scope.ServiceProvider.GetRequiredService<ICoreScopeProvider>()
            .CreateCoreScope(repositoryCacheMode: RepositoryCacheMode.None, autoComplete: true);
        var id = scope.ServiceProvider.GetRequiredService<IMemberService>().GetByUsername(username)?.Id;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<MemberSnapshot>> LoadManyAsync(int[] ids, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ids.Length == 0) return Task.FromResult<IReadOnlyList<MemberSnapshot>>([]);
        using var scope = scopes.CreateScope();
        using var readScope = scope.ServiceProvider.GetRequiredService<ICoreScopeProvider>()
            .CreateCoreScope(repositoryCacheMode: RepositoryCacheMode.None, autoComplete: true);
        var snapshots = scope.ServiceProvider.GetRequiredService<IMemberService>().GetAllMembers(ids)
            .Select(MemberSnapshotMapper.Capture).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<MemberSnapshot>>(snapshots);
    }

    public async Task<IPublishedContent?> PublishAsync(MemberSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = scopes.CreateScope();
        var type = scope.ServiceProvider.GetRequiredService<IMemberTypeService>().Get(snapshot.TypeId);
        if (type is null) return null;
        var member = MemberSnapshotMapper.Restore(snapshot, type);
        var published = await scope.ServiceProvider.GetRequiredService<IPublishedMemberCache>().GetAsync(member).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return published is null ? null : (IPublishedContent)models.CreateModel(published);
    }

    public Type GetModelType(string alias) => models.GetModelType(alias);
}
