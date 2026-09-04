using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: smg-l10n <editor-source-dir> <output-en.json>");
    return 1;
}

string sourceDir = args[0];
string outputPath = args[1];

if (!Directory.Exists(sourceDir))
{
    Console.Error.WriteLine($"not a directory: {sourceDir}");
    return 1;
}

var call = new Regex("""(?<![\w.])L[fF]?\(\s*"((?:[^"\\]|\\.)*)"\s*[,)]""", RegexOptions.Compiled);

var strings = new SortedSet<string>(StringComparer.Ordinal);
int fileCount = 0;

foreach (string file in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
{
    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || file.EndsWith("Loc.cs", StringComparison.Ordinal))
    {
        continue;
    }

    fileCount++;
    string text = File.ReadAllText(file);
    foreach (Match match in call.Matches(text))
    {
        strings.Add(Unescape(match.Groups[1].Value));
    }
}

var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (string s in strings)
{
    catalog[s] = s;
}

var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
File.WriteAllText(outputPath, JsonSerializer.Serialize(catalog, options), new UTF8Encoding(false));

Console.WriteLine($"{strings.Count} string(s) from {fileCount} file(s) -> {outputPath}");
return 0;

static string Unescape(string raw)
{
    var sb = new StringBuilder(raw.Length);
    for (int i = 0; i < raw.Length; i++)
    {
        if (raw[i] != '\\' || i + 1 >= raw.Length)
        {
            sb.Append(raw[i]);
            continue;
        }

        i++;
        sb.Append(raw[i] switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '"' => '"',
            '\\' => '\\',
            '0' => '\0',
            _ => raw[i],
        });
    }

    return sb.ToString();
}
