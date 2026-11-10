using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Umbraco.Community.MemberCache.Tests;

public class SourceTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("username")]
    [InlineData("batch")]
    public async Task RawSourceReadsBypassStaleRepositoryCacheAndDisposeTheirReadScope(string operation)
    {
        using var fixture = new RepositoryFixture();
        fixture.DatabaseMember = fixture.Member(operation == "username" ? 2 : 1, "fresh database value");
        switch (operation)
        {
            case "id":
                Assert.Equal("fresh database value", (await fixture.Source.LoadAsync(1, default))!.Name);
                break;
            case "username":
                Assert.Equal(2, await fixture.Source.FindIdAsync("taken-username", default));
                break;
            case "batch":
                Assert.Equal("fresh database value", Assert.Single(await fixture.Source.LoadManyAsync([1], default)).Name);
                break;
        }
        Assert.Equal((RepositoryCacheMode.None, true), Assert.Single(fixture.ReadScopes));
        Assert.Equal(1, fixture.DisposedScopes);
        Assert.Equal(RepositoryCacheMode.Default, fixture.AmbientMode);
    }

    [Fact]
    public async Task UnnotifiedNodeCannotPopulateNewSharedGenerationFromItsStaleRepositoryCache()
    {
        var shared = new CacheCoreTests.SharedCache();
        using var upstream = new RepositoryFixture();
        var sourceA = new CacheCoreTests.Source();
        sourceA.Members[1] = CacheCoreTests.Snapshot(1, "fresh database value");
        using var a = new CacheCoreTests.Node(shared, sourceA);
        using var b = new CacheCoreTests.Node(shared, actualSource: upstream.Source);
        await b.Lookup.SeedAsync([1]);
        upstream.DatabaseMember = upstream.Member(1, "fresh database value");
        await a.Invalidator.InvalidateMembersAsync([1]);

        // B has not received its Umbraco refresher; its simulated repository cache still holds the old member.
        Assert.Equal("fresh database value", (await b.Lookup.GetByIdAsync(1))!.Name);
        Assert.Equal("fresh database value", (await a.Lookup.GetByIdAsync(1))!.Name);
        Assert.Equal(0, sourceA.LoadCount);
        Assert.All(upstream.ReadScopes, scope => Assert.Equal((RepositoryCacheMode.None, true), scope));
        Assert.Equal(upstream.ReadScopes.Count, upstream.DisposedScopes);
    }

    private sealed class RepositoryFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly MemberType _type;
        private readonly IMember _cachedMember;
        public IMember DatabaseMember { get; set; }
        public RepositoryCacheMode AmbientMode { get; private set; } = RepositoryCacheMode.Default;
        public List<(RepositoryCacheMode Mode, bool AutoComplete)> ReadScopes { get; } = [];
        public int DisposedScopes { get; private set; }
        public UmbracoMemberCacheSource Source { get; }

        public RepositoryFixture()
        {
            var strings = new DefaultShortStringHelper(Options.Create(new RequestHandlerSettings()));
            _type = new MemberType(strings, -1) { Id = 7, Alias = "person", Name = "Person" };
            _cachedMember = DatabaseMember = Member(1, "stale repository value");
            var scopeProvider = Substitute.For<ICoreScopeProvider>();
            scopeProvider.CreateCoreScope().ReturnsForAnyArgs(call =>
            {
                var parent = AmbientMode;
                AmbientMode = call.ArgAt<RepositoryCacheMode>(1);
                ReadScopes.Add((AmbientMode, call.ArgAt<bool>(6)));
                var coreScope = Substitute.For<ICoreScope>();
                coreScope.When(x => x.Dispose()).Do(_ => { AmbientMode = parent; ++DisposedScopes; });
                return coreScope;
            });
            var members = Substitute.For<IMemberService>();
            members.GetById(Arg.Any<int>()).Returns(_ => ReadMember());
            members.GetByUsername(Arg.Any<string>()).Returns(_ => ReadMember());
            members.GetAllMembers(Arg.Any<int[]>()).Returns(_ => ReadMembers());
            var types = Substitute.For<IMemberTypeService>();
            types.Get(7).Returns(_type);
            var published = Substitute.For<IPublishedMemberCache>();
            published.GetAsync(Arg.Any<IMember>()).Returns(call =>
            {
                var member = call.Arg<IMember>();
                var model = Substitute.For<IPublishedMember>();
                model.Id.Returns(member.Id);
                model.Name.Returns(member.Name);
                model.ContentType.Alias.Returns("person");
                return Task.FromResult<IPublishedMember?>(model);
            });
            _services = new ServiceCollection().AddScoped(_ => scopeProvider).AddScoped(_ => members)
                .AddScoped(_ => types).AddScoped(_ => published).BuildServiceProvider();
            Source = new UmbracoMemberCacheSource(_services.GetRequiredService<IServiceScopeFactory>(), new NoopPublishedModelFactory());
        }

        public Member Member(int id, string name) => new(_type)
        {
            Id = id, Key = Guid.NewGuid(), Name = name, Username = "taken-username", Email = "test@example.test"
        };

        private IMember ReadMember() => AmbientMode == RepositoryCacheMode.None ? DatabaseMember : _cachedMember;
        private IEnumerable<IMember> ReadMembers() { yield return ReadMember(); }
        public void Dispose() => _services.Dispose();
    }
}
