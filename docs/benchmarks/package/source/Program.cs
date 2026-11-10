using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
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

string runtime = Environment.GetEnvironmentVariable("PACKAGE_BENCHMARK_RUNTIME")
    ?? throw new InvalidOperationException("Set PACKAGE_BENCHMARK_RUNTIME to a new scratch directory.");
Directory.CreateDirectory(runtime);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{ Args = [], ContentRootPath = Path.GetFullPath(runtime), EnvironmentName = "Development" });
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ConnectionStrings:umbracoDbDSN"] = $"Data Source={Path.Combine(runtime, "benchmark.db")};Cache=Shared;Foreign Keys=True;Pooling=True",
    ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.Sqlite",
    ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
    ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Benchmark",
    ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "benchmark@example.invalid",
    ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = $"Local.Benchmark.{Guid.NewGuid():N}!",
    ["Umbraco:CMS:Unattended:UnattendedTelemetryLevel"] = "Minimal",
    ["Umbraco:CMS:Global:Id"] = "member-cache-final-package-benchmark",
    ["Umbraco:CMS:Global:UseHttps"] = "false",
    ["Umbraco:CMS:ModelsBuilder:ModelsMode"] = "InMemoryAuto",
    ["Umbraco:CMS:ModelsBuilder:ModelsNamespace"] = "PackageBenchmark.Generated",
    ["Umbraco:CMS:Runtime:Mode"] = "BackofficeDevelopment",
    ["Logging:LogLevel:Default"] = "Warning"
});
var umbraco = builder.CreateUmbracoBuilder().AddBackOffice().AddWebsite().AddComposers();
umbraco.PropertyValueConverters().Append<CoreFixtureMemberPickerValueConverter>();
umbraco.Build();
await using var app = builder.Build();
await app.BootUmbracoAsync();
app.UseUmbraco().WithMiddleware(u => { u.UseBackOffice(); u.UseWebsite(); })
    .WithEndpoints(u => { u.UseBackOfficeEndpoints(); u.UseWebsiteEndpoints(); });
app.Urls.Add("http://127.0.0.1:0");
await app.StartAsync();
try
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    using var context = services.GetRequiredService<IUmbracoContextFactory>().EnsureUmbracoContext();
    var strings = services.GetRequiredService<IShortStringHelper>();
    var editors = services.GetRequiredService<PropertyEditorCollection>();
    var json = services.GetRequiredService<IConfigurationEditorJsonSerializer>();
    var dataTypes = services.GetRequiredService<IDataTypeService>();
    var types = services.GetRequiredService<IContentTypeService>();
    var memberTypes = services.GetRequiredService<IMemberTypeService>();
    var members = services.GetRequiredService<IMemberService>();
    var models = services.GetRequiredService<IPublishedModelFactory>();
    var lookup = services.GetRequiredService<IPublishedMemberLookup>();
    var checks = new List<string>();
    void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        checks.Add(name);
        Console.WriteLine("PASS: " + name);
    }
    async Task<IDataType> DataType(string alias)
    {
        var type = new DataType(editors[alias], json) { Name = "Package benchmark " + alias, DatabaseType = ValueStorageType.Nvarchar };
        var result = await dataTypes.CreateAsync(type, Constants.Security.SuperUserKey);
        if (!result.Success) throw new InvalidOperationException("Datatype failed: " + result.Status);
        return result.Result;
    }
    PropertyType Property(IDataType type, string alias) => new(strings, type, alias) { Name = alias };
    var text = await DataType(Constants.PropertyEditors.Aliases.TextBox);
    var picker = await DataType(Constants.PropertyEditors.Aliases.MemberPicker);
    var memberType = new MemberType(strings, -1) { Alias = "packageBenchmarkMember", Name = "Package benchmark member" };
    memberType.AddPropertyType(Property(text, "profileTitle"));
    if (!(await memberTypes.CreateAsync(memberType, Constants.Security.SuperUserKey)).Success)
        throw new InvalidOperationException("Member type failed");
    memberType = (MemberType)memberTypes.Get(memberType.Id)!;
    var member = members.CreateMemberWithIdentity("package-benchmark", "benchmark@example.invalid", "Package benchmark member", memberType);
    string expected = "00123 – ÆØÅ";
    member.SetValue("profileTitle", expected);
    Check(members.Save(member).Success, "MemberSaved");
    var pageType = new ContentType(strings, -1)
    { Alias = "packageBenchmarkDocument", Name = "Package benchmark document", AllowedAsRoot = true };
    pageType.AddPropertyType(Property(picker, "pickedMember"));
    pageType.AddPropertyType(Property(picker, "cachedPickerNone"));
    Check((await types.CreateAsync(pageType, Constants.Security.SuperUserKey)).Success, "DocumentTypeCreated");
    var content = services.GetRequiredService<IContentService>();
    var page = content.Create("Package benchmark", -1, pageType.Alias);
    string selection = Udi.Create(Constants.UdiEntityType.Member, member.Key).ToString();
    page.SetValue("pickedMember", selection);
    page.SetValue("cachedPickerNone", selection);
    Check(content.Save(page).Success && content.Publish(page, ["*"]).Success, "RealDocumentSavedAndPublished");
    var owner = await services.GetRequiredService<IPublishedContentCache>().GetByIdAsync(page.Id)
        ?? throw new InvalidOperationException("Published document missing");
    var canonical = await lookup.GetByIdAsync(member.Id);
    var core = owner.Value<IPublishedContent>("pickedMember");
    var cached = owner.Value<IPublishedContent>("cachedPickerNone");
    Check(core?.Id == member.Id && cached?.Id == member.Id && core.Key == member.Key && cached.Key == member.Key,
        "BothNormalValueCallsPreserveMemberIdentity");
    Check(core!.Value<string>("profileTitle") == expected && cached!.Value<string>("profileTitle") == expected,
        "BothNormalValueCallsReadIdenticalProfileTitle");
    Check(cached!.GetType() == models.GetModelType(memberType.Alias) && cached.GetType().FullName == "PackageBenchmark.Generated.PackageBenchmarkMember",
        "ActualPackageReturnsGeneratedMemberModel");
    Check(ReferenceEquals(canonical, cached) && ReferenceEquals(cached, await lookup.GetByIdAsync(member.Id)),
        "ActualPublicLookupAndPickerShareCanonicalWarmModel");
    var coreProperty = owner.ContentType.GetPropertyType("pickedMember")!;
    var cachedProperty = owner.ContentType.GetPropertyType("cachedPickerNone")!;
    Check(coreProperty.DataType.Id == cachedProperty.DataType.Id && Equals(page.GetValue("pickedMember"), page.GetValue("cachedPickerNone")),
        "SamePickerDatatypeAndRawSource");
    Check(cachedProperty.CacheLevel == PropertyCacheLevel.None, "ActualPipelineCacheLevelIsNone");
    string? SelectedConverter(IPublishedPropertyType property) => typeof(PublishedPropertyType)
        .GetField("_converter", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(property)?.GetType().FullName;
    string? coreConverter = SelectedConverter(coreProperty), cachedConverter = SelectedConverter(cachedProperty);
    Check(coreConverter == typeof(CoreFixtureMemberPickerValueConverter).FullName &&
        cachedConverter == typeof(CachedMemberPickerValueConverter).FullName, "ActualPipelineSelectsCoreControlAndAutomaticPackageConverter");
    var options = services.GetRequiredService<IOptions<MemberCacheOptions>>().Value;
    var validation = new
    {
        Cms = typeof(IMember).Assembly.GetName().Version!.ToString(),
        Package = typeof(IPublishedMemberLookup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        CoreConverter = coreConverter, CachedConverter = cachedConverter,
        CorePropertyCacheLevel = coreProperty.CacheLevel.ToString(), CachedPropertyCacheLevel = cachedProperty.CacheLevel.ToString(),
        DocumentOwner = owner.GetType().FullName, DocumentId = owner.Id, MemberId = member.Id, MemberKey = member.Key,
        CoreMemberModel = core.GetType().FullName, PackageMemberModel = cached.GetType().FullName,
        HybridCache = services.GetRequiredService<HybridCache>().GetType().FullName,
        DistributedCache = services.GetService<IDistributedCache>()?.GetType().FullName,
        options.MaxModels, options.LocalCacheExpiration, options.CacheExpiration, SeedEnabled = options.Seed.Enabled,
        Passed = checks.Count, Checks = checks
    };
    await File.WriteAllTextAsync(Path.Combine(runtime, "validation.json"), JsonSerializer.Serialize(validation, new JsonSerializerOptions { WriteIndented = true }));
    PackagePickerBenchmarks.Owner = owner;
    PackagePickerBenchmarks.Contexts = services.GetRequiredService<IUmbracoContextFactory>();
    var fixture = new PackagePickerBenchmarks();
    Check(fixture.CorePicker() == expected && fixture.CachedPicker() == expected, "MeasuredMethodsReturnExpectedValue");
    bool dry = Environment.GetEnvironmentVariable("PACKAGE_BENCHMARK_JOB") == "Dry";
    var job = dry ? Job.Dry : Job.Default.WithWarmupCount(5).WithIterationCount(15);
    var config = DefaultConfig.Instance.AddJob(job.WithToolchain(InProcessNoEmitToolchain.Instance));
    var summaries = BenchmarkSwitcher.FromTypes([typeof(PackagePickerBenchmarks)]).Run(args, config).ToArray();
    Check(summaries.Length == 1 && !summaries[0].HasCriticalValidationErrors && summaries[0].Reports.Count() == 2 &&
        summaries[0].Reports.All(r => r.Success), "BothBenchmarkCasesCompleted");
}
finally { await app.StopAsync(); }

