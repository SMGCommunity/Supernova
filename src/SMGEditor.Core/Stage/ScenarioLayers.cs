namespace SMGEditor.Core.Stage;

public static class ScenarioLayers
{
    public static readonly string[] LayerDirNames =
    [
        "LayerA", "LayerB", "LayerC", "LayerD", "LayerE", "LayerF", "LayerG", "LayerH",
        "LayerI", "LayerJ", "LayerK", "LayerL", "LayerM", "LayerN", "LayerO", "LayerP",
    ];

    public static IReadOnlyList<string> Resolve(IReadOnlyDictionary<string, object?> scenarioRow, string zoneName)
    {
        var layers = new List<string> { "Common" };

        if (scenarioRow.TryGetValue(zoneName, out object? rawMask) && rawMask is int mask)
        {
            for (int bit = 0; bit < LayerDirNames.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    layers.Add(LayerDirNames[bit]);
                }
            }
        }

        return layers;
    }
}
