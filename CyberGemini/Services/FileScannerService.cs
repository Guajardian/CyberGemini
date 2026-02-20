using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CyberGemini.Models;
using CyberGemini.Utilities;

namespace CyberGemini.Services;

public sealed class FileScannerService
{
    private sealed class FileEntry
    {
        public string Path { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public long Size { get; init; }
        public string? Hash { get; set; }
    }

    public sealed record ScanProgress(double Percent, string Message);

    public sealed class ScanOptions
    {
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
        public HashAlgorithmName HashAlgorithm { get; init; } = HashAlgorithmName.SHA256;
        public bool SkipHiddenFiles { get; init; }
        public bool SkipSystemFiles { get; init; }
        public IReadOnlySet<string> ExcludedExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public long? MinFileSizeBytes { get; init; }
        public long? MaxFileSizeBytes { get; init; }
        public PauseToken PauseToken { get; init; }
    }

    public Task<ScanResult> ScanAsync(IEnumerable<string> roots, ScanOptions options, IProgress<ScanProgress>? progress,
        Action<DuplicateFileItem>? onDuplicateFound, CancellationToken cancellationToken)
    {
        return Task.Run(() => ExecuteScan(roots, options, progress, onDuplicateFound, cancellationToken), cancellationToken);
    }

    private ScanResult ExecuteScan(IEnumerable<string> roots, ScanOptions options, IProgress<ScanProgress>? progress,
        Action<DuplicateFileItem>? onDuplicateFound, CancellationToken cancellationToken)
    {
        progress?.Report(new ScanProgress(5, "Enumerating files..."));

        var entries = new ConcurrentBag<FileEntry>();
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, options.MaxDegreeOfParallelism)
        };

        Parallel.ForEach(roots, parallelOptions, root =>
        {
            foreach (var file in EnumerateFilesSafe(root, options, cancellationToken))
            {
                options.PauseToken.WaitWhilePaused(cancellationToken);

                try
                {
                    var info = new FileInfo(file);
                    entries.Add(new FileEntry
                    {
                        Path = file,
                        Name = info.Name,
                        Size = info.Length
                    });
                }
                catch
                {
                    // Ignore files we cannot access.
                }
            }
        });

        var allEntries = entries.ToList();

        progress?.Report(new ScanProgress(30, "Grouping by file size..."));
        var sizeGroups = allEntries.GroupBy(entry => entry.Size)
            .Where(group => group.Count() > 1)
            .ToList();

        // Process each size group individually so duplicates can be streamed to the UI
        var allDuplicateFiles = new List<DuplicateFileItem>();
        var groupId = 1;
        var processedGroups = 0;

        // Track ALL file paths per hash (including the "original") so we can determine folder-essential status
        var allPathsByHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sizeGroup in sizeGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            options.PauseToken.WaitWhilePaused(cancellationToken);

            var groupEntries = sizeGroup.ToList();

            // Hash files in this size group
            Parallel.ForEach(groupEntries, parallelOptions, entry =>
            {
                options.PauseToken.WaitWhilePaused(cancellationToken);
                entry.Hash = TryComputeHash(entry.Path, options.HashAlgorithm);
            });

            // Find hash duplicates within this size group
            var hashGroups = groupEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Hash))
                .GroupBy(entry => entry.Hash!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1);

            foreach (var hashGroup in hashGroups)
            {
                var hashGroupList = hashGroup.ToList();

                // Record ALL paths for this hash (including the original kept by Skip(1))
                if (!allPathsByHash.TryGetValue(hashGroupList[0].Hash!, out var pathList))
                {
                    pathList = new List<string>();
                    allPathsByHash[hashGroupList[0].Hash!] = pathList;
                }
                pathList.AddRange(hashGroupList.Select(e => e.Path));

                // Skip the first file (the "original") — only emit duplicates
                foreach (var entry in hashGroupList.Skip(1))
                {
                    var item = new DuplicateFileItem
                    {
                        GroupId = groupId,
                        FileName = entry.Name,
                        FullPath = entry.Path,
                        Size = entry.Size,
                        Hash = entry.Hash ?? string.Empty
                    };
                    allDuplicateFiles.Add(item);
                    onDuplicateFound?.Invoke(item);
                }

                groupId++;
            }

            processedGroups++;
            var percent = 30 + (int)(50.0 * processedGroups / sizeGroups.Count);
            progress?.Report(new ScanProgress(percent, $"Hashing group {processedGroups}/{sizeGroups.Count}... ({allDuplicateFiles.Count} duplicates found)"));
        }

        // ── Compute folder-essential status ──
        // A duplicate is "folder-essential" if its parent directory has NO other file with the same hash
        // (including the original that was skipped).
        progress?.Report(new ScanProgress(78, "Computing folder safety..."));
        foreach (var item in allDuplicateFiles)
        {
            if (allPathsByHash.TryGetValue(item.Hash, out var allPaths))
            {
                var thisDir = Path.GetDirectoryName(item.FullPath) ?? string.Empty;
                // Count how many files with the same hash exist in the same directory
                var sameHashSameDir = allPaths.Count(p =>
                    string.Equals(Path.GetDirectoryName(p), thisDir, StringComparison.OrdinalIgnoreCase));
                // If this is the only copy in its folder → essential
                item.IsFolderEssential = sameHashSameDir <= 1;
            }
        }

        progress?.Report(new ScanProgress(80, "Checking for shared file names..."));
        var nameDuplicates = allEntries
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .Select(entry => new NameDuplicateItem
            {
                FileName = entry.Name,
                FullPath = entry.Path,
                NewFileName = entry.Name
            })
            .ToList();

        progress?.Report(new ScanProgress(100, "Scan complete."));

        return new ScanResult
        {
            DuplicateFiles = allDuplicateFiles,
            NameDuplicates = nameDuplicates
        };
    }

    private static string? TryComputeHash(string path, HashAlgorithmName algorithmName)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var algorithm = CreateAlgorithm(algorithmName);
            var hash = algorithm.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null;
        }
    }

    private static HashAlgorithm CreateAlgorithm(HashAlgorithmName name)
    {
        return name.Name?.ToUpperInvariant() switch
        {
            "SHA512" => SHA512.Create(),
            "SHA384" => SHA384.Create(),
            "SHA1" => SHA1.Create(),
            _ => SHA256.Create()
        };
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, ScanOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            string[] files = Array.Empty<string>();
            string[] directories = Array.Empty<string>();

            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                if (ShouldSkip(file, options))
                {
                    continue;
                }

                yield return file;
            }

            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static bool ShouldSkip(string filePath, ScanOptions options)
    {
        try
        {
            var attributes = File.GetAttributes(filePath);
            if (options.SkipHiddenFiles && attributes.HasFlag(FileAttributes.Hidden))
            {
                return true;
            }

            if (options.SkipSystemFiles && attributes.HasFlag(FileAttributes.System))
            {
                return true;
            }

            var extension = Path.GetExtension(filePath);
            if (!string.IsNullOrWhiteSpace(extension) && options.ExcludedExtensions.Contains(extension))
            {
                return true;
            }

            if (options.MinFileSizeBytes.HasValue || options.MaxFileSizeBytes.HasValue)
            {
                var info = new FileInfo(filePath);
                if (options.MinFileSizeBytes.HasValue && info.Length < options.MinFileSizeBytes.Value)
                {
                    return true;
                }

                if (options.MaxFileSizeBytes.HasValue && info.Length > options.MaxFileSizeBytes.Value)
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }
}