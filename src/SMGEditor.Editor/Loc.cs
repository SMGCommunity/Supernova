using System.Text.Json;

namespace SMGEditor.Editor;

internal static class Loc
{
    private static Dictionary<string, string> _map = [];
    private static string _langDir = "";

    public static string CurrentLanguage { get; private set; } = "en";

    public static List<(string Code, string Name)> Available { get; } = [("en", "English")];

    public static void Init(string langDir, string languageCode)
    {
        _langDir = langDir;
        Available.RemoveAll(l => l.Code != "en");

        if (Directory.Exists(langDir))
        {
            foreach (string file in Directory.EnumerateFiles(langDir, "*.json"))
            {
                string code = Path.GetFileNameWithoutExtension(file);
                if (!code.Equals("en", StringComparison.OrdinalIgnoreCase))
                {
                    Available.Add((code, ReadLanguageName(file) ?? code));
                }
            }
        }

        SetLanguage(languageCode);
    }

    public static void SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            _map = [];
            CurrentLanguage = "en";
            return;
        }

        string path = Path.Combine(_langDir, code + ".json");
        try
        {
            _map = ParseFile(File.ReadAllText(path));
            CurrentLanguage = code;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Loc] {code}: {ex.Message}");
            _map = [];
            CurrentLanguage = "en";
        }
    }

    public static string L(string english) =>
        _map.TryGetValue(english, out string? translated) && translated.Length > 0 ? translated : english;

    public static string LF(string englishFormat, params object?[] args) =>
        string.Format(L(englishFormat), args);

    private static Dictionary<string, string> ParseFile(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>();
        foreach (JsonProperty property in doc.RootElement.EnumerateObject())
        {
            if (!property.Name.StartsWith('$') && property.Value.ValueKind == JsonValueKind.String)
            {
                result[property.Name] = property.Value.GetString() ?? "";
            }
        }

        return result;
    }

    private static string? ReadLanguageName(string file)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("$language", out JsonElement name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
