using System.Numerics;
using ImGuiNET;
using SMGEditor.Core.Formats;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal sealed class FlowGraphEditor
{
    private string? gameRootDir;
    private string? outputDir;
    private string? language;
    private string? zoneName;
    private string? entryLabel;
    private MSBFFile? flow;
    private MSBTFile? msbt;
    private List<int> nodeIndices = [];
    private string plainMessageText = "";

    private readonly HashSet<int> unreachableAddedNodes = [];

    private readonly Dictionary<int, Vector2> positions = [];
    private int? selectedNodeIndex;
    private string editableText = "";
    private bool isOpen;
    private int? draggingNode;
    private float zoom = 1f;

    private static readonly Vector4 ColorEntry = new(218 / 255f, 165 / 255f, 32 / 255f, 1f);
    private static readonly Vector4 ColorMessage = new(120 / 255f, 120 / 255f, 120 / 255f, 1f);
    private static readonly Vector4 ColorCondition = new(0f, 128 / 255f, 0f, 1f);
    private static readonly Vector4 ColorEvent = new(70 / 255f, 110 / 255f, 180 / 255f, 1f);
    private static readonly Vector4 ColorUnknown = new(150 / 255f, 90 / 255f, 30 / 255f, 1f);
    private static readonly Vector4 ColorEndOfFlow = new(139 / 255f, 0f, 0f, 1f);

    private const float ColumnGap = 90f;
    private const float RowGap = 24f;
    private const float MessageBoxHeight = 130f;
    private const float BranchBoxHeight = 210f;
    private const float EventBoxHeight = 120f;
    private const float SimpleBoxHeight = 56f;
    private const float BoxWidth = 220f;

    public bool IsOpen => isOpen;

    public void Open(string gameRootDir, string? outputDir, string language, string zoneName, string entryLabel)
    {
        this.gameRootDir = gameRootDir;
        this.outputDir = outputDir;
        this.language = language;
        this.zoneName = zoneName;
        this.entryLabel = entryLabel;

        (msbt, flow) = ZoneText.LoadZoneFiles(gameRootDir, outputDir, language, zoneName);
        selectedNodeIndex = null;
        editableText = "";
        positions.Clear();
        isOpen = true;

        SyncPlainMessageText();
        RebuildGraph(flow?.FindEntryPoint(entryLabel) is { } entry ? (int)entry.NodeIndex : -1);
    }

    private int startIndex = -1;

    private void RebuildGraph(int? newStartIndex = null)
    {
        if (newStartIndex.HasValue)
        {
            startIndex = newStartIndex.Value;
        }

        if (flow is null || startIndex < 0 || startIndex >= flow.Nodes.Count)
        {
            nodeIndices = [];
            return;
        }

        var visited = new HashSet<int>();
        var order = new Queue<int>();
        order.Enqueue(startIndex);

        while (order.Count > 0)
        {
            int index = order.Dequeue();
            if (!visited.Add(index) || index < 0 || index >= flow.Nodes.Count)
            {
                continue;
            }

            foreach (int target in OutgoingEdges(flow, index))
            {
                if (target >= 0 && target < flow.Nodes.Count)
                {
                    order.Enqueue(target);
                }
            }
        }

        unreachableAddedNodes.RemoveWhere(visited.Contains);
        nodeIndices = [.. visited.Union(unreachableAddedNodes).OrderBy(i => i)];
        LayoutNewNodes();
    }

    private static IEnumerable<int> OutgoingEdges(MSBFFile flow, int index) => flow.Nodes[index] switch
    {
        MSBFNode.Entry e => [e.NextNodeIndex],
        MSBFNode.Message m => [m.NextNodeIndex],
        MSBFNode.Event ev => [ev.NextNodeIndex],
        MSBFNode.Branch br when br.BranchTableIndex + 1 < flow.BranchTable.Count =>
            [flow.BranchTable[br.BranchTableIndex], flow.BranchTable[br.BranchTableIndex + 1]],
        _ => [],
    };

    private void LayoutNewNodes()
    {
        if (flow is null || nodeIndices.Count == 0)
        {
            return;
        }

        var depth = new Dictionary<int, int> { [startIndex] = 0 };
        var order = new Queue<int>();
        order.Enqueue(startIndex);
        while (order.Count > 0)
        {
            int index = order.Dequeue();
            foreach (int target in OutgoingEdges(flow, index))
            {
                if (target >= 0 && target < flow.Nodes.Count && !depth.ContainsKey(target))
                {
                    depth[target] = depth[index] + 1;
                    order.Enqueue(target);
                }
            }
        }

        var rowByColumn = new Dictionary<int, float>();
        foreach (int index in nodeIndices.Where(i => !positions.ContainsKey(i)).OrderBy(i => depth.GetValueOrDefault(i, 0)))
        {
            int column = depth.GetValueOrDefault(index, 0);
            float row = rowByColumn.GetValueOrDefault(column, 0);
            float height = BoxHeightFor(flow.Nodes[index]);
            rowByColumn[column] = row + height + RowGap;
            positions[index] = new Vector2(column * (BoxWidth + ColumnGap), row);
        }
    }

    private static float BoxHeightFor(MSBFNode node) => node switch
    {
        MSBFNode.Message => MessageBoxHeight,
        MSBFNode.Branch => BranchBoxHeight,
        MSBFNode.Event => EventBoxHeight,
        _ => SimpleBoxHeight,
    };

    public void Draw(float uiScale)
    {
        if (!isOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(1100, 700) * uiScale, ImGuiCond.FirstUseEver);
        if (!ImGui.Begin($"{LF("Flow Editor - {0}", zoneName)}###FlowEditor", ref isOpen))
        {
            ImGui.End();
            return;
        }

        if (flow is null || nodeIndices.Count == 0)
        {
            DrawPlainMessageMode(uiScale);
            ImGui.End();
            return;
        }

        if (ImGui.Button(L("Add Message Node")))
        {
            AddNode(new MSBFNode.Message(0, MSBFNode.NoNext));
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Add Condition Node")))
        {
            AddNode(new MSBFNode.Branch((ushort)MSBFConditions.YesNoResult, 0, (ushort)flow.BranchTable.Count));
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Add Event Node")))
        {
            AddNode(new MSBFNode.Event((ushort)MSBFEvents.EventFuncAndChain, MSBFNode.NoNext, 0));
        }

        float canvasWidth = ImGui.GetContentRegionAvail().X * 0.75f;
        ImGui.BeginChild("##FlowCanvas", new Vector2(canvasWidth, 0), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);
        DrawCanvas(uiScale);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##FlowDetails", Vector2.Zero, ImGuiChildFlags.Borders);
        DrawDetailsPanel(uiScale);
        ImGui.EndChild();

        ImGui.End();
    }

    private void DrawPlainMessageMode(float uiScale)
    {
        ImGui.TextWrapped(flow is null
            ? "This zone has no flow data (.msbf). Only a message."
            : "No entry point found for this object in this zone's own FEN1 table. Only a message.");

        ImGui.Spacing();
        ImGui.TextDisabled(entryLabel ?? "");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##plainmsgtext", ref plainMessageText, 4096, new Vector2(-1, 200f * uiScale)))
        {
            SavePlainMessageText();
        }

        ImGui.TextDisabled(L("Tags use bracket syntax, e.g. [color:red]...[defcolor], [delay:30], [icon:a_button]."));

        ImGui.Spacing();
        if (ImGui.Button(L("Create Flow for This Object")))
        {
            CreateFlow();
        }
    }

    private void SyncPlainMessageText()
    {
        plainMessageText = entryLabel is not null && msbt?.FindByLabel(entryLabel) is { } m
            ? MSBTText.ToEditableText(m.Parts)
            : "";
    }

    private void SavePlainMessageText()
    {
        if (gameRootDir is null || outputDir is null || language is null || zoneName is null || entryLabel is null)
        {
            return;
        }

        List<MSBTTextRun> parts;
        try
        {
            parts = MSBTText.ParseMessageText(plainMessageText);
        }
        catch (FormatException)
        {
            return;
        }

        ZoneText.SetObjectMessage(gameRootDir, outputDir, language, zoneName, entryLabel, parts);
        (msbt, _) = ZoneText.LoadZoneFiles(gameRootDir, outputDir, language, zoneName);
    }

    private void CreateFlow()
    {
        if (gameRootDir is null || outputDir is null || language is null || zoneName is null || entryLabel is null)
        {
            return;
        }

        ZoneText.CreateFlowEntryForObject(gameRootDir, outputDir, language, zoneName, entryLabel);
        (msbt, flow) = ZoneText.LoadZoneFiles(gameRootDir, outputDir, language, zoneName);
        RebuildGraph(flow?.FindEntryPoint(entryLabel) is { } entry ? (int)entry.NodeIndex : -1);
    }

    private void DrawDetailsPanel(float uiScale)
    {
        if (selectedNodeIndex is not int idx || flow is null || idx >= flow.Nodes.Count)
        {
            ImGui.TextWrapped(L("Click a node's title bar to select it."));
            return;
        }

        if (flow.Nodes[idx] is not MSBFNode.Message m)
        {
            ImGui.TextWrapped(L("Selected node has no text to edit."));
            return;
        }

        ImGui.TextDisabled(ResolveMessageLabel(m.MessageIndex) ?? "(no label)");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##msgtext", ref editableText, 4096, new Vector2(-1, 260f * uiScale)))
        {
            SaveEditableText();
        }

        ImGui.TextDisabled(L("Tags use bracket syntax, e.g. [color:red]...[defcolor], [delay:30], [icon:a_button]."));
    }

    private void DrawCanvas(float uiScale)
    {
        if (flow is null)
        {
            return;
        }

        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 viewportSize = ImGui.GetContentRegionAvail();
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        if (ImGui.IsWindowHovered())
        {
            float wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0f)
            {
                float oldScale = uiScale * zoom;
                Vector2 mouseScreen = ImGui.GetMousePos();
                Vector2 contentPoint = (mouseScreen - origin) / oldScale;

                zoom = Math.Clamp(zoom + wheel * 0.1f, 0.25f, 2.5f);
                float newScale = uiScale * zoom;

                Vector2 desiredOrigin = mouseScreen - contentPoint * newScale;
                Vector2 originDelta = origin - desiredOrigin;
                ImGui.SetScrollX(ImGui.GetScrollX() + originDelta.X);
                ImGui.SetScrollY(ImGui.GetScrollY() + originDelta.Y);
            }
        }

        ImGui.SetWindowFontScale(zoom);

        float scale = uiScale * zoom;
        Vector2 TopLeft(int index) => origin + positions[index] * scale;
        Vector2 BoxSize(int index) => new(BoxWidth * scale, BoxHeightFor(flow.Nodes[index]) * scale);

        foreach (int index in nodeIndices)
        {
            Vector2 from = TopLeft(index) + new Vector2(BoxSize(index).X, BoxSize(index).Y / 2);
            switch (flow.Nodes[index])
            {
                case MSBFNode.Entry e:
                    DrawEdge(drawList, from, e.NextNodeIndex, "next", 0xFFB0B0B0, scale);
                    break;
                case MSBFNode.Message m:
                    DrawEdge(drawList, from, m.NextNodeIndex, "next", 0xFFB0B0B0, scale);
                    break;
                case MSBFNode.Event ev:
                    DrawEdge(drawList, from, ev.NextNodeIndex, "next", 0xFFB0B0B0, scale);
                    break;
                case MSBFNode.Branch br when br.BranchTableIndex + 1 < flow.BranchTable.Count:
                    bool paramShown = MSBFParameterRules.ConditionUsesParameter((MSBFConditions)br.Condition);
                    int trueRow = paramShown ? 2 : 1;
                    int falseRow = paramShown ? 3 : 2;
                    Vector2 fromTrue = TopLeft(index) + new Vector2(BoxSize(index).X, BodyRowCenterOffsetY(trueRow, scale));
                    Vector2 fromFalse = TopLeft(index) + new Vector2(BoxSize(index).X, BodyRowCenterOffsetY(falseRow, scale));
                    DrawEdge(drawList, fromTrue, flow.BranchTable[br.BranchTableIndex], null, 0xFF4CAF50, scale);
                    DrawEdge(drawList, fromFalse, flow.BranchTable[br.BranchTableIndex + 1], null, 0xFF5A5AD8, scale);
                    break;
            }
        }

        foreach (int index in nodeIndices)
        {
            DrawNode(drawList, index, TopLeft(index), BoxSize(index), scale);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##canvasbg", new Vector2(Math.Max(viewportSize.X, 1f), Math.Max(viewportSize.Y, 1f)));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            Vector2 panDelta = ImGui.GetIO().MouseDelta;
            ImGui.SetScrollX(ImGui.GetScrollX() - panDelta.X);
            ImGui.SetScrollY(ImGui.GetScrollY() - panDelta.Y);
        }

        Vector2 maxCorner = positions.Count > 0 ? positions.Values.Aggregate(Vector2.Zero, Vector2.Max) : Vector2.Zero;
        ImGui.Dummy((maxCorner + new Vector2(BoxWidth, BranchBoxHeight) + new Vector2(60, 60)) * scale);

        ImGui.SetWindowFontScale(1f);

        void DrawEdge(ImDrawListPtr dl, Vector2 fromPoint, ushort targetIndex, string? label, uint color, float scale)
        {
            Vector2 to;
            if (targetIndex == MSBFNode.NoNext || targetIndex >= flow.Nodes.Count)
            {
                to = fromPoint + new Vector2(60f * scale, 0);
                dl.AddRectFilled(to - new Vector2(0, 10f * scale), to + new Vector2(70f * scale, 10f * scale), ImGui.GetColorU32(ColorEndOfFlow), 3f * scale);
                dl.AddText(to - new Vector2(-4f * scale, 8f * scale), 0xFFFFFFFF, "End");
            }
            else if (positions.TryGetValue(targetIndex, out Vector2 targetPos))
            {
                to = origin + targetPos * scale + new Vector2(0, BoxHeightFor(flow.Nodes[targetIndex]) * scale / 2);
            }
            else
            {
                return;
            }

            Vector2 c1 = fromPoint + new Vector2(40f * scale, 0);
            Vector2 c2 = to - new Vector2(40f * scale, 0);
            dl.AddBezierCubic(fromPoint, c1, c2, to, color, 2.2f * scale);
            dl.AddCircleFilled(to, 3f * scale, color);
            if (label is not null)
            {
                Vector2 mid = (fromPoint + to) / 2;
                dl.AddText(mid, color, label);
            }
        }
    }

    private static float BodyRowCenterOffsetY(int rowIndex, float scale)
    {
        float bodyTop = 22f * scale + 6f * scale;
        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        return bodyTop + rowHeight * (rowIndex + 0.5f);
    }

    private void DrawNode(ImDrawListPtr drawList, int index, Vector2 topLeft, Vector2 size, float uiScale)
    {
        if (flow is null)
        {
            return;
        }

        MSBFNode node = flow.Nodes[index];
        Vector2 bottomRight = topLeft + size;
        Vector4 titleColorVec = node switch
        {
            MSBFNode.Entry => ColorEntry,
            MSBFNode.Message => ColorMessage,
            MSBFNode.Branch => ColorCondition,
            MSBFNode.Event => ColorEvent,
            _ => ColorUnknown,
        };
        uint titleColor = ImGui.GetColorU32(titleColorVec);
        float titleHeight = 22f * uiScale;

        drawList.AddRectFilled(topLeft, bottomRight, ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.14f, 1f)), 5f * uiScale);
        drawList.AddRectFilled(topLeft, topLeft + new Vector2(size.X, titleHeight), titleColor, 5f * uiScale, ImDrawFlags.RoundCornersTop);
        bool selected = selectedNodeIndex == index;
        drawList.AddRect(topLeft, bottomRight, selected ? 0xFFFFFFFF : 0xFF202020, 5f * uiScale, ImDrawFlags.None, selected ? 2.5f * uiScale : 1f);

        drawList.PushClipRect(topLeft, topLeft + new Vector2(size.X, titleHeight), true);
        drawList.AddText(topLeft + new Vector2(6f, 3f) * uiScale, 0xFF000000, TitleFor(node));
        drawList.PopClipRect();

        ImGui.SetCursorScreenPos(topLeft);
        ImGui.InvisibleButton($"##dragnode{index}", new Vector2(size.X, titleHeight));
        if (ImGui.IsItemActivated())
        {
            selectedNodeIndex = index;
            SyncEditableTextFromSelection();
            draggingNode = index;
        }

        if (draggingNode == index)
        {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                positions[index] += ImGui.GetIO().MouseDelta / uiScale;
            }

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                draggingNode = null;
            }
        }

        Vector2 bodyPos = topLeft + new Vector2(8f * uiScale, titleHeight + 6f * uiScale);
        float bodyWidth = size.X - 16f * uiScale;
        DrawNodeBody(index, node, bodyPos, bodyWidth, uiScale);
    }

    private static string TitleFor(MSBFNode node) => node switch
    {
        MSBFNode.Entry => "Entry",
        MSBFNode.Message m => $"Message (msg {m.MessageIndex})",
        MSBFNode.Branch b => $"Condition: {(MSBFConditions)b.Condition}",
        MSBFNode.Event e => $"Event: {(MSBFEvents)e.EventType}",
        MSBFNode.Unknown u => $"Unknown (type {u.Type})",
        _ => "?",
    };

    private void DrawNodeBody(int index, MSBFNode node, Vector2 pos, float width, float uiScale)
    {
        if (flow is null)
        {
            return;
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.PushID(index);
        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(width);

        switch (node)
        {
            case MSBFNode.Entry:
                MSBFEntryPoint? entry = flow.EntryPoints.FirstOrDefault(e => e.NodeIndex == index);
                ImGui.TextWrapped(entry?.Name ?? "(no FEN1 label)");
                break;

            case MSBFNode.Message m:
                string? label = ResolveMessageLabel(m.MessageIndex);
                ImGui.TextDisabled(label ?? "(no label)");
                string preview = msbt is not null && m.MessageIndex < msbt.Messages.Count
                    ? MSBTText.ToEditableText(msbt.Messages[m.MessageIndex].Parts)
                    : "(message index out of range)";
                ImGui.TextWrapped(preview.Length > 90 ? preview[..90] + "…" : preview);
                if (ImGui.Button(L("Edit Text..."), new Vector2(width, 0)))
                {
                    selectedNodeIndex = index;
                    SyncEditableTextFromSelection();
                }

                break;

            case MSBFNode.Branch br:
                DrawConditionCombo(index, br);
                if (MSBFParameterRules.ConditionUsesParameter((MSBFConditions)br.Condition))
                {
                    int param = br.Parameter;
                    if (ImGui.InputInt("Param", ref param))
                    {
                        ReplaceNode(index, br with { Parameter = (ushort)Math.Clamp(param, 0, 0xFFFF) });
                    }
                }

                if (br.BranchTableIndex + 1 < flow.BranchTable.Count)
                {
                    DrawBranchTargetCombo("True", br.BranchTableIndex, width);
                    DrawBranchTargetCombo("False", br.BranchTableIndex + 1, width);
                }

                break;

            case MSBFNode.Event ev:
                DrawEventCombo(index, ev);
                if (MSBFParameterRules.EventUsesParameter((MSBFEvents)ev.EventType))
                {
                    int param = ev.Parameter;
                    if (ImGui.InputInt("Param", ref param))
                    {
                        ReplaceNode(index, ev with { Parameter = (ushort)Math.Clamp(param, 0, 0xFFFF) });
                    }
                }

                break;

            case MSBFNode.Unknown u:
                ImGui.TextWrapped("Args: " + string.Join(", ", u.Arguments));
                break;
        }

        ImGui.EndGroup();
        ImGui.PopID();
    }

    private void DrawConditionCombo(int index, MSBFNode.Branch br)
    {
        var current = (MSBFConditions)br.Condition;
        if (ImGui.BeginCombo(L("Condition"), current.ToString()))
        {
            foreach (MSBFConditions value in Enum.GetValues<MSBFConditions>())
            {
                if (ImGui.Selectable(value.ToString(), value == current))
                {
                    ReplaceNode(index, br with { Condition = (ushort)value });
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawBranchTargetCombo(string id, int branchTableSlot, float width)
    {
        if (flow is null)
        {
            return;
        }

        ushort current = flow.BranchTable[branchTableSlot];
        bool isEnd = current == MSBFNode.NoNext || current >= flow.Nodes.Count;
        ImGui.SetNextItemWidth(width);

        if (ImGui.BeginCombo($"##{id}", isEnd ? "(End of Flow)" : NodeOptionLabel(current)))
        {
            if (ImGui.Selectable(L("(End of Flow)"), isEnd))
            {
                SetBranchTarget(branchTableSlot, MSBFNode.NoNext);
            }

            for (int i = 0; i < flow.Nodes.Count; i++)
            {
                if (ImGui.Selectable(NodeOptionLabel(i), i == current))
                {
                    SetBranchTarget(branchTableSlot, (ushort)i);
                }
            }

            ImGui.EndCombo();
        }
    }

    private string NodeOptionLabel(int index) => flow is null ? index.ToString() : $"[{index}] {TitleFor(flow.Nodes[index])}";

    private void SetBranchTarget(int branchTableSlot, ushort target)
    {
        if (flow is null || gameRootDir is null || outputDir is null || language is null || zoneName is null)
        {
            return;
        }

        var updatedBranchTable = flow.BranchTable.ToList();
        updatedBranchTable[branchTableSlot] = target;
        flow = new MSBFFile { Encoding = flow.Encoding, Nodes = flow.Nodes, BranchTable = updatedBranchTable, EntryPoints = flow.EntryPoints };
        ZoneText.SetZoneFlow(gameRootDir, outputDir, language, zoneName, flow);
        RebuildGraph();
    }

    private void DrawEventCombo(int index, MSBFNode.Event ev)
    {
        var current = (MSBFEvents)ev.EventType;
        if (ImGui.BeginCombo(L("Event"), current.ToString()))
        {
            foreach (MSBFEvents value in Enum.GetValues<MSBFEvents>())
            {
                if (ImGui.Selectable(value.ToString(), value == current))
                {
                    ReplaceNode(index, ev with { EventType = (ushort)value });
                }
            }

            ImGui.EndCombo();
        }
    }

    private string? ResolveMessageLabel(ushort messageIndex) =>
        msbt?.Labels.FirstOrDefault(l => l.MessageIndex == messageIndex).Name;

    private void SyncEditableTextFromSelection()
    {
        if (selectedNodeIndex is not int idx || flow is null || flow.Nodes[idx] is not MSBFNode.Message m)
        {
            editableText = "";
            return;
        }

        editableText = msbt is not null && m.MessageIndex < msbt.Messages.Count
            ? MSBTText.ToEditableText(msbt.Messages[m.MessageIndex].Parts)
            : "";
    }

    private void AddNode(MSBFNode node)
    {
        if (flow is null || gameRootDir is null || outputDir is null || language is null || zoneName is null)
        {
            return;
        }

        var updatedNodes = flow.Nodes.ToList();
        updatedNodes.Add(node);
        var updatedBranchTable = flow.BranchTable.ToList();
        if (node is MSBFNode.Branch)
        {
            updatedBranchTable.Add(MSBFNode.NoNext);
            updatedBranchTable.Add(MSBFNode.NoNext);
        }

        flow = new MSBFFile { Encoding = flow.Encoding, Nodes = updatedNodes, BranchTable = updatedBranchTable, EntryPoints = flow.EntryPoints };
        ZoneText.SetZoneFlow(gameRootDir, outputDir, language, zoneName, flow);

        int newIndex = updatedNodes.Count - 1;
        unreachableAddedNodes.Add(newIndex);
        nodeIndices.Add(newIndex);
        positions[newIndex] = new Vector2(0, (positions.Count > 0 ? positions.Values.Max(p => p.Y) : 0) + BranchBoxHeight + RowGap);
    }

    private void ReplaceNode(int index, MSBFNode updated)
    {
        if (flow is null || gameRootDir is null || outputDir is null || language is null || zoneName is null)
        {
            return;
        }

        var updatedNodes = flow.Nodes.ToList();
        updatedNodes[index] = updated;
        flow = new MSBFFile { Encoding = flow.Encoding, Nodes = updatedNodes, BranchTable = flow.BranchTable, EntryPoints = flow.EntryPoints };
        ZoneText.SetZoneFlow(gameRootDir, outputDir, language, zoneName, flow);
        RebuildGraph();
    }

    private void SaveEditableText()
    {
        if (gameRootDir is null || outputDir is null || language is null || zoneName is null || selectedNodeIndex is not int idx || flow is null)
        {
            return;
        }

        if (flow.Nodes[idx] is not MSBFNode.Message m)
        {
            return;
        }

        string? label = ResolveMessageLabel(m.MessageIndex);
        if (label is null)
        {
            return;
        }

        List<MSBTTextRun> parts;
        try
        {
            parts = MSBTText.ParseMessageText(editableText);
        }
        catch (FormatException)
        {
            return;
        }

        ZoneText.SetObjectMessage(gameRootDir, outputDir, language, zoneName, label, parts);
        (msbt, _) = ZoneText.LoadZoneFiles(gameRootDir, outputDir, language, zoneName);
    }
}

internal static class MSBFParameterRules
{
    public static bool ConditionUsesParameter(MSBFConditions condition) => condition is MSBFConditions.YesNoResult or MSBFConditions.BranchFunc;

    public static bool EventUsesParameter(MSBFEvents evt) => evt is MSBFEvents.EventFuncAndChain or MSBFEvents.EventFuncAndEnd or MSBFEvents.AnimeFunc or MSBFEvents.KillFunc;
}
