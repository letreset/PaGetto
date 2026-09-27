using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Protocol.Extensions;
using PaGetto.Protocol.Models;
using Microsoft.Extensions.Logging;

namespace PaGetto.Protocol.Catalog;

/// <summary>
/// Processes catalog leafs in chronological order.
/// </summary>    
/// <remarks>
/// See: <see href="https://docs.microsoft.com/en-us/nuget/api/catalog-resource"/>
/// </remarks>
public partial class CatalogProcessor
{
    private readonly ICatalogLeafProcessor _leafProcessor;
    private readonly ICatalogClient _client;
    private readonly ICursor _cursor;
    private readonly CatalogProcessorOptions _options;
    private readonly ILogger<CatalogProcessor> _logger;

    /// <summary>
    /// Create a processor to discover and download catalog leafs. Leafs are processed
    /// by the <see cref="ICatalogLeafProcessor"/>.
    /// </summary>
    /// <param name="cursor">Cursor to track succesfully processed leafs. Leafs before the cursor are skipped.</param>
    /// <param name="client">The client to interact with the catalog resource.</param>
    /// <param name="leafProcessor">The leaf processor.</param>
    /// <param name="options">The options to configure catalog processing.</param>
    /// <param name="logger">The logger used for telemetry.</param>
    public CatalogProcessor(
        ICursor cursor,
        ICatalogClient client,
        ICatalogLeafProcessor leafProcessor,
        CatalogProcessorOptions options,
        ILogger<CatalogProcessor> logger)
    {
        _leafProcessor = leafProcessor ?? throw new ArgumentNullException(nameof(leafProcessor));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Discovers and downloads all of the catalog leafs after the current cursor value and before the maximum
    /// commit timestamp found in the settings. Each catalog leaf is passed to the catalog leaf processor in
    /// chronological order. After a commit is completed, its commit timestamp is written to the cursor, i.e. when
    /// transitioning from commit timestamp A to B, A is written to the cursor so that it never is processed again.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>True if all of the catalog leaves found were processed successfully.</returns>
    public async Task<bool> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var minCommitTimestamp = await GetMinCommitTimestamp(cancellationToken);
        LogTimeBounds(minCommitTimestamp, _options.MaxCommitTimestamp);

        return await ProcessIndexAsync(minCommitTimestamp, cancellationToken);
    }

    private async Task<bool> ProcessIndexAsync(DateTimeOffset minCommitTimestamp, CancellationToken cancellationToken)
    {
        var index = await _client.GetIndexAsync(cancellationToken);

        var pageItems = index.GetPagesInBounds(
            minCommitTimestamp,
            _options.MaxCommitTimestamp);
        LogPagesInBounds(pageItems.Count, index.Items.Count);

        var success = true;
        for (var i = 0; i < pageItems.Count; i++)
        {
            success = await ProcessPageAsync(minCommitTimestamp, pageItems[i], cancellationToken);
            if (!success)
            {
                LogPagesIncomplete(pageItems.Count - i, pageItems.Count);
                break;
            }
        }

        return success;
    }

    private async Task<bool> ProcessPageAsync(
        DateTimeOffset minCommitTimestamp,
        CatalogPageItem pageItem,
        CancellationToken cancellationToken)
    {
        var page = await _client.GetPageAsync(pageItem.CatalogPageUrl, cancellationToken);

        var leafItems = page.GetLeavesInBounds(
            minCommitTimestamp,
            _options.MaxCommitTimestamp,
            _options.ExcludeRedundantLeaves);
        LogLeavesInBounds(pageItem.CatalogPageUrl, leafItems.Count, page.Items.Count);

        DateTimeOffset? newCursor = null;
        var success = true;
        for (var i = 0; i < leafItems.Count; i++)
        {
            var leafItem = leafItems[i];

            if (newCursor.HasValue && newCursor.Value != leafItem.CommitTimestamp)
            {
                await _cursor.SetAsync(newCursor.Value, cancellationToken);
            }

            newCursor = leafItem.CommitTimestamp;

            success = await ProcessLeafAsync(leafItem, cancellationToken);
            if (!success)
            {
                LogLeavesIncomplete(leafItems.Count - i, leafItems.Count);
                break;
            }
        }

        if (newCursor.HasValue && success)
        {
            await _cursor.SetAsync(newCursor.Value, cancellationToken);
        }

        return success;
    }

    private async Task<bool> ProcessLeafAsync(CatalogLeafItem leafItem, CancellationToken cancellationToken)
    {
        bool success;
        try
        {
            if (leafItem.IsPackageDelete())
            {
                var packageDelete = await _client.GetPackageDeleteLeafAsync(leafItem.CatalogLeafUrl, cancellationToken);
                success = await _leafProcessor.ProcessPackageDeleteAsync(packageDelete, cancellationToken);
            }
            else if (leafItem.IsPackageDetails())
            {
                var packageDetails = await _client.GetPackageDetailsLeafAsync(leafItem.CatalogLeafUrl, cancellationToken);
                success = await _leafProcessor.ProcessPackageDetailsAsync(packageDetails, cancellationToken);
            }
            else
            {
                throw new NotSupportedException($"The catalog leaf type '{leafItem.Type}' is not supported.");
            }
        }
        catch (Exception exception)
        {
            LogLeafException(exception, leafItem.CatalogLeafUrl);
            success = false;
        }

        if (!success)
        {
            LogLeafFailed(leafItem.CatalogLeafUrl, leafItem.PackageId, leafItem.PackageVersion, leafItem.Type);
        }

        return success;
    }

    private async Task<DateTimeOffset> GetMinCommitTimestamp(CancellationToken cancellationToken)
    {
        var minCommitTimestamp = await _cursor.GetAsync(cancellationToken);

        minCommitTimestamp ??= _options.DefaultMinCommitTimestamp
            ?? _options.MinCommitTimestamp;

        if (minCommitTimestamp.Value < _options.MinCommitTimestamp)
        {
            minCommitTimestamp = _options.MinCommitTimestamp;
        }

        return minCommitTimestamp.Value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Using time bounds {min:O} (exclusive) to {max:O} (inclusive).")]
    private partial void LogTimeBounds(DateTimeOffset min, DateTimeOffset max);

    [LoggerMessage(Level = LogLevel.Information, Message = "{pages} pages were in the time bounds, out of {totalPages}.")]
    private partial void LogPagesInBounds(int pages, int totalPages);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{unprocessedPages} out of {pages} pages were left incomplete due to a processing failure.")]
    private partial void LogPagesIncomplete(int unprocessedPages, int pages);

    [LoggerMessage(Level = LogLevel.Information, Message = "On page {page}, {leaves} out of {totalLeaves} were in the time bounds.")]
    private partial void LogLeavesInBounds(string page, int leaves, int totalLeaves);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{unprocessedLeaves} out of {leaves} leaves were left incomplete due to a processing failure.")]
    private partial void LogLeavesIncomplete(int unprocessedLeaves, int leaves);

    [LoggerMessage(Level = LogLevel.Error, Message = "An exception was thrown while processing leaf {leafUrl}.")]
    private partial void LogLeafException(Exception exception, string leafUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process leaf {leafUrl} ({packageId} {packageVersion}, {leafType}).")]
    private partial void LogLeafFailed(string leafUrl, string packageId, string packageVersion, string leafType);
}
