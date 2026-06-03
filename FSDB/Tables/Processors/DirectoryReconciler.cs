using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FSDB.Helpers;
using FSDB.Index;
using FSDB.Logging;
using FSDB.Scheduling;
using Microsoft.Extensions.Logging;

namespace FSDB.Tables.Processors;

internal sealed class DirectoryReconciler<TKey, TRecord, TProjection>(
    string tablePath,
    TableIndex<TKey, TRecord, TProjection> index,
    FileReconciler<TKey, TRecord, TProjection> fileReconciler,
    Action<string> requestFileReconcile,
    ILogger logger)
    where TRecord : class, IRecord<TKey>
    where TKey : notnull
{
    public async Task<ProcessResult> ReconcileAsync(CancellationToken ct = default)
    {
        using var _ = logger.BeginMethodScope();
        var stopwatch = Stopwatch.StartNew();
        logger.LogDebug("Rescan started: path=\"{Path}\"", tablePath);

        IEnumerable<string> filesOnDisk;
        try
        {
            filesOnDisk = Directory.GetFiles(tablePath, "*.json", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path));
        }
        catch (DirectoryNotFoundException)
        {
            using (var exclusiveIndexScope = await index.EnterExclusiveScopeAsync(ct))
            {
                logger.LogWarning("Rescan directory missing, index cleared: path=\"{Path}\"", tablePath);
                exclusiveIndexScope.Clear();
            }

            return ProcessResult.Complete;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rescan directory read failed, will retry: path=\"{Path}\"", tablePath);
            return ProcessResult.RetryWithBackoff;
        }

        HashSet<string> processingFileNames;
        using (var sharedIndexScope = await index.EnterSharedScopeAsync(ct))
        {
            processingFileNames = filesOnDisk
                .Union(sharedIndexScope.Files.Keys)
                .ToHashSet(PathHelper.OSDependedPathComparer);
        }

        foreach (var fileName in processingFileNames)
        {
            var filePath = Path.Combine(tablePath, fileName);
            var result = await fileReconciler.ReconcileAsync(filePath, ct);
            if (result != ProcessResult.Complete)
            {
                requestFileReconcile(filePath);
            }
        }

        using (var sharedIndexScope = await index.EnterSharedScopeAsync(ct))
        {
            logger.LogDebug(
                "Rescan completed: path=\"{Path}\" files={Files} entries={Entries} durationMs={DurationMs}",
                tablePath,
                processingFileNames.Count,
                sharedIndexScope.Records.Count,
                stopwatch.ElapsedMilliseconds);
        }

        return ProcessResult.Complete;
    }
}
