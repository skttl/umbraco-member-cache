using Microsoft.Extensions.Options;

namespace Umbraco.Community.MemberCache;

public sealed class MemberCacheOptions
{
    public const string SectionName = "Umbraco:Community:MemberCache";
    public int MaxModels { get; set; } = 10_000;
    public TimeSpan LocalCacheExpiration { get; set; } = TimeSpan.FromDays(1);
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromDays(365);
    public MemberCacheSeedOptions Seed { get; set; } = new();
}

public sealed class MemberCacheSeedOptions
{
    public bool Enabled { get; set; }
    public string[] MemberTypes { get; set; } = [];
    public int BatchSize { get; set; } = 100;
}

internal sealed class MemberCacheOptionsValidator : IValidateOptions<MemberCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, MemberCacheOptions options)
    {
        var failures = new List<string>();
        if (options.MaxModels <= 0) failures.Add("MaxModels must be greater than zero.");
        if (options.CacheExpiration <= TimeSpan.Zero) failures.Add("CacheExpiration must be greater than zero.");
        if (options.LocalCacheExpiration <= TimeSpan.Zero || options.LocalCacheExpiration > options.CacheExpiration)
            failures.Add("LocalCacheExpiration must be greater than zero and no greater than CacheExpiration.");
        if (options.Seed is null) failures.Add("Seed must be configured as an object.");
        else
        {
            if (options.Seed.BatchSize <= 0) failures.Add("Seed:BatchSize must be greater than zero.");
            if (options.Seed.MemberTypes is null || options.Seed.MemberTypes.Any(string.IsNullOrWhiteSpace))
                failures.Add("Seed:MemberTypes must contain only nonblank member type aliases.");
        }
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
