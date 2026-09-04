using System.Text;
using SMGEditor.Core.Formats;

namespace SMGEditor.Viewer;

public static class ZoneText
{
    private static string ZoneArcRelativePath(string language, string zoneOrGalaxyName) =>
        Path.Combine("DATA", "files", "LocalizeData", language, "MessageData", zoneOrGalaxyName + ".arc");

    private static readonly Dictionary<(string Root, string? OutputDir, string Language, string Zone), (MSBTFile? MSBT, MSBFFile? MSBF)> Cache = new();

    private static (MSBTFile? MSBT, MSBFFile? MSBF) LoadZone(string gameRootDir, string? outputDir, string language, string zoneOrGalaxyName)
    {
        var key = (gameRootDir, outputDir, language, zoneOrGalaxyName);
        if (Cache.TryGetValue(key, out (MSBTFile?, MSBFFile?) cached))
        {
            return cached;
        }

        MSBTFile? msbt = null;
        MSBFFile? msbf = null;
        string relativePath = ZoneArcRelativePath(language, zoneOrGalaxyName);
        string arcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (File.Exists(arcPath))
        {
            RARCArchive arc = RARCArchive.Load(arcPath);

            RARCFile? msbtFile = arc.Root.FindFileByName(zoneOrGalaxyName + ".msbt");
            if (msbtFile is not null)
            {
                msbt = MSBTReader.Load(msbtFile.Data);
            }

            RARCFile? msbfFile = arc.Root.FindFileByName(zoneOrGalaxyName + ".msbf");
            if (msbfFile is not null)
            {
                msbf = MSBFReader.Load(msbfFile.Data);
            }
        }

        var result = (msbt, msbf);
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

    public static (MSBTFile? MSBT, MSBFFile? MSBF) LoadZoneFiles(string gameRootDir, string? outputDir, string language, string zoneOrGalaxyName) =>
        LoadZone(gameRootDir, outputDir, language, zoneOrGalaxyName);

    public static IReadOnlyList<(string Label, string Text)> ResolveObjectMessages(
        string gameRootDir, string? outputDir, string language, string zoneOrGalaxyName, string objectInternalName, int messageId)
    {
        (MSBTFile? msbt, _) = LoadZone(gameRootDir, outputDir, language, zoneOrGalaxyName);
        if (msbt is null)
        {
            return [];
        }

        string baseLabel = $"{objectInternalName}{messageId:D3}";
        MSBTMessage? plain = msbt.FindByLabel(baseLabel);
        if (plain is not null)
        {
            return [(baseLabel, PlainText(plain))];
        }

        var results = new List<(string, string)>();
        for (int flowId = 0; ; flowId++)
        {
            string label = $"{baseLabel}_Flow{flowId:D3}";
            MSBTMessage? message = msbt.FindByLabel(label);
            if (message is null)
            {
                break;
            }

            results.Add((label, PlainText(message)));
        }

        return results;
    }

    public static void SetObjectMessage(string gameRootDir, string outputDir, string language, string zoneOrGalaxyName, string label, string newText) =>
        SetObjectMessage(gameRootDir, outputDir, language, zoneOrGalaxyName, label, (IReadOnlyList<MSBTTextRun>)[new MSBTTextRun.Literal(newText)]);

    public static void SetObjectMessage(string gameRootDir, string outputDir, string language, string zoneOrGalaxyName, string label, IReadOnlyList<MSBTTextRun> newParts)
    {
        string relativePath = ZoneArcRelativePath(language, zoneOrGalaxyName);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        string msbtFileName = zoneOrGalaxyName + ".msbt";
        RARCFile? msbtFile = archive.Root.FindFileByName(msbtFileName);
        if (msbtFile is null)
        {
            return;
        }

        MSBTFile updated = MSBTReader.Load(msbtFile.Data).WithUpsertedLabel(label, newParts);
        archive.Root.ReplaceFileDataByName(msbtFileName, updated.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);

        var key = (gameRootDir, (string?)outputDir, language, zoneOrGalaxyName);
        (MSBTFile?, MSBFFile?) previous = Cache.TryGetValue(key, out (MSBTFile?, MSBFFile?) existing) ? existing : (null, null);
        Cache[key] = (updated, previous.Item2);
    }

    public static void CreateFlowEntryForObject(string gameRootDir, string outputDir, string language, string zoneOrGalaxyName, string entryLabel)
    {
        string relativePath = ZoneArcRelativePath(language, zoneOrGalaxyName);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        string msbtFileName = zoneOrGalaxyName + ".msbt";
        RARCFile? msbtFile = archive.Root.FindFileByName(msbtFileName);
        if (msbtFile is null)
        {
            return;
        }

        MSBTFile msbt = MSBTReader.Load(msbtFile.Data);
        string existingText = msbt.FindByLabel(entryLabel) is { } existing ? PlainText(existing) : "";
        msbt = msbt.WithUpsertedLabel(entryLabel, existingText);
        archive.Root.ReplaceFileDataByName(msbtFileName, msbt.Save());

        int messageIndex = -1;
        foreach (MSBTLabel label in msbt.Labels)
        {
            if (label.Name == entryLabel)
            {
                messageIndex = (int)label.MessageIndex;
                break;
            }
        }

        string msbfFileName = zoneOrGalaxyName + ".msbf";
        RARCFile? msbfFile = archive.Root.FindFileByName(msbfFileName);
        MSBFFile msbf = msbfFile is not null ? MSBFReader.Load(msbfFile.Data) : new MSBFFile { Encoding = 1, Nodes = [], BranchTable = [], EntryPoints = [] };

        if (msbf.FindEntryPoint(entryLabel) is null)
        {
            var nodes = msbf.Nodes.ToList();
            nodes.Add(new MSBFNode.Message((ushort)messageIndex, MSBFNode.NoNext));
            int messageNodeIndex = nodes.Count - 1;
            nodes.Add(new MSBFNode.Entry((ushort)messageNodeIndex));
            int entryNodeIndex = nodes.Count - 1;

            var entryPoints = msbf.EntryPoints.ToList();
            entryPoints.Add(new MSBFEntryPoint { Name = entryLabel, NodeIndex = (uint)entryNodeIndex });

            msbf = new MSBFFile { Encoding = msbf.Encoding, Nodes = nodes, BranchTable = msbf.BranchTable, EntryPoints = entryPoints };
        }

        byte[] msbfData = msbf.Save();
        if (msbfFile is not null)
        {
            archive.Root.ReplaceFileDataByName(msbfFileName, msbfData);
        }
        else
        {
            RARCDirectory containingDir = archive.Root.FindContainingDirectory(msbtFileName) ?? archive.Root;
            containingDir.Files.Add(new RARCFile { Name = msbfFileName, Data = msbfData });
        }

        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);

        var key = (gameRootDir, (string?)outputDir, language, zoneOrGalaxyName);
        Cache[key] = (msbt, msbf);
    }

    public static void SetZoneFlow(string gameRootDir, string outputDir, string language, string zoneOrGalaxyName, MSBFFile updatedFlow)
    {
        string relativePath = ZoneArcRelativePath(language, zoneOrGalaxyName);
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        string msbfFileName = zoneOrGalaxyName + ".msbf";
        if (!archive.Root.ReplaceFileDataByName(msbfFileName, updatedFlow.Save()))
        {
            return;
        }

        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);

        var key = (gameRootDir, (string?)outputDir, language, zoneOrGalaxyName);
        (MSBTFile?, MSBFFile?) previous = Cache.TryGetValue(key, out (MSBTFile?, MSBFFile?) existing) ? existing : (null, null);
        Cache[key] = (previous.Item1, updatedFlow);
    }
}

public static class SMG1Text
{
    public const string Locale = "UsEnglish";

    private static string MessageArcRelativePath => Path.Combine("DATA", "files", Locale, "MessageData", "Message.arc");

    private static readonly Dictionary<(string Root, string? OutputDir), (BMGFile? BMG, Dictionary<string, int>? LabelToIndex)> Cache = new();

    private static (BMGFile? BMG, Dictionary<string, int>? LabelToIndex) Load(string gameRootDir, string? outputDir)
    {
        var key = (gameRootDir, outputDir);
        if (Cache.TryGetValue(key, out (BMGFile?, Dictionary<string, int>?) cached))
        {
            return cached;
        }

        BMGFile? bmg = null;
        Dictionary<string, int>? labelToIndex = null;
        string relativePath = MessageArcRelativePath;
        string arcPath = ProjectFiles.ResolveFile(gameRootDir, outputDir, relativePath);
        if (File.Exists(arcPath))
        {
            RARCArchive arc = RARCArchive.Load(arcPath);

            RARCFile? bmgFile = arc.Root.FindFileByName("message.bmg");
            if (bmgFile is not null)
            {
                bmg = BMGReader.Load(bmgFile.Data);
            }

            RARCFile? tblFile = arc.Root.FindFileByName("messageid.tbl");
            if (tblFile is not null)
            {
                BCSVTable tbl = BCSVTable.Load(tblFile.Data);
                labelToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (IReadOnlyDictionary<string, object?> row in tbl.Rows)
                {
                    if (row.TryGetValue("MessageId", out object? id) && id is string label
                        && row.TryGetValue("Index", out object? idx) && idx is int index)
                    {
                        labelToIndex[label] = index;
                    }
                }
            }
        }

        (BMGFile?, Dictionary<string, int>?) result = (bmg, labelToIndex);
        Cache[key] = result;
        return result;
    }

    private static string PlainText(BMGMessage message)
    {
        var sb = new StringBuilder();
        foreach (BMGTextRun part in message.Parts)
        {
            if (part is BMGTextRun.Literal literal)
            {
                sb.Append(literal.Value);
            }
        }

        return sb.ToString();
    }

    public static IReadOnlyList<(string Label, string Text)> ResolveObjectMessages(string gameRootDir, string? outputDir, string zoneOrGalaxyName, string objectInternalName, int messageId)
    {
        (BMGFile? bmg, Dictionary<string, int>? labelToIndex) = Load(gameRootDir, outputDir);
        if (bmg is null || labelToIndex is null)
        {
            return [];
        }

        string baseLabel = $"{zoneOrGalaxyName}_{objectInternalName}{messageId:D3}";
        if (labelToIndex.TryGetValue(baseLabel, out int index) && index < bmg.Messages.Count)
        {
            return [(baseLabel, PlainText(bmg.Messages[index]))];
        }

        var results = new List<(string, string)>();
        for (int flowId = 0; ; flowId++)
        {
            string label = $"{baseLabel}_Flow{flowId:D3}";
            if (!labelToIndex.TryGetValue(label, out int flowIndex) || flowIndex >= bmg.Messages.Count)
            {
                break;
            }

            results.Add((label, PlainText(bmg.Messages[flowIndex])));
        }

        return results;
    }

    private static string? ResolveLabel(string gameRootDir, string? outputDir, string label)
    {
        (BMGFile? bmg, Dictionary<string, int>? labelToIndex) = Load(gameRootDir, outputDir);
        return bmg is not null && labelToIndex is not null && labelToIndex.TryGetValue(label, out int index) && index < bmg.Messages.Count
            ? PlainText(bmg.Messages[index])
            : null;
    }

    public static (BMGFile? BMG, Dictionary<string, int>? LabelToIndex) LoadBMG(string gameRootDir, string? outputDir) => Load(gameRootDir, outputDir);

    public static string? ResolveLabelForMessageIndex(string gameRootDir, string? outputDir, int messageIndex)
    {
        (_, Dictionary<string, int>? labelToIndex) = Load(gameRootDir, outputDir);
        foreach ((string label, int index) in labelToIndex ?? [])
        {
            if (index == messageIndex)
            {
                return label;
            }
        }

        return null;
    }

    public static int? FindFlowEntryNodeIndex(string gameRootDir, string? outputDir, string baseLabel)
    {
        (BMGFile? bmg, Dictionary<string, int>? labelToIndex) = Load(gameRootDir, outputDir);
        if (bmg is null || labelToIndex is null || !labelToIndex.TryGetValue(baseLabel, out int messageIndex))
        {
            return null;
        }

        for (int i = 0; i < bmg.FlowNodes.Count; i++)
        {
            if (bmg.FlowNodes[i] is BMGFlowNode.Continuation c && c.MessageIndex == messageIndex)
            {
                return i;
            }
        }

        return null;
    }

    private static void UpdateBMG(string gameRootDir, string outputDir, Func<BMGFile, BMGFile> update)
    {
        string relativePath = MessageArcRelativePath;
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        RARCFile? bmgFile = archive.Root.FindFileByName("message.bmg");
        if (bmgFile is null)
        {
            return;
        }

        BMGFile updated = update(BMGReader.Load(bmgFile.Data));
        archive.Root.ReplaceFileDataByName("message.bmg", updated.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);

        var key = (gameRootDir, (string?)outputDir);
        (BMGFile?, Dictionary<string, int>?) previous = Cache.TryGetValue(key, out (BMGFile?, Dictionary<string, int>?) existing) ? existing : (null, null);
        Cache[key] = (updated, previous.Item2);
    }

    public static void SetFlowNode(string gameRootDir, string outputDir, int nodeIndex, BMGFlowNode updatedNode) =>
        UpdateBMG(gameRootDir, outputDir, bmg =>
        {
            var nodes = bmg.FlowNodes.ToList();
            nodes[nodeIndex] = updatedNode;
            return WithFlow(bmg, nodes, bmg.FlowIndirectionTable);
        });

    public static void SetFlowIndirection(string gameRootDir, string outputDir, int slot, ushort target) =>
        UpdateBMG(gameRootDir, outputDir, bmg =>
        {
            var indirection = bmg.FlowIndirectionTable.ToList();
            indirection[slot] = target;
            return WithFlow(bmg, bmg.FlowNodes, indirection);
        });

    public static void AddFlowNode(string gameRootDir, string outputDir, BMGFlowNode node, int extraIndirectionSlots) =>
        UpdateBMG(gameRootDir, outputDir, bmg =>
        {
            var nodes = bmg.FlowNodes.ToList();
            nodes.Add(node);
            var indirection = bmg.FlowIndirectionTable.ToList();
            for (int i = 0; i < extraIndirectionSlots; i++)
            {
                indirection.Add(0xFFFF);
            }

            return WithFlow(bmg, nodes, indirection);
        });

    public static void SetMessageTextByIndex(string gameRootDir, string outputDir, int messageIndex, string newText) =>
        UpdateBMG(gameRootDir, outputDir, bmg =>
        {
            var messages = bmg.Messages.ToList();
            messages[messageIndex] = new BMGMessage { Parts = [new BMGTextRun.Literal(newText)], Attributes = messages[messageIndex].Attributes };
            return new BMGFile
            {
                Encoding = bmg.Encoding,
                Messages = messages,
                FlowNodes = bmg.FlowNodes,
                FlowIndirectionTable = bmg.FlowIndirectionTable,
                FlowIds = bmg.FlowIds,
                Inf1HeaderExtra = bmg.Inf1HeaderExtra,
                Fli1HeaderUnknown = bmg.Fli1HeaderUnknown,
            };
        });

    private static BMGFile WithFlow(BMGFile bmg, IReadOnlyList<BMGFlowNode> nodes, IReadOnlyList<ushort> indirection) => new()
    {
        Encoding = bmg.Encoding,
        Messages = bmg.Messages,
        FlowNodes = nodes,
        FlowIndirectionTable = indirection,
        FlowIds = bmg.FlowIds,
        Inf1HeaderExtra = bmg.Inf1HeaderExtra,
        Fli1HeaderUnknown = bmg.Fli1HeaderUnknown,
    };

    public static string ResolveGalaxyName(string gameRootDir, string? outputDir, string internalName) =>
        ResolveLabel(gameRootDir, outputDir, $"GalaxyName_{internalName}") ?? internalName;

    public static string? ResolveScenarioName(string gameRootDir, string? outputDir, string internalGalaxyName, int starNumber) =>
        ResolveLabel(gameRootDir, outputDir, $"ScenarioName_{internalGalaxyName}{starNumber}");

    public static void SetGalaxyName(string gameRootDir, string outputDir, string internalName, string newDisplayName) =>
        SetLabel(gameRootDir, outputDir, $"GalaxyName_{internalName}", newDisplayName);

    public static void SetScenarioName(string gameRootDir, string outputDir, string internalGalaxyName, int starNumber, string newDisplayName) =>
        SetLabel(gameRootDir, outputDir, $"ScenarioName_{internalGalaxyName}{starNumber}", newDisplayName);

    public static void SetObjectMessage(string gameRootDir, string outputDir, string label, string newText) =>
        SetLabel(gameRootDir, outputDir, label, newText);

    private static void SetLabel(string gameRootDir, string outputDir, string label, string newText)
    {
        string relativePath = MessageArcRelativePath;
        (RARCArchive archive, bool wasCompressed) = ProjectFiles.LoadArc(gameRootDir, outputDir, relativePath);

        RARCFile? bmgFile = archive.Root.FindFileByName("message.bmg");
        RARCFile? tblFile = archive.Root.FindFileByName("messageid.tbl");
        if (bmgFile is null || tblFile is null)
        {
            return;
        }

        BMGFile bmg = BMGReader.Load(bmgFile.Data);
        BCSVTable tbl = BCSVTable.Load(tblFile.Data);

        int existingIndex = -1;
        foreach (IReadOnlyDictionary<string, object?> row in tbl.Rows)
        {
            if (row.TryGetValue("MessageId", out object? idVal) && idVal is string rowLabel && rowLabel == label
                && row.TryGetValue("Index", out object? idxVal) && idxVal is int idx)
            {
                existingIndex = idx;
                break;
            }
        }

        var messages = bmg.Messages.ToList();
        IReadOnlyList<BMGTextRun> newParts = [new BMGTextRun.Literal(newText)];
        var updatedRows = tbl.Rows.ToList();

        if (existingIndex >= 0 && existingIndex < messages.Count)
        {
            messages[existingIndex] = new BMGMessage { Parts = newParts, Attributes = messages[existingIndex].Attributes };
        }
        else
        {
            IReadOnlyList<byte> attributes = messages.Count > 0 ? messages[0].Attributes : new byte[8];
            messages.Add(new BMGMessage { Parts = newParts, Attributes = attributes });
            updatedRows.Add(new Dictionary<string, object?> { ["MessageId"] = label, ["Index"] = messages.Count - 1 });
        }

        var updatedBMG = new BMGFile
        {
            Encoding = bmg.Encoding,
            Messages = messages,
            FlowNodes = bmg.FlowNodes,
            FlowIndirectionTable = bmg.FlowIndirectionTable,
            FlowIds = bmg.FlowIds,
            Inf1HeaderExtra = bmg.Inf1HeaderExtra,
            Fli1HeaderUnknown = bmg.Fli1HeaderUnknown,
        };
        var updatedTbl = new BCSVTable { Fields = tbl.Fields, Rows = updatedRows, EntrySize = tbl.EntrySize, DataOffset = tbl.DataOffset };

        archive.Root.ReplaceFileDataByName("message.bmg", updatedBMG.Save());
        archive.Root.ReplaceFileDataByName("messageid.tbl", updatedTbl.Save());
        ProjectFiles.SaveArc(outputDir, relativePath, archive, wasCompressed);

        var labelToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, object?> row in updatedRows)
        {
            if (row.TryGetValue("MessageId", out object? id) && id is string l && row.TryGetValue("Index", out object? i) && i is int index)
            {
                labelToIndex[l] = index;
            }
        }

        Cache[(gameRootDir, outputDir)] = (updatedBMG, labelToIndex);
    }
}
