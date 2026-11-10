using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.MemberCache;

/// <summary>Routes the standard Member Picker through the published member cache.</summary>
[DefaultPropertyValueConverter]
public sealed class CachedMemberPickerValueConverter(
    IPublishedMemberLookup lookup,
    IIdKeyMap idKeyMap,
    IMemberService memberService,
    IPublishedMemberCache memberCache,
    IExternalMemberService externalMemberService)
    : MemberPickerValueConverter(memberService, memberCache, externalMemberService)
{
    /// <inheritdoc />
    public override PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.None;

    /// <inheritdoc />
    public override object? ConvertIntermediateToObject(IPublishedElement owner, IPublishedPropertyType propertyType,
        PropertyCacheLevel cacheLevel, object? source, bool preview)
    {
        int? id = source as int?;
        if (source is GuidUdi udi && udi.EntityType == Constants.UdiEntityType.Member)
        {
            Attempt<int> mapped = idKeyMap.GetIdForKey(udi.Guid, UmbracoObjectTypes.Member);
            if (mapped.Success) id = mapped.Result;
        }

        if (id is > 0)
        {
            // Property conversion is synchronous; the lookup itself uses ConfigureAwait(false).
            IPublishedContent? member = lookup.GetByIdAsync(id.Value).GetAwaiter().GetResult();
            if (member is not null) return member;
        }

        return base.ConvertIntermediateToObject(owner, propertyType, cacheLevel, source, preview);
    }
}
