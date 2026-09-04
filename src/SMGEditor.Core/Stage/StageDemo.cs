using SMGEditor.Core.Formats;

namespace SMGEditor.Core.Stage;

public enum DemoActionType
{
    Appear = 0,
    Disappear = 1,
    Functor = 2,
    Nerve = 3,
    SwitchAOn = 4,
    SwitchBOn = 5,
    ShowModel = 6,
    HideModel = 7,
    TalkKeepPause = 8,
    TalkNoPauseResetInterp = 9,
    TalkNoPause = 10,
    None = 11,
    SwitchAOff = 12,
    SwitchBOff = 13,
}

public sealed class DemoActionEntry
{
    public required string PartName { get; set; }
    public string? CastName { get; set; }
    public int CastId { get; set; }
    public DemoActionType ActionType { get; set; }
    public string? PosName { get; set; }
    public string? AnimName { get; set; }
}

public sealed class DemoCameraEntry
{
    public required string PartName { get; set; }
    public string? CameraTargetName { get; set; }
    public int CameraTargetCastId { get; set; }
    public string? AnimCameraName { get; set; }
    public int AnimCameraStartFrame { get; set; }
    public int AnimCameraEndFrame { get; set; }
    public bool IsContinuous { get; set; }
}

public sealed class DemoPlayerEntry
{
    public required string PartName { get; set; }
    public string? PosName { get; set; }
    public string? BCKName { get; set; }
}

public sealed class DemoSoundEntry
{
    public required string PartName { get; set; }
    public string? Bgm { get; set; }
    public string? SystemSe { get; set; }
    public string? ActionSe { get; set; }
    public bool ReturnBgm { get; set; }
    public int BgmWipeoutFrame { get; set; }
    public int? AllSoundStopFrame { get; set; }
}

public sealed class DemoSubPartEntry
{
    public required string SubPartName { get; set; }
    public int SubPartTotalStep { get; set; }
    public required string MainPartName { get; set; }
    public int MainPartStep { get; set; }
}

public sealed class DemoTimeEntry
{
    public required string PartName { get; set; }
    public int TotalStep { get; set; }
    public bool SuspendFlag { get; set; }
    public bool? WaitUserInputFlag { get; set; }
}

public sealed class DemoWipeEntry
{
    public required string PartName { get; set; }
    public string? WipeName { get; set; }
    public int WipeType { get; set; }
    public int WipeFrame { get; set; }
}

public sealed class DemoTimeline
{
    public required string TimeSheetName { get; init; }
    public required IReadOnlyList<DemoTimeEntry> TimeEntries { get; init; }
    public required IReadOnlyList<DemoActionEntry> ActionEntries { get; init; }
    public required IReadOnlyList<DemoCameraEntry> CameraEntries { get; init; }
    public required IReadOnlyList<DemoPlayerEntry> PlayerEntries { get; init; }
    public required IReadOnlyList<DemoSoundEntry> SoundEntries { get; init; }
    public required IReadOnlyList<DemoSubPartEntry> SubPartEntries { get; init; }
    public required IReadOnlyList<DemoWipeEntry> WipeEntries { get; init; }
}

public static class StageDemoReader
{
    private const string SheetFolder = "csv";

    public static List<string> ListTimeSheetNames(RARCArchive demoArchive)
    {
        RARCDirectory? sheetDir = demoArchive.Root.FindDirectory(SheetFolder);
        if (sheetDir is null)
        {
            return [];
        }

        var names = new List<string>();
        const string prefix = "Demo";
        const string suffix = "Time";

        foreach (RARCFile file in sheetDir.Files)
        {
            string fileName = file.Name.EndsWith(".bcsv", StringComparison.OrdinalIgnoreCase)
                ? file.Name[..^".bcsv".Length]
                : file.Name;

            if (fileName.StartsWith(prefix, StringComparison.Ordinal) && fileName.EndsWith(suffix, StringComparison.Ordinal) &&
                fileName.Length > prefix.Length + suffix.Length)
            {
                names.Add(fileName[prefix.Length..^suffix.Length]);
            }
        }

        return names;
    }

    public static DemoTimeline? ReadTimeline(RARCArchive demoArchive, string timeSheetName)
    {
        RARCDirectory? sheetDir = demoArchive.Root.FindDirectory(SheetFolder);
        if (sheetDir is null)
        {
            return null;
        }

        BCSVTable? timeTable = LoadSheet(sheetDir, timeSheetName, "Time");
        if (timeTable is null)
        {
            return null;
        }

        return new DemoTimeline
        {
            TimeSheetName = timeSheetName,
            TimeEntries = timeTable.Rows.Select(r => new DemoTimeEntry
            {
                PartName = Str(r, "PartName") ?? "",
                TotalStep = Int(r, "TotalStep"),
                SuspendFlag = Bool(r, "SuspendFlag"),
                WaitUserInputFlag = BoolOrNull(r, "WaitUserInputFlag"),
            }).ToList(),
            ActionEntries = LoadSheet(sheetDir, timeSheetName, "Action")?.Rows.Select(r => new DemoActionEntry
            {
                PartName = Str(r, "PartName") ?? "",
                CastName = Str(r, "CastName"),
                CastId = Int(r, "CastID"),
                ActionType = (DemoActionType)Int(r, "ActionType"),
                PosName = Str(r, "PosName"),
                AnimName = Str(r, "AnimName"),
            }).ToList() ?? [],
            CameraEntries = LoadSheet(sheetDir, timeSheetName, "Camera")?.Rows.Select(r => new DemoCameraEntry
            {
                PartName = Str(r, "PartName") ?? "",
                CameraTargetName = Str(r, "CameraTargetName"),
                CameraTargetCastId = Int(r, "CameraTargetCastID"),
                AnimCameraName = Str(r, "AnimCameraName"),
                AnimCameraStartFrame = Int(r, "AnimCameraStartFrame"),
                AnimCameraEndFrame = Int(r, "AnimCameraEndFrame"),
                IsContinuous = Bool(r, "IsContinuous"),
            }).ToList() ?? [],
            PlayerEntries = LoadSheet(sheetDir, timeSheetName, "Player")?.Rows.Select(r => new DemoPlayerEntry
            {
                PartName = Str(r, "PartName") ?? "",
                PosName = Str(r, "PosName"),
                BCKName = Str(r, "BCKName"),
            }).ToList() ?? [],
            SoundEntries = LoadSheet(sheetDir, timeSheetName, "Sound")?.Rows.Select(r => new DemoSoundEntry
            {
                PartName = Str(r, "PartName") ?? "",
                Bgm = Str(r, "Bgm"),
                SystemSe = Str(r, "SystemSe"),
                ActionSe = Str(r, "ActionSe"),
                ReturnBgm = Bool(r, "ReturnBgm"),
                BgmWipeoutFrame = Int(r, "BgmWipeoutFrame"),
                AllSoundStopFrame = IntOrNull(r, "AllSoundStopFrame"),
            }).ToList() ?? [],
            SubPartEntries = LoadSheet(sheetDir, timeSheetName, "SubPart")?.Rows.Select(r => new DemoSubPartEntry
            {
                SubPartName = Str(r, "SubPartName") ?? "",
                SubPartTotalStep = Int(r, "SubPartTotalStep"),
                MainPartName = Str(r, "MainPartName") ?? "",
                MainPartStep = Int(r, "MainPartStep"),
            }).ToList() ?? [],
            WipeEntries = LoadSheet(sheetDir, timeSheetName, "Wipe")?.Rows.Select(r => new DemoWipeEntry
            {
                PartName = Str(r, "PartName") ?? "",
                WipeName = Str(r, "WipeName"),
                WipeType = Int(r, "WipeType"),
                WipeFrame = Int(r, "WipeFrame"),
            }).ToList() ?? [],
        };
    }

    private static BCSVTable? LoadSheet(RARCDirectory sheetDir, string timeSheetName, string category)
    {
        RARCFile? file = sheetDir.Files.Find(f => string.Equals(f.Name, $"Demo{timeSheetName}{category}.bcsv", StringComparison.OrdinalIgnoreCase));
        return file is null ? null : BCSVTable.Load(file.Data);
    }

    private static string? Str(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? v) && v is string s && s.Length > 0 ? s : null;

    private static int Int(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? v) && v is int i ? i : 0;

    private static int? IntOrNull(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? v) && v is int i ? i : null;

    private static bool Bool(IReadOnlyDictionary<string, object?> row, string key) =>
        Int(row, key) != 0;

    private static bool? BoolOrNull(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out object? v) && v is int i ? i != 0 : null;
}
