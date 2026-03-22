namespace TorrentCore.Service.Engine;

public static class TorrentDataPathCleanup
{
    public static void DeletePayloadArtifacts(string downloadRootPath, IEnumerable<string?> candidatePaths)
    {
        var normalizedDownloadRootPath = NormalizeDirectoryPath(downloadRootPath);
        var normalizedCandidates = candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path!))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(path => path.Length)
            .ToArray();

        foreach (var candidatePath in normalizedCandidates)
        {
            DeletePayloadArtifact(candidatePath, normalizedDownloadRootPath);
        }
    }

    public static void DeleteEmptyDirectories(string downloadRootPath, IEnumerable<string?> candidatePaths)
    {
        var normalizedDownloadRootPath = NormalizeDirectoryPath(downloadRootPath);
        var candidateDirectories = candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizePath(path!))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(path => path.Length)
            .ToArray();

        foreach (var candidateDirectory in candidateDirectories)
        {
            DeleteEmptyDirectoryChain(candidateDirectory, normalizedDownloadRootPath);
        }
    }

    private static void DeletePayloadArtifact(string candidatePath, string normalizedDownloadRootPath)
    {
        if (!IsWithinOrEqual(candidatePath, normalizedDownloadRootPath) ||
            string.Equals(candidatePath, normalizedDownloadRootPath, StringComparison.Ordinal))
        {
            return;
        }

        if (File.Exists(candidatePath))
        {
            File.Delete(candidatePath);
            return;
        }

        if (Directory.Exists(candidatePath))
        {
            Directory.Delete(candidatePath, recursive: true);
        }
    }

    private static void DeleteEmptyDirectoryChain(string candidateDirectory, string normalizedDownloadRootPath)
    {
        var currentDirectory = candidateDirectory;

        while (!string.IsNullOrWhiteSpace(currentDirectory) &&
               IsWithinOrEqual(currentDirectory, normalizedDownloadRootPath) &&
               !string.Equals(currentDirectory, normalizedDownloadRootPath, StringComparison.Ordinal))
        {
            if (!Directory.Exists(currentDirectory))
            {
                currentDirectory = Path.GetDirectoryName(currentDirectory);
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(currentDirectory).Any())
            {
                break;
            }

            Directory.Delete(currentDirectory);
            currentDirectory = Path.GetDirectoryName(currentDirectory);
        }
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsWithinOrEqual(string path, string rootPath)
    {
        return string.Equals(path, rootPath, StringComparison.Ordinal) ||
               path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
