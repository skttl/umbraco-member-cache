using System.Text.Json;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Community.MemberCache;
using Umbraco.Extensions;
using File = System.IO.File;

// Opt-in sample and real-host verification; never runs in a normal package installation.
internal static class DemoFixture
{
    public static async Task RunAsync(IServiceProvider root, IConfiguration configuration)
    {
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        using var context = services.GetRequiredService<IUmbracoContextFactory>().EnsureUmbracoContext();
        var members = services.GetRequiredService<IMemberService>();
        if (members.GetByUsername("member-cache-demo") is not null) return;
        var strings = services.GetRequiredService<IShortStringHelper>();
        var editors = services.GetRequiredService<PropertyEditorCollection>();
        var json = services.GetRequiredService<IConfigurationEditorJsonSerializer>();
        var types = services.GetRequiredService<IContentTypeService>();
        var memberTypes = services.GetRequiredService<IMemberTypeService>();
        var dataTypes = services.GetRequiredService<IDataTypeService>();
        var serializer = services.GetRequiredService<IJsonSerializer>();
        var models = services.GetRequiredService<IPublishedModelFactory>();
        var lookup = services.GetRequiredService<IPublishedMemberLookup>();
        var checks = new List<string>();
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("Member cache verification failed: " + name);
            checks.Add(name);
            Console.WriteLine("MEMBER_CACHE_PASS: " + name);
        }
        async Task<IDataType> DataType(string alias, ValueStorageType storage, Dictionary<string, object>? config = null)
        {
            var type = new DataType(editors[alias], json) { Name = "Member cache demo " + alias, DatabaseType = storage };
            if (config is not null) type.ConfigurationData = config;
            var result = await dataTypes.CreateAsync(type, Constants.Security.SuperUserKey);
            if (!result.Success) throw new InvalidOperationException("Could not create demo datatype: " + result.Status);
            return result.Result;
        }
        PropertyType Property(IDataType type, string alias) => new(strings, type, alias) { Name = alias };
        var text = await DataType(Constants.PropertyEditors.Aliases.TextBox, ValueStorageType.Nvarchar);
        var integer = await DataType(Constants.PropertyEditors.Aliases.Integer, ValueStorageType.Integer);
        var number = await DataType(Constants.PropertyEditors.Aliases.Decimal, ValueStorageType.Decimal);
        var picker = await DataType(Constants.PropertyEditors.Aliases.MemberPicker, ValueStorageType.Nvarchar);
        var blockType = new ContentType(strings, -1) { Alias = "memberCacheDemoBlock", Name = "Member cache demo block", IsElement = true };
        blockType.AddPropertyType(Property(text, "heading"));
        if (!(await types.CreateAsync(blockType, Constants.Security.SuperUserKey)).Success) throw new InvalidOperationException("Block type failed");
        var blocks = await DataType(Constants.PropertyEditors.Aliases.BlockList, ValueStorageType.Ntext,
            new() { ["blocks"] = new BlockListConfiguration.BlockConfiguration[] { new() { ContentElementTypeKey = blockType.Key } } });
        var memberType = new MemberType(strings, -1) { Alias = "memberCacheDemoMember", Name = "Member cache demo member" };
        foreach (var property in new[] { Property(text, "profileTitle"), Property(integer, "points"), Property(number, "balance"), Property(blocks, "blocks") })
            memberType.AddPropertyType(property);
        if (!(await memberTypes.CreateAsync(memberType, Constants.Security.SuperUserKey)).Success) throw new InvalidOperationException("Member type failed");
        memberType = (MemberType)memberTypes.Get(memberType.Id)!;
        var blockKey = Guid.NewGuid();
        var blockValue = new BlockListValue
        {
            Layout = new Dictionary<string, IEnumerable<IBlockLayoutItem>> { [Constants.PropertyEditors.Aliases.BlockList] = [new BlockListLayoutItem { ContentKey = blockKey }] },
            ContentData = [new() { Key = blockKey, ContentTypeKey = blockType.Key, ContentTypeAlias = blockType.Alias,
                Values = [new() { Alias = "heading", Value = "Block from cached member" }] }],
            Expose = [new(blockKey, null, null)]
        };
        var member = members.CreateMemberWithIdentity("member-cache-demo", "member-cache-demo@example.invalid", "Member cache demo", memberType);
        member.SetValue("profileTitle", "Member from cache");
        member.SetValue("points", 42);
        member.SetValue("balance", 1234567890.1234m);
        member.SetValue("blocks", serializer.Serialize(blockValue));
        if (!members.Save(member).Success) throw new InvalidOperationException("Member save failed");
        var cached = await lookup.GetByIdAsync(member.Id);
        Check(cached is not null && cached.Id == member.Id && cached.Key == member.Key, "PublicLookupPreservesIdentity");
        Check(cached!.GetType() == models.GetModelType(memberType.Alias) && cached.GetType().Name == "MemberCacheDemoMember", "LookupUsesGeneratedModelsBuilderModel");
        Check(cached.Value<int>("points") == 42 && cached.Value<decimal>("balance") == 1234567890.1234m, "CachedRawValuesKeepStorageTypes");
        var convertedBlocks = cached.Value<BlockListModel>("blocks")!;
        Check(convertedBlocks.Count == 1 && convertedBlocks[0].Content.GetType() == models.GetModelType(blockType.Alias) &&
            convertedBlocks[0].Content.Value<string>("heading") == "Block from cached member", "CachedBlockListUsesGeneratedElementModel");
        Check(ReferenceEquals(cached, await lookup.GetByIdAsync(member.Id)) && ReferenceEquals(cached, await lookup.GetByUsernameAsync(member.Username)), "IdAndUsernameShareCanonicalModel");
        var batch = await lookup.GetByIdsAsync([member.Id, int.MaxValue, member.Id]);
        Check(batch.Count == 2 && ReferenceEquals(batch[0], batch[1]), "BatchOmitsMissingAndPreservesDuplicates");

        var pageType = new ContentType(strings, -1) { Alias = "memberCacheDemoPage", Name = "Member cache demo page", AllowedAsRoot = true };
        pageType.AddPropertyType(Property(picker, "selectedMember"));
        if (!(await types.CreateAsync(pageType, Constants.Security.SuperUserKey)).Success) throw new InvalidOperationException("Page type failed");
        var content = services.GetRequiredService<IContentService>();
        var page = content.Create("Member cache demo", -1, pageType.Alias);
        page.SetValue("selectedMember", Udi.Create(Constants.UdiEntityType.Member, member.Key).ToString());
        content.Save(page);
        if (!content.Publish(page, ["*"]).Success) throw new InvalidOperationException("Page publish failed");
        var publishedPage = await services.GetRequiredService<IPublishedContentCache>().GetByIdAsync(page.Id);
        if (publishedPage is null) throw new InvalidOperationException("Published demo document missing");
        Check(publishedPage.Id == page.Id, "RealDocumentIsPublished");
        var picked = publishedPage.Value<IPublishedContent>("selectedMember");
        Check(picked?.Id == member.Id && picked.GetType() == models.GetModelType(memberType.Alias), "AutomaticPickerOnDocumentReturnsGeneratedMemberModel");
        Check(picked!.Value<string>("profileTitle") == "Member from cache", "AutomaticPickerReadsMemberProperty");

        member.SetValue("profileTitle", "Updated cached member");
        if (!members.Save(member).Success) throw new InvalidOperationException("Update failed");
        Check((await lookup.GetByIdAsync(member.Id))?.Value<string>("profileTitle") == "Updated cached member", "MemberSaveInvalidatesLookup");
        Check(publishedPage.Value<IPublishedContent>("selectedMember")?.Value<string>("profileTitle") == "Updated cached member", "AlreadyReadDocumentPickerSeesMemberSave");
        member.Username = "member-cache-demo-renamed";
        if (!members.Save(member).Success) throw new InvalidOperationException("Rename failed");
        Check(await lookup.GetByUsernameAsync("member-cache-demo") is null && (await lookup.GetByUsernameAsync(member.Username))?.Id == member.Id, "MemberRenameInvalidatesUsernameAliases");
        if (configuration.GetValue<bool>("MemberCacheDemo:DestructiveVerification"))
        {
            members.Delete(member);
            Check(await lookup.GetByIdAsync(member.Id) is null && await lookup.GetByUsernameAsync(member.Username) is null, "MemberDeleteInvalidatesBothLookups");
            Check(publishedPage.Value<IPublishedContent>("selectedMember") is null, "AlreadyReadDocumentPickerSeesMemberDelete");
        }
        var report = new { Passed = checks.Count, Failed = 0, Checks = checks,
            Cms = typeof(IMember).Assembly.GetName().Version!.ToString(), MemberModel = cached.GetType().FullName,
            BlockModel = convertedBlocks[0].Content.GetType().FullName, DocumentId = page.Id };
        var path = configuration["MemberCacheDemo:VerificationReport"];
        if (!string.IsNullOrWhiteSpace(path)) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("MEMBER_CACHE_VERIFIED: " + JsonSerializer.Serialize(report));
    }
}
