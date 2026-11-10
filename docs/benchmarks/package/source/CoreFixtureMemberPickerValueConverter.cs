using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;

// The only fixture override: the package remains automatically registered for the other picker.
public sealed class CoreFixtureMemberPickerValueConverter : MemberPickerValueConverter
{
#if UMBRACO18
    public CoreFixtureMemberPickerValueConverter(IMemberService members, IPublishedMemberCache cache,
        IExternalMemberService external) : base(members, cache, external) { }
#else
    public CoreFixtureMemberPickerValueConverter(IMemberService members, IPublishedMemberCache cache)
        : base(members, cache) { }
#endif
    public override bool IsConverter(IPublishedPropertyType propertyType) =>
        propertyType.Alias == "pickedMember" && base.IsConverter(propertyType);
}
