using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.MemberCache.Tests;

public class MemberTypeSeedProviderTests
{
    [Fact]
    public async Task Selects_configured_types_with_paging_and_deduplicated_aliases()
    {
        var members = Substitute.For<IMemberService>();
        var types = Substitute.For<IMemberTypeService>();
        types.Get("customers").Returns(Substitute.For<IMemberType>());
        members.GetAll(Arg.Any<int>(), 2, out Arg.Any<long>(), "id", Direction.Ascending, "customers", "")
            .Returns(call =>
            {
                call[2] = 3L;
                int skip = call.ArgAt<int>(0);
                return Enumerable.Range(skip + 1, skip == 0 ? 2 : 1).Select(id => { var member = Substitute.For<IMember>(); member.Id.Returns(id); return member; }).ToArray();
            });
        var provider = new MemberTypeSeedProvider(members, types, Options.Create(new MemberCacheOptions { Seed = new() { MemberTypes = ["customers", "customers"], BatchSize = 2 } }));
        int[] actual = await Collect(provider);
        Assert.Equal([1, 2, 3], actual);
        members.Received(1).GetAll(0, 2, out Arg.Any<long>(), "id", Direction.Ascending, "customers", "");
        members.Received(1).GetAll(2, 2, out Arg.Any<long>(), "id", Direction.Ascending, "customers", "");
    }

    [Fact]
    public async Task Unknown_type_stops_before_member_queries()
    {
        var members = Substitute.For<IMemberService>();
        var types = Substitute.For<IMemberTypeService>();
        types.Get("missing").Returns((IMemberType?)null);
        var provider = new MemberTypeSeedProvider(members, types, Options.Create(new MemberCacheOptions { Seed = new() { MemberTypes = ["missing"] } }));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(provider));
        Assert.Contains("missing", exception.Message);
        Assert.Empty(members.ReceivedCalls());
    }

    [Fact]
    public async Task Cancellation_stops_before_querying_types_or_members()
    {
        var types = Substitute.For<IMemberTypeService>();
        var members = Substitute.For<IMemberService>();
        var provider = new MemberTypeSeedProvider(members, types, Options.Create(new MemberCacheOptions { Seed = new() { MemberTypes = ["customers"] } }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect(provider, new CancellationToken(true)));
        Assert.Empty(types.ReceivedCalls());
        Assert.Empty(members.ReceivedCalls());
    }

    private static async Task<int[]> Collect(IMemberSeedProvider provider, CancellationToken cancellationToken = default)
    {
        var ids = new List<int>();
        await foreach (int id in provider.GetMemberIdsAsync(cancellationToken)) ids.Add(id);
        return ids.ToArray();
    }
}
