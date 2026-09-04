using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public static class ProjectFiles
{
    public static string FilesRoot(string root)
    {
        string classic = Path.Combine(root, "DATA", "files");
        if (Directory.Exists(classic))
        {
            return classic;
        }

        string files = Path.Combine(root, "files");
        if (Directory.Exists(files))
        {
            return files;
        }

        return root;
    }

    public static string GameFilePath(string root, string logicalRelativePath)
    {
        string normalized = logicalRelativePath.Replace('\\', '/');
        if (normalized.StartsWith("DATA/files/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["DATA/files/".Length..];
        }

        return Path.Combine(FilesRoot(root), normalized);
    }

    public static string ResolveFile(string gameRootDir, string? outputDir, string logicalRelativePath)
    {
        if (outputDir is not null)
        {
            string outputPath = GameFilePath(outputDir, logicalRelativePath);
            if (File.Exists(outputPath))
            {
                return outputPath;
            }
        }

        return GameFilePath(gameRootDir, logicalRelativePath);
    }

    public static (RARCArchive Archive, bool WasCompressed) LoadArc(string gameRootDir, string? outputDir, string relativePath)
    {
        byte[] raw = File.ReadAllBytes(ResolveFile(gameRootDir, outputDir, relativePath));
        return (RARCArchive.Load(raw), Yaz0.IsCompressed(raw));
    }

    public static void SaveArc(string outputDir, string relativePath, RARCArchive archive, bool compress)
    {
        byte[] data = archive.Save();
        if (compress)
        {
            data = Yaz0.Compress(data);
        }

        string fullPath = GameFilePath(outputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, data);
    }
}
