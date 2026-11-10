using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Xunit;

namespace Umbraco.Community.MemberCache.Tests;

public class IntegrationTests
{
    private readonly IPublishedMemberLookup _lookup = Substitute.For<IPublishedMemberLookup>();
    private readonly IMemberService _members = Substitute.For<IMemberService>();
    private readonly IPublishedMemberCache _published = Substitute.For<IPublishedMemberCache>();
    private readonly IIdKeyMap _ids = Substitute.For<IIdKeyMap>();
    private readonly IExternalMemberService _external = Substitute.For<IExternalMemberService>();
    private readonly IPublishedElement _owner = Substitute.For<IPublishedElement>();
    private readonly IPublishedPropertyType _property = Substitute.For<IPublishedPropertyType>();
    public IntegrationTests()
    {
        _lookup.GetByIdAsync(Arg.Any<int>()).Returns((IPublishedContent?)null);
        _members.GetById(Arg.Any<int>()).Returns((IMember?)null);
        _members.GetById(Arg.Any<Guid>()).Returns((IMember?)null);
        _external.GetByKeyAsync(Arg.Any<Guid>()).Returns((ExternalMemberIdentity?)null);
    }
    private CachedMemberPickerValueConverter Converter => new(_lookup, _ids, _members, _published, _external);
    private object? Convert(object? value) => Converter.ConvertIntermediateToObject(_owner, _property, PropertyCacheLevel.None, value, false);

    [Fact]
    public void Legacy_id_and_member_udi_share_lookup_and_see_replacement()
    {
        var key = Guid.NewGuid();
        var udi = new GuidUdi(Constants.UdiEntityType.Member, key);
        _ids.GetIdForKey(key, UmbracoObjectTypes.Member).Returns(Attempt.Succeed(42));
        var first = Substitute.For<IPublishedContent>();
        var replacement = Substitute.For<IPublishedContent>();
        _lookup.GetByIdAsync(42).Returns(first, replacement);
        Assert.Same(first, Convert(42));
        Assert.Same(replacement, Convert(udi));
        Assert.Equal(PropertyCacheLevel.None, Converter.GetPropertyCacheLevel(_property));
        _members.DidNotReceiveWithAnyArgs().GetById(default(int));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public void Invalid_source_has_core_null_semantics(object? source)
    {
        Assert.Null(Converter.ConvertSourceToIntermediate(_owner, _property, source, false));
        Assert.Null(Convert(null));
    }

    [Fact]
    public void Missing_numeric_member_has_core_null_semantics() => Assert.Null(Convert(42));

    [Fact]
    public void Missing_member_udi_uses_core_external_fallback_and_source_fallback()
    {
        var key = Guid.NewGuid();
        var udi = new GuidUdi(Constants.UdiEntityType.Member, key);
        Assert.Same(udi, Convert(udi));
        _external.Received(1).GetByKeyAsync(key);
    }

    [Fact]
    public void External_member_udi_preserves_core_published_external_result()
    {
        var key = Guid.NewGuid();
        _external.GetByKeyAsync(key).Returns(new ExternalMemberIdentity { Id = 9, Key = key, Name = "External" });
        var result = Assert.IsAssignableFrom<IPublishedContent>(Convert(new GuidUdi(Constants.UdiEntityType.Member, key)));
        Assert.Equal(key, result.Key);
        Assert.Equal("External", result.Name);
    }

    [Fact]
    public void Composer_replaces_only_core_default_and_project_converter_wins()
    {
        var services = new ServiceCollection();
        var collection = new PropertyValueConverterCollectionBuilder();
        collection.Append<MemberPickerValueConverter>().Append<ProjectMemberConverter>();
        var builder = Substitute.For<IUmbracoBuilder>();
        builder.Services.Returns(services);
        builder.Config.Returns(new ConfigurationBuilder().Build());
        builder.WithCollectionBuilder<PropertyValueConverterCollectionBuilder>().Returns(collection);
        new MemberCacheComposer().Compose(builder);
        services.AddSingleton(_lookup);
        services.AddSingleton(_ids);
        services.AddSingleton(_members);
        services.AddSingleton(_published);
        services.AddSingleton(_external);
        collection.RegisterWith(services);
        using var provider = services.BuildServiceProvider();
        var converters = provider.GetRequiredService<PropertyValueConverterCollection>();
        Assert.DoesNotContain(converters, converter => converter.GetType() == typeof(MemberPickerValueConverter));
        Assert.Single(converters.OfType<CachedMemberPickerValueConverter>());
        Assert.Single(converters.OfType<ProjectMemberConverter>());

        var factory = Substitute.For<IPublishedContentTypeFactory>();
        factory.GetDataType(1).Returns(new PublishedDataType(1, Constants.PropertyEditors.Aliases.MemberPicker, null, new Lazy<object?>(() => null)));
        var property = new PublishedPropertyType("picker", 1, true, ContentVariation.Nothing, converters, Substitute.For<IPublishedModelFactory>(), factory);
        Assert.Equal("project result", property.ConvertInterToObject(_owner, PropertyCacheLevel.None, 42, false));
    }

    [Fact]
    public void Converter_is_default_and_delivery_api_stays_unsupported()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(CachedMemberPickerValueConverter), typeof(DefaultPropertyValueConverterAttribute)));
        Assert.Equal("(unsupported)", Converter.ConvertIntermediateToDeliveryApiObject(_owner, _property, PropertyCacheLevel.None, 42, false, false));
    }

    public sealed class ProjectMemberConverter : PropertyValueConverterBase
    {
        public override bool IsConverter(IPublishedPropertyType propertyType) => propertyType.EditorAlias == Constants.PropertyEditors.Aliases.MemberPicker;
        public override object? ConvertIntermediateToObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object? inter, bool preview) => "project result";
    }
}
