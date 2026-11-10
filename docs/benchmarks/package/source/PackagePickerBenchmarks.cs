using BenchmarkDotNet.Attributes;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

[MemoryDiagnoser]
public class PackagePickerBenchmarks
{
    internal static IPublishedContent Owner = null!;
    internal static IUmbracoContextFactory Contexts = null!;

    [Benchmark(Baseline = true)]
    public string? CorePicker()
    {
        using var context = Contexts.EnsureUmbracoContext();
        return Owner.Value<IPublishedContent>("pickedMember")?.Value<string>("profileTitle");
    }

    [Benchmark]
    public string? CachedPicker()
    {
        using var context = Contexts.EnsureUmbracoContext();
        return Owner.Value<IPublishedContent>("cachedPickerNone")?.Value<string>("profileTitle");
    }
}
