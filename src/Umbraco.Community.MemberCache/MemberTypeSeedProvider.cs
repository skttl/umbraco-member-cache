using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.MemberCache;

/// <summary>Selects members of the configured member type aliases using bounded pages.</summary>
public sealed class MemberTypeSeedProvider(
    IMemberService memberService,
    IMemberTypeService memberTypeService,
    IOptions<MemberCacheOptions> options) : IMemberSeedProvider
{
    /// <inheritdoc />
    public async IAsyncEnumerable<int> GetMemberIdsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        MemberCacheSeedOptions settings = options.Value.Seed;
        string[] aliases = settings.MemberTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string alias in aliases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (memberTypeService.Get(alias) is null)
                throw new InvalidOperationException($"Member cache seed member type '{alias}' does not exist.");
        }

        foreach (string alias in aliases)
        {
            long total;
            int skip = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMember[] page = memberService.GetAll(skip, settings.BatchSize, out total, "id", Direction.Ascending, alias).ToArray();
                foreach (IMember member in page)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return member.Id;
                }
                if (page.Length == 0) break;
                skip = checked(skip + page.Length);
            } while (skip < total);
        }
    }
}
