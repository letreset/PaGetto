using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The gems of a RubyGems feed (<see cref="FeedType.RubyGems"/>): those pushed to it, combined with
/// those of its mirrors. A gem is an <see cref="Artifact"/> with its lowercased name, and its gem
/// files, one per platform, are <see cref="ArtifactFile"/> rows of their version. The gemspecs are
/// the version's manifest (<see cref="RubyGemsManifest"/>). An unlisted version is yanked.
/// </summary>
public interface IRubyGemsPackageService
{
    /// <summary>
    /// The compact index's <c>/versions</c>: the gems of the feed, then those of its mirrors.
    /// </summary>
    Task<byte[]> GetVersionsFileAsync(Feed feed, CancellationToken cancellationToken);

    /// <summary>
    /// The compact index's <c>/info/{gem}</c>: a line per gem file of the listed versions, those of
    /// the feed and those of its mirror. A version pushed to the feed hides the same version of
    /// the mirror, and a version unlisted in the feed is left out.
    /// </summary>
    /// <returns>The file, or null if neither the feed nor its mirror has the gem.</returns>
    Task<byte[]> GetInfoFileOrNullAsync(Feed feed, string name, CancellationToken cancellationToken);

    /// <summary>
    /// The compact index's <c>/names</c>: the gems stored in the feed. Gems that only a mirror has
    /// are not listed: a mirror such as rubygems.org has too many.
    /// </summary>
    Task<byte[]> GetNamesFileAsync(Feed feed, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a gem file. A gem that only the mirror has is downloaded from it and stored in the feed first.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="fullName">The file name without <c>.gem</c>.</param>
    /// <param name="countDownload">Whether the download is counted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The gem, or null if there is no such gem file.</returns>
    Task<Stream> GetGemStreamOrNullAsync(Feed feed, string fullName, bool countDownload, CancellationToken cancellationToken);

    /// <summary>
    /// The <c>quick/Marshal.4.8</c> gemspec of a gem file, written from the stored gemspec, or the
    /// mirror's for a gem file the feed doesn't have.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="fullName">The file name without <c>.gemspec.rz</c>.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The compressed gemspec, or null if there is no such gem file.</returns>
    Task<byte[]> GetQuickSpecOrNullAsync(Feed feed, string fullName, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a gem pushed with <c>gem push</c>. A version gets a gem file per platform.
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="gem">The gem file.</param>
    /// <param name="maxBytes">The most the gem may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    Task<RubyGemsPushResult> PushAsync(Feed feed, Stream gem, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Unlists a version, with all its platforms (<c>gem yank</c>).
    /// </summary>
    /// <param name="feed">The feed.</param>
    /// <param name="name">The gem name.</param>
    /// <param name="version">The version.</param>
    /// <param name="platform">The platform the client named, which the version must have, or null.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>False if the feed has no such version.</returns>
    Task<bool> YankAsync(Feed feed, string name, string version, string platform, CancellationToken cancellationToken);
}
