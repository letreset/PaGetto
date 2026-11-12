using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Artifacts;

public class ArtifactDatabaseTests
{
    public class AddVersionAsync : FactsBase
    {
        [Fact]
        public async Task CreatesThePackageWithItsFirstVersion()
        {
            var result = await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.Equal(ArtifactAddResult.Success, result);

            var artifact = await Context.Artifacts.Include(a => a.Versions).SingleAsync(Ct);
            Assert.Equal("my_package", artifact.Name);
            Assert.Equal(Feed.Id, artifact.FeedId);
            Assert.Equal("1.0.0", Assert.Single(artifact.Versions).Version);
        }

        [Fact]
        public async Task AddsFurtherVersionsToTheSamePackage()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            var result = await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0+1"), Ct);

            Assert.Equal(ArtifactAddResult.Success, result);
            Assert.Equal(1, await Context.Artifacts.CountAsync(Ct));
            Assert.Equal(2, await Context.ArtifactVersions.CountAsync(Ct));
        }

        [Fact]
        public async Task ReportsAVersionThatExistsRegardlessOfCase()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0-Beta"), Ct);
            var result = await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0-beta"), Ct);

            Assert.Equal(ArtifactAddResult.AlreadyExists, result);
            Assert.Equal(1, await Context.ArtifactVersions.CountAsync(Ct));

            // The context is still usable after the failed insert.
            Assert.Equal(ArtifactAddResult.Success, await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.1"), Ct));
        }

        [Fact]
        public async Task KeepsFeedsApart()
        {
            var other = AddFeed("other");

            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            var result = await Target.AddVersionAsync(other.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.Equal(ArtifactAddResult.Success, result);
            Assert.Empty(await Target.FindVersionsAsync(Guid.NewGuid(), "my_package", Ct));
            Assert.Single(await Target.FindVersionsAsync(other.Id, "my_package", Ct));
        }
    }

    public class FindVersionOrNullAsync : FactsBase
    {
        [Fact]
        public async Task FindsAVersionRegardlessOfCase()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0-Beta"), Ct);

            var version = await Target.FindVersionOrNullAsync(Feed.Id, "my_package", "1.0.0-BETA", Ct);

            Assert.Equal("1.0.0-Beta", version.Version);
            Assert.Equal("my_package", version.Artifact.Name);
        }

        [Fact]
        public async Task ReturnsNullForAnUnknownVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.Null(await Target.FindVersionOrNullAsync(Feed.Id, "my_package", "2.0.0", Ct));
            Assert.Null(await Target.FindVersionOrNullAsync(Feed.Id, "other_package", "1.0.0", Ct));
        }
    }

    public class HardDeleteVersionAsync : FactsBase
    {
        [Fact]
        public async Task RemovesOnlyTheVersionWhileOthersRemain()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("2.0.0"), Ct);

            Assert.True(await Target.HardDeleteVersionAsync(Feed.Id, "my_package", "1.0.0", Ct));

            Assert.Equal("2.0.0", (await Target.FindVersionsAsync(Feed.Id, "my_package", Ct)).Single().Version);
        }

        [Fact]
        public async Task RemovesThePackageWithItsLastVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.True(await Target.HardDeleteVersionAsync(Feed.Id, "my_package", "1.0.0", Ct));

            Assert.Equal(0, await Context.Artifacts.CountAsync(Ct));
            Assert.Equal(0, await Context.ArtifactVersions.CountAsync(Ct));
        }

        [Fact]
        public async Task ReturnsFalseForAnUnknownVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.False(await Target.HardDeleteVersionAsync(Feed.Id, "my_package", "2.0.0", Ct));
            Assert.False(await Target.HardDeleteVersionAsync(Feed.Id, "other_package", "1.0.0", Ct));
        }
    }

    public class SetListedAsync : FactsBase
    {
        [Fact]
        public async Task UnlistsAndRelistsAVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.True(await Target.SetListedAsync(Feed.Id, "my_package", "1.0.0", listed: false, Ct));
            Assert.False(await Context.ArtifactVersions.AsNoTracking().Select(v => v.Listed).SingleAsync(Ct));

            Assert.True(await Target.SetListedAsync(Feed.Id, "my_package", "1.0.0", listed: true, Ct));
            Assert.True(await Context.ArtifactVersions.AsNoTracking().Select(v => v.Listed).SingleAsync(Ct));
        }

        [Fact]
        public async Task ReturnsFalseForAnUnknownVersion()
        {
            Assert.False(await Target.SetListedAsync(Feed.Id, "my_package", "1.0.0", listed: false, Ct));
        }
    }

    public class UpdateVersionAsync : FactsBase
    {
        [Fact]
        public async Task ChangesTheListingAndTheManifestTogether()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.True(await Target.UpdateVersionAsync(Feed.Id, "my_package", "1.0.0", listed: false, "{\"deprecated\":\"old\"}", Ct));

            var version = await Context.ArtifactVersions.AsNoTracking().SingleAsync(Ct);
            Assert.False(version.Listed);
            Assert.Equal("{\"deprecated\":\"old\"}", version.MetadataJson);
        }

        [Fact]
        public async Task ReturnsFalseForAnUnknownVersion()
        {
            Assert.False(await Target.UpdateVersionAsync(Feed.Id, "my_package", "1.0.0", listed: true, "{}", Ct));
        }
    }

    public class SetTagAsync : FactsBase
    {
        [Fact]
        public async Task CreatesAndMovesATag()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("2.0.0-Beta"), Ct);

            Assert.True(await Target.SetTagAsync(Feed.Id, "my_package", "next", "1.0.0", Ct));
            Assert.True(await Target.SetTagAsync(Feed.Id, "my_package", "next", "2.0.0-beta", Ct));

            // The tag points at the version as it was published.
            var tag = Assert.Single(await Target.FindTagsAsync(Feed.Id, "my_package", Ct));
            Assert.Equal("next", tag.Name);
            Assert.Equal("2.0.0-Beta", tag.Version);
        }

        [Fact]
        public async Task ReturnsFalseForAnUnknownVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);

            Assert.False(await Target.SetTagAsync(Feed.Id, "my_package", "next", "9.9.9", Ct));
            Assert.False(await Target.SetTagAsync(Feed.Id, "other", "next", "1.0.0", Ct));
            Assert.Empty(await Target.FindTagsAsync(Feed.Id, "my_package", Ct));
        }
    }

    public class RemoveTagAsync : FactsBase
    {
        [Fact]
        public async Task RemovesOnlyThatTag()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            await Target.SetTagAsync(Feed.Id, "my_package", "latest", "1.0.0", Ct);
            await Target.SetTagAsync(Feed.Id, "my_package", "next", "1.0.0", Ct);

            Assert.True(await Target.RemoveTagAsync(Feed.Id, "my_package", "next", Ct));
            Assert.False(await Target.RemoveTagAsync(Feed.Id, "my_package", "next", Ct));

            Assert.Equal("latest", Assert.Single(await Target.FindTagsAsync(Feed.Id, "my_package", Ct)).Name);
        }
    }

    public class HardDeleteVersionTags : FactsBase
    {
        [Fact]
        public async Task RemovesTheTagsOfTheDeletedVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("2.0.0"), Ct);
            await Target.SetTagAsync(Feed.Id, "my_package", "latest", "1.0.0", Ct);
            await Target.SetTagAsync(Feed.Id, "my_package", "next", "2.0.0", Ct);

            await Target.HardDeleteVersionAsync(Feed.Id, "my_package", "2.0.0", Ct);

            Assert.Equal("latest", Assert.Single(await Target.FindTagsAsync(Feed.Id, "my_package", Ct)).Name);

            // The package goes with its last version, and its tags with it.
            await Target.HardDeleteVersionAsync(Feed.Id, "my_package", "1.0.0", Ct);

            Assert.Empty(await Context.ArtifactTags.ToListAsync(Ct));
        }
    }

    public class AddDownloadAsync : FactsBase
    {
        [Fact]
        public async Task IncrementsOnlyThatVersion()
        {
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "my_package", NewVersion("2.0.0"), Ct);

            await Target.AddDownloadAsync(Feed.Id, "my_package", "1.0.0", Ct);
            await Target.AddDownloadAsync(Feed.Id, "my_package", "1.0.0", Ct);

            var downloads = await Context.ArtifactVersions.AsNoTracking().ToDictionaryAsync(v => v.Version, v => v.Downloads, Ct);
            Assert.Equal(2, downloads["1.0.0"]);
            Assert.Equal(0, downloads["2.0.0"]);
        }
    }

    public class SearchAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsEveryPackageOfTheFeedByName()
        {
            var other = AddFeed("other");
            await Target.AddVersionAsync(Feed.Id, "zebra", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "apple", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "apple", NewVersion("2.0.0"), Ct);
            await Target.AddVersionAsync(other.Id, "elsewhere", NewVersion("1.0.0"), Ct);

            var result = await Target.SearchAsync(Feed.Id, query: null, skip: 0, take: 20, Ct);

            Assert.Equal(2, result.TotalHits);
            Assert.Equal(new[] { "apple", "zebra" }, result.Artifacts.Select(a => a.Name));
            Assert.Equal(2, result.Artifacts[0].Versions.Count);
        }

        [Fact]
        public async Task MatchesNamesAndDescriptionsWithNameMatchesFirst()
        {
            await Target.AddVersionAsync(Feed.Id, "alpha", NewVersion("1.0.0", description: "Talks HTTP to servers"), Ct);
            await Target.AddVersionAsync(Feed.Id, "http_client", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "http", NewVersion("1.0.0"), Ct);
            await Target.AddVersionAsync(Feed.Id, "unrelated", NewVersion("1.0.0"), Ct);

            var result = await Target.SearchAsync(Feed.Id, "HTTP", skip: 0, take: 20, Ct);

            Assert.Equal(3, result.TotalHits);
            Assert.Equal(new[] { "http", "http_client", "alpha" }, result.Artifacts.Select(a => a.Name));
        }

        [Fact]
        public async Task Pages()
        {
            foreach (var name in new[] { "a", "b", "c" })
            {
                await Target.AddVersionAsync(Feed.Id, name, NewVersion("1.0.0"), Ct);
            }

            var result = await Target.SearchAsync(Feed.Id, query: null, skip: 2, take: 2, Ct);

            Assert.Equal(3, result.TotalHits);
            Assert.Equal("c", Assert.Single(result.Artifacts).Name);
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext Context;
        protected readonly Feed Feed;
        protected readonly ArtifactDatabase Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            Feed = AddFeed("dart");
            Target = new ArtifactDatabase(Context, new SystemTime());
        }

        protected Feed AddFeed(string slug)
        {
            var feed = new Feed
            {
                Id = Guid.NewGuid(),
                Slug = slug,
                Name = slug,
                Type = FeedType.Pub,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
            };

            Context.Feeds.Add(feed);
            Context.SaveChanges();
            return feed;
        }

        protected static ArtifactVersion NewVersion(string version, string description = null)
        {
            return new ArtifactVersion
            {
                Version = version,
                Listed = true,
                Description = description,
                MetadataJson = "{}",
                Published = DateTime.UtcNow,
            };
        }

        public void Dispose()
        {
            Context?.Dispose();
        }
    }
}
