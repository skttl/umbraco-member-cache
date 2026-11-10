using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;

namespace Umbraco.Community.MemberCache;

/// <summary>Automatically installs the cache, Member Picker converter and distributed invalidation.</summary>
public sealed class MemberCacheComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<MemberCacheOptions>()
            .Bind(builder.Config.GetSection(MemberCacheOptions.SectionName)).ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MemberCacheOptions>, MemberCacheOptionsValidator>());
        builder.Services.AddHybridCache();
        builder.Services.TryAddSingleton<IMemberCacheSource, UmbracoMemberCacheSource>();
        builder.Services.TryAddSingleton<PublishedMemberLookup>();
        builder.Services.TryAddSingleton<IPublishedMemberLookup>(provider => provider.GetRequiredService<PublishedMemberLookup>());
        builder.Services.TryAddSingleton<IRawMemberSeeder>(provider => provider.GetRequiredService<PublishedMemberLookup>());
        builder.Services.TryAddSingleton<MemberCacheInvalidator>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IMemberSeedProvider, MemberTypeSeedProvider>());
        builder.PropertyValueConverters().Remove<MemberPickerValueConverter>().Append<CachedMemberPickerValueConverter>();
        builder.AddNotificationHandler<MemberCacheRefresherNotification, MemberCacheNotificationHandler>()
            .AddNotificationHandler<ContentTypeCacheRefresherNotification, MemberCacheNotificationHandler>()
            .AddNotificationHandler<DataTypeCacheRefresherNotification, MemberCacheNotificationHandler>()
            .AddNotificationHandler<ContentCacheRefresherNotification, MemberCacheNotificationHandler>()
            .AddNotificationHandler<MediaCacheRefresherNotification, MemberCacheNotificationHandler>()
            .AddNotificationAsyncHandler<UmbracoApplicationStartingNotification, MemberCacheStartup>();
    }
}
