using System.Text.Json;
using System.Text.Json.Serialization;

namespace SMGEditor.Core.Database;

public sealed class ObjectDbValueOption
{
    [JsonPropertyName("Value")]
    public string Value { get; init; } = "";

    [JsonPropertyName("Notes")]
    public string Notes { get; init; } = "";
}

public sealed class ObjectDbParameter
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Type")]
    public string? Type { get; init; }

    [JsonPropertyName("Description")]
    public string? Description { get; init; }

    [JsonPropertyName("Values")]
    public List<ObjectDbValueOption> Values { get; init; } = [];

    [JsonPropertyName("Exclusives")]
    public List<string> Exclusives { get; init; } = [];
}

public sealed class ObjectDbClass
{
    [JsonPropertyName("InternalName")]
    public string InternalName { get; init; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("Parameters")]
    public Dictionary<string, ObjectDbParameter> Parameters { get; init; } = [];
}

public sealed class ObjectDbEntry
{
    [JsonPropertyName("InternalName")]
    public string InternalName { get; init; } = "";

    [JsonPropertyName("ClassNameSMG1")]
    public string? ClassNameSMG1 { get; init; }

    [JsonPropertyName("ClassNameSMG2")]
    public string? ClassNameSMG2 { get; init; }

    [JsonPropertyName("Name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("Notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("Category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("ListSMG1")]
    public string? ListSMG1 { get; init; }

    [JsonPropertyName("ListSMG2")]
    public string? ListSMG2 { get; init; }

    [JsonPropertyName("AreaShape")]
    public string? AreaShape { get; init; }

    public string? ClassName(int game) => game == 1 ? ClassNameSMG1 : ClassNameSMG2;

    public string? ListName(int game) => game == 1 ? ListSMG1 : ListSMG2;
}

public sealed class ObjectDbCategory
{
    [JsonPropertyName("Key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("Description")]
    public string Description { get; init; } = "";
}

public sealed class ObjectDatabase
{
    // should this be changeable?
    private const string SourceUrl = "https://raw.githubusercontent.com/SMGCommunity/galaxydatabase/main/objectdb.json";

    public required IReadOnlyDictionary<string, ObjectDbEntry> ObjectsByInternalName { get; init; }
    public required IReadOnlyDictionary<string, ObjectDbClass> ClassesByInternalName { get; init; }
    public required IReadOnlyList<ObjectDbCategory> Categories { get; init; }

    public ObjectDbEntry? FindObject(string internalName) =>
        ObjectsByInternalName.GetValueOrDefault(internalName);

    public ObjectDbClass? FindClass(string? internalName) =>
        internalName is null ? null : ClassesByInternalName.GetValueOrDefault(internalName);

    public string CategoryDisplayName(string key) =>
        Categories.FirstOrDefault(c => c.Key == key)?.Description ?? key;

    public static async Task<ObjectDatabase> LoadOrDownloadAsync(string cachePath)
    {
        if (!File.Exists(cachePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            using var http = new HttpClient();
            byte[] data = await http.GetByteArrayAsync(SourceUrl);
            await File.WriteAllBytesAsync(cachePath, data);
        }

        return Parse(await File.ReadAllBytesAsync(cachePath));
    }

    private static ObjectDatabase Parse(byte[] json)
    {
        JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        var objects = JsonSerializer.Deserialize<List<ObjectDbEntry>>(root.GetProperty("Objects").GetRawText()) ?? [];
        var classes = JsonSerializer.Deserialize<List<ObjectDbClass>>(root.GetProperty("Classes").GetRawText()) ?? [];
        var categories = JsonSerializer.Deserialize<List<ObjectDbCategory>>(root.GetProperty("Categories").GetRawText()) ?? [];

        return new ObjectDatabase
        {
            ObjectsByInternalName = objects.ToDictionary(o => o.InternalName, o => o, StringComparer.OrdinalIgnoreCase),
            ClassesByInternalName = classes.ToDictionary(c => c.InternalName, c => c, StringComparer.OrdinalIgnoreCase),
            Categories = categories,
        };
    }
}
