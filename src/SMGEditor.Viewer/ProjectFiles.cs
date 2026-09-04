using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public static class ProjectFiles
{
    public static string ResolveRoot(string gameRootDir, string? outputDir, string relativePath)
    {
        if (outputDir is not null && File.Exists(Path.Combine(outputDir, relativePath)))
        {
            return outputDir;
        }

        return gameRootDir;
    }

    public static (RARCArchive Archive, bool WasCompressed) LoadArc(string gameRootDir, string? outputDir, string relativePath)
    {
        string root = ResolveRoot(gameRootDir, outputDir, relativePath);
        byte[] raw = File.ReadAllBytes(Path.Combine(root, relativePath));
        return (RARCArchive.Load(raw), Yaz0.IsCompressed(raw));
    }

    public static void SaveArc(string outputDir, string relativePath, RARCArchive archive, bool compress)
    {
        byte[] data = archive.Save();
        if (compress)
        {
            data = Yaz0.Compress(data);
        }

        string fullPath = Path.Combine(outputDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, data);
    }
}
