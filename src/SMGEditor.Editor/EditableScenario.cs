using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal sealed class EditableScenario
{
    public required Dictionary<string, object?> Fields { get; init; }

    public int ScenarioNo
    {
        get => Fields.TryGetValue("ScenarioNo", out object? v) && v is int i ? i : 0;
        set => Fields["ScenarioNo"] = value;
    }

    public string ScenarioName
    {
        get => Fields.TryGetValue("ScenarioName", out object? v) && v is string s ? s : "";
        set => Fields["ScenarioName"] = value;
    }

    public string DisplayName => string.IsNullOrWhiteSpace(ScenarioName) ? $"Scenario {ScenarioNo}" : ScenarioName;

    public string ListLabel => $"[{ScenarioNo}] {DisplayName}";

    public int PowerStarId
    {
        get => Fields.TryGetValue("PowerStarId", out object? v) && v is int i ? i : 0;
        set => Fields["PowerStarId"] = value;
    }

    public string PowerStarType
    {
        get => Fields.TryGetValue("PowerStarType", out object? v) && v is string s ? s : "Normal";
        set => Fields["PowerStarType"] = value;
    }

    public string Comet
    {
        get => Fields.TryGetValue("Comet", out object? v) && v is string s ? s : "";
        set => Fields["Comet"] = value;
    }

    public string AppearPowerStarObj
    {
        get => Fields.TryGetValue("AppearPowerStarObj", out object? v) && v is string s ? s : "";
        set => Fields["AppearPowerStarObj"] = value;
    }

    public int CometLimitTimer
    {
        get => Fields.TryGetValue("CometLimitTimer", out object? v) && v is int i ? i : 0;
        set => Fields["CometLimitTimer"] = value;
    }

    public bool IsHidden
    {
        get => Fields.TryGetValue("IsHidden", out object? v) && v is int i && i != 0;
        set => Fields["IsHidden"] = value ? 1 : 0;
    }

    public int GetLayerMask(string zoneName) => Fields.TryGetValue(zoneName, out object? v) && v is int i ? i : 0;

    public void SetLayerMask(string zoneName, int mask) => Fields[zoneName] = mask;

    public static EditableScenario FromInfo(GalaxyLoader.ScenarioInfo info) =>
        new() { Fields = new Dictionary<string, object?>(info.Fields) };

    public static EditableScenario CreateNew(IEnumerable<EditableScenario> existing)
    {
        int nextNo = existing.Select(s => s.ScenarioNo).DefaultIfEmpty(0).Max() + 1;
        return new EditableScenario
        {
            Fields = new Dictionary<string, object?>
            {
                ["ScenarioNo"] = nextNo,
                ["ScenarioName"] = $"Scenario {nextNo}",
                ["PowerStarId"] = 0,
                ["AppearPowerStarObj"] = "",
                ["PowerStarType"] = "Normal",
                ["Comet"] = "",
                ["CometLimitTimer"] = 0,
            },
        };
    }
}
