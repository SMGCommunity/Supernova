using System.Reflection;

namespace SMGEditor.Core.Formats;

/* implementation of a fast BCSV hash lookup table */
public static class BCSVHashLookup
{
    private static readonly Lazy<IReadOnlyDictionary<uint, string>> LazyNames = new(Load);

    public static uint Hash(string value)
    {
        uint hash = 0;
        foreach (char c in value)
        {
            hash = (byte)c + hash * 31;
        }

        return hash;
    }

    public static string Resolve(uint hash) =>
        LazyNames.Value.TryGetValue(hash, out string? name) ? name : $"[{hash:X8}]";

    private static IReadOnlyDictionary<uint, string> Load()
    {
        var names = new Dictionary<uint, string>();

        using Stream? stream = typeof(BCSVHashLookup).Assembly.GetManifestResourceStream(
            "SMGEditor.Core.Data.bcsv_hashlookup.txt");
        if (stream is null)
        {
            return names;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            names[Hash(line)] = line;
        }

        return names;
    }
}
