using System.Text.Json;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Strings;

namespace Umbraco.Community.MemberCache.Tests;

public class SnapshotTests
{
    [Fact]
    public void JsonRoundtripPreservesPublicIdentityAccountFieldsAndTypedRawValuesWithoutCredentials()
    {
        var strings = new DefaultShortStringHelper(Options.Create(new RequestHandlerSettings()));
        var type = new MemberType(strings, -1) { Id = 7, Alias = "person", Name = "Person" };
        foreach (var (alias, storage) in new[] { ("text", ValueStorageType.Nvarchar), ("integer", ValueStorageType.Integer),
                     ("decimal", ValueStorageType.Decimal), ("date", ValueStorageType.Date),
                     ("blocks", ValueStorageType.Ntext), ("empty", ValueStorageType.Nvarchar) })
            type.AddPropertyType(new PropertyType(strings, "test", storage, alias));
        var original = new Member(type)
        {
            Id = 42, Key = Guid.NewGuid(), Name = "Member", Email = "member@example.test", Username = "Member",
            CreateDate = new DateTime(2024, 1, 2), UpdateDate = new DateTime(2025, 2, 3),
            CreatorId = 3, SortOrder = 4, IsApproved = false, IsLockedOut = true,
            LastLoginDate = new DateTime(2025, 3, 4), LastLockoutDate = new DateTime(2025, 4, 5),
            LastPasswordChangeDate = new DateTime(2025, 5, 6), RawPasswordValue = "DO-NOT-SERIALIZE",
            SecurityStamp = "SECRET-SECURITY-STAMP", PasswordConfiguration = "SECRET-PASSWORD-CONFIG"
        };
        original.SetValue("text", "00123 – ÆØÅ");
        original.SetValue("integer", 42);
        original.SetValue("decimal", 1234567890.1234m);
        original.SetValue("date", new DateTime(2024, 2, 29, 12, 34, 56, DateTimeKind.Unspecified));
        original.SetValue("blocks", "{\"layout\":{\"Umbraco.BlockList\":[]},\"contentData\":[]}");
        var json = JsonSerializer.Serialize(MemberSnapshotMapper.Capture(original));
        Assert.DoesNotContain("DO-NOT-SERIALIZE", json);
        Assert.DoesNotContain("SECRET-", json);
        var restored = MemberSnapshotMapper.Restore(JsonSerializer.Deserialize<MemberSnapshot>(json)!, type);
        Assert.NotSame(original, restored);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Key, restored.Key);
        Assert.Equal(original.Username, restored.Username);
        Assert.Equal(original.Email, restored.Email);
        Assert.Equal(original.CreateDate, restored.CreateDate);
        Assert.Equal(original.UpdateDate, restored.UpdateDate);
        Assert.Equal(original.CreatorId, restored.CreatorId);
        Assert.Equal(original.SortOrder, restored.SortOrder);
        Assert.Equal(original.IsApproved, restored.IsApproved);
        Assert.Equal(original.IsLockedOut, restored.IsLockedOut);
        Assert.Equal(original.LastLoginDate, restored.LastLoginDate);
        Assert.Equal(original.LastLockoutDate, restored.LastLockoutDate);
        Assert.Equal(original.LastPasswordChangeDate, restored.LastPasswordChangeDate);
        foreach (var property in original.Properties)
            Assert.Equal(property.GetValue(), restored.GetValue(property.Alias));
        Assert.IsType<int>(restored.GetValue("integer"));
        Assert.IsType<decimal>(restored.GetValue("decimal"));
        Assert.IsType<DateTime>(restored.GetValue("date"));
        Assert.Null(restored.GetValue("empty"));
        Assert.True(string.IsNullOrEmpty(restored.RawPasswordValue));
    }

    [Fact]
    public void DefaultsAndValidationRejectInvalidCapacityDurationsAndSeedConfiguration()
    {
        var options = new MemberCacheOptions();
        var validator = new MemberCacheOptionsValidator();
        Assert.Equal(10000, options.MaxModels);
        Assert.Equal(TimeSpan.FromDays(1), options.LocalCacheExpiration);
        Assert.Equal(TimeSpan.FromDays(365), options.CacheExpiration);
        Assert.False(options.Seed.Enabled);
        Assert.Equal(100, options.Seed.BatchSize);
        Assert.Empty(options.Seed.MemberTypes);
        Assert.True(validator.Validate(null, options).Succeeded);
        Assert.True(validator.Validate(null, new() { MaxModels = 0 }).Failed);
        Assert.True(validator.Validate(null, new() { LocalCacheExpiration = TimeSpan.Zero }).Failed);
        Assert.True(validator.Validate(null, new() { CacheExpiration = TimeSpan.Zero }).Failed);
        Assert.True(validator.Validate(null, new() { LocalCacheExpiration = TimeSpan.FromDays(366) }).Failed);
        Assert.True(validator.Validate(null, new() { Seed = new() { BatchSize = 0 } }).Failed);
        Assert.True(validator.Validate(null, new() { Seed = new() { MemberTypes = [" "] } }).Failed);
    }
}
