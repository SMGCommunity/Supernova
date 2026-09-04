using System.Text;
using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public static class SMG2Languages
{
    public static readonly string[] Codes =
    [
        "EuEnglish", "EuFrench", "EuGerman", "EuItalian", "EuSpanish", "UsEnglish", "UsSpanish",
    ];

    public const string Default = "UsEnglish";
}

public static class GalaxyText
{
    private static string SystemMessageRelativePath(string language) =>
        Path.Combine("DATA", "files", "LocalizeData", language, "MessageData", "SystemMessage.arc");

    private static readonly Dictionary<(string Root, string? OutputDir, string Language, string File), MSBTFile?> Cache = new();

    private static MSBTFile? LoadSystemMessage(string gameRootDir, string? outputDir, string language, string fileName)
    {
        var key = (gameRootDir, outputDir, language, fileName);
        if (Cache.TryGetValue(key, out MSBTFile? cached))
        {
            return cached;
        }

        MSBTFile? result = null;
        string relativePath = SystemMessageRelativePath(language);
        string arcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (File.Exists(arcPath))
        {
            RARCArchive arc = RARCArchive.Load(arcPath);
            RARCFile? file = arc.Root.FindFileByName(fileName);
            if (file is not null)
            {
                result = MSBTReader.Load(file.Data);
            }
        }

        Cache[key] = result;
        return result;
    }

    private static string PlainText(MSBTMessage message)
    {
        var sb = new StringBuilder();
        foreach (MSBTTextRun part in message.Parts)
        {
            if (part is MSBTTextRun.Literal literal)
            {
                sb.Append(literal.Value);
            }
        }

        return sb.ToString();
    }

    public static string ResolveGalaxyName(string gameRootDir, string? outputDir, string language, string internalName)
    {
        MSBTMessage? message = LoadSystemMessage(gameRootDir, outputDir, language, "GalaxyName.msbt")?.FindByLabel($"GalaxyName_{internalName}");
        return message is not null ? PlainText(message) : internalName;
    }

    public static string ResolveGalaxyNameShort(string gameRootDir, string? outputDir, string language, string internalName)
    {
        MSBTMessage? message = LoadSystemMessage(gameRootDir, outputDir, language, "GalaxyNameShort.msbt")?.FindByLabel($"GalaxyNameShort_{internalName}");
        return message is not null ? PlainText(message) : internalName;
    }

    public static string? ResolveScenarioName(string gameRootDir, string? outputDir, string language, string internalGalaxyName, int starNumber)
    {
        MSBTMessage? message = LoadSystemMessage(gameRootDir, outputDir, language, "ScenarioName.msbt")?.FindByLabel($"ScenarioName_{internalGalaxyName}{starNumber}");
        return message is not null ? PlainText(message) : null;
    }

    public static void SetGalaxyName(string gameRootDir, string outputDir, string language, string internalName, string newDisplayName) =>
        SetLabel(gameRootDir, outputDir, language, "GalaxyName.msbt", $"GalaxyName_{internalName}", newDisplayName);

    public static void SetScenarioName(string gameRootDir, string outputDir, string language, string internalGalaxyName, int starNumber, string newDisplayName) =>
        SetLabel(gameRootDir, outputDir, language, "ScenarioName.msbt", $"ScenarioName_{internalGalaxyName}{starNumber}", newDisplayName);

    private static void SetLabel(string gameRootDir, string outputDir, string language, string fileName, string label, string newText)
    {
        string relativePath = SystemMessageRelativePath(language);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        RARCFile? msbtFile = archive.Root.FindFileByName(fileName);
        if (msbtFile is null)
        {
            return;
        }

        MSBTFile updated = MSBTReader.Load(msbtFile.Data).WithUpsertedLabel(label, newText);
        archive.Root.ReplaceFileDataByName(fileName, updated.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);

        Cache[(gameRootDir, outputDir, language, fileName)] = updated;
    }
}
