using System.Numerics;
using ImGuiNET;
using SMGEditor.Core.Formats;
using SMGEditor.Viewer;

namespace SMGEditor.Editor;

internal sealed class SMG1FlowGraphEditor
{
    private string? gameRootDir;
    private string? outputDir;
    private BMGFile? bmg;
    private Dictionary<string, int>? labelToIndex;
    private List<int> nodeIndices = [];

    private readonly HashSet<int> unreachableAddedNodes = [];

    private readonly Dictionary<int, Vector2> positions = [];
    private int? selectedNodeIndex;
    private string editableText = "";
    private bool isOpen;
    private int? draggingNode;
    private float zoom = 1f;

    private static readonly Vector4 ColorContinuation = new(120 / 255f, 120 / 255f, 120 / 255f, 1f);
    private static readonly Vector4 ColorBranch = new(0f, 128 / 255f, 0f, 1f);
    private static readonly Vector4 ColorEvent = new(70 / 255f, 110 / 255f, 180 / 255f, 1f);
    private static readonly Vector4 ColorEndOfFlow = new(139 / 255f, 0f, 0f, 1f);

    private const float ColumnGap = 90f;
    private const float RowGap = 24f;
    private const float MessageBoxHeight = 130f;
    private const float BranchBoxHeight = 210f;
    private const float EventBoxHeight = 150f;
    private const float BoxWidth = 220f;
    private const ushort NoNext = 0xFFFF;

    public bool IsOpen => isOpen;

    public void Open(string gameRootDir, string? outputDir, string baseLabel)
    {
        this.gameRootDir = gameRootDir;
        this.outputDir = outputDir;

        (bmg, labelToIndex) = SMG1Text.LoadBMG(gameRootDir, outputDir);
        selectedNodeIndex = null;
        editableText = "";
        positions.Clear();
        isOpen = true;

        int? entry = SMG1Text.FindFlowEntryNodeIndex(gameRootDir, outputDir, baseLabel);
        startIndex = entry ?? (labelToIndex is not null && labelToIndex.TryGetValue(baseLabel, out int msgIndex) ? FindContinuationForMessage(msgIndex) : null) ?? -1;
        RebuildGraph();
    }

    private int? FindContinuationForMessage(int messageIndex)
    {
        if (bmg is null)
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

    private int startIndex = -1;

    private void RebuildGraph()
    {
        if (bmg is null || startIndex < 0 || startIndex >= bmg.FlowNodes.Count)
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
            if (!visited.Add(index) || index < 0 || index >= bmg.FlowNodes.Count)
            {
                continue;
            }

            foreach (int target in OutgoingEdges(bmg, index))
            {
                if (target >= 0 && target < bmg.FlowNodes.Count)
                {
                    order.Enqueue(target);
                }
            }
        }

        unreachableAddedNodes.RemoveWhere(visited.Contains);
        nodeIndices = [.. visited.Union(unreachableAddedNodes).OrderBy(i => i)];
        LayoutNewNodes();
    }

    private static IEnumerable<int> OutgoingEdges(BMGFile bmg, int index) => bmg.FlowNodes[index] switch
    {
        BMGFlowNode.Continuation c when c.NextNodeIndex != NoNext => [c.NextNodeIndex],
        BMGFlowNode.Branch b => IndirectionTargets(bmg, b.IndirectionTableOffset, 2),
        BMGFlowNode.Event e => IndirectionTargets(bmg, e.IndirectionTableIndex, 1),
        _ => [],
    };

    private static IEnumerable<int> IndirectionTargets(BMGFile bmg, int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int slot = start + i;
            if (slot >= 0 && slot < bmg.FlowIndirectionTable.Count && bmg.FlowIndirectionTable[slot] != NoNext)
            {
                yield return bmg.FlowIndirectionTable[slot];
            }
        }
    }

    private void LayoutNewNodes()
    {
        if (bmg is null || nodeIndices.Count == 0)
        {
            return;
        }

        var depth = new Dictionary<int, int> { [startIndex] = 0 };
        var order = new Queue<int>();
        order.Enqueue(startIndex);
        while (order.Count > 0)
        {
            int index = order.Dequeue();
            foreach (int target in OutgoingEdges(bmg, index))
            {
                if (target >= 0 && target < bmg.FlowNodes.Count && !depth.ContainsKey(target))
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
            float height = BoxHeightFor(bmg.FlowNodes[index]);
            rowByColumn[column] = row + height + RowGap;
            positions[index] = new Vector2(column * (BoxWidth + ColumnGap), row);
        }
    }

    private static float BoxHeightFor(BMGFlowNode node) => node switch
    {
        BMGFlowNode.Continuation => MessageBoxHeight,
        BMGFlowNode.Branch => BranchBoxHeight,
        BMGFlowNode.Event => EventBoxHeight,
        _ => 56f,
    };

    public void Draw(float uiScale)
    {
        if (!isOpen)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(1100, 700) * uiScale, ImGuiCond.FirstUseEver);
        if (!ImGui.Begin($"{L("SMG1 Flow Editor")}###SMG1FlowEditor", ref isOpen))
        {
            ImGui.End();
            return;
        }

        if (bmg is null)
        {
            ImGui.TextWrapped(L("Message.bmg could not be loaded."));
            ImGui.End();
            return;
        }

        if (nodeIndices.Count == 0)
        {
            ImGui.TextWrapped(L("No flow entry found for this message in Message.bmg's own FLW1 - it may just be a plain, non-flow message."));
            ImGui.End();
            return;
        }

        if (ImGui.Button(L("Add Branch Node")))
        {
            AddNode(new BMGFlowNode.Branch(0, 0, 0, (ushort)bmg.FlowIndirectionTable.Count));
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Add Event Node")))
        {
            AddNode(new BMGFlowNode.Event(0, (ushort)bmg.FlowIndirectionTable.Count, new byte[4]));
        }

        float canvasWidth = ImGui.GetContentRegionAvail().X * 0.75f;
        ImGui.BeginChild("##SMG1FlowCanvas", new Vector2(canvasWidth, 0), ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);
        DrawCanvas(uiScale);
        ImGui.EndChild();

        ImGui.SameLine();

        ImGui.BeginChild("##SMG1FlowDetails", Vector2.Zero, ImGuiChildFlags.Border);
        DrawDetailsPanel(uiScale);
        ImGui.EndChild();

        ImGui.End();
    }

    private void DrawDetailsPanel(float uiScale)
    {
        if (selectedNodeIndex is not int idx || bmg is null || idx >= bmg.FlowNodes.Count)
        {
            ImGui.TextWrapped(L("Click a node's title bar to select it."));
            return;
        }

        if (bmg.FlowNodes[idx] is not BMGFlowNode.Continuation c)
        {
            ImGui.TextWrapped(L("Selected node has no text to edit."));
            return;
        }

        ImGui.TextDisabled(ResolveMessageLabel(c.MessageIndex) ?? "(no label)");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextMultiline("##msgtext", ref editableText, 2048, new Vector2(-1, 260f * uiScale)))
        {
            SaveEditableText();
        }
    }

    private void DrawCanvas(float uiScale)
    {
        if (bmg is null)
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
        Vector2 BoxSize(int index) => new(BoxWidth * scale, BoxHeightFor(bmg.FlowNodes[index]) * scale);

        foreach (int index in nodeIndices)
        {
            Vector2 from = TopLeft(index) + new Vector2(BoxSize(index).X, BoxSize(index).Y / 2);
            switch (bmg.FlowNodes[index])
            {
                case BMGFlowNode.Continuation c:
                    DrawEdge(drawList, from, c.NextNodeIndex, "next", 0xFFB0B0B0, scale);
                    break;
                case BMGFlowNode.Branch b:
                    Vector2 fromTrue = TopLeft(index) + new Vector2(BoxSize(index).X, BodyRowCenterOffsetY(2, scale));
                    Vector2 fromFalse = TopLeft(index) + new Vector2(BoxSize(index).X, BodyRowCenterOffsetY(3, scale));
                    DrawEdge(drawList, fromTrue, IndirectionTarget(bmg, b.IndirectionTableOffset), null, 0xFF4CAF50, scale);
                    DrawEdge(drawList, fromFalse, IndirectionTarget(bmg, b.IndirectionTableOffset + 1), null, 0xFF5A5AD8, scale);
                    break;
                case BMGFlowNode.Event e:
                    DrawEdge(drawList, from, IndirectionTarget(bmg, e.IndirectionTableIndex), "next", 0xFFB0B0B0, scale);
                    break;
            }
        }

        foreach (int index in nodeIndices)
        {
            DrawNode(drawList, index, TopLeft(index), BoxSize(index), scale);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##smg1canvasbg", new Vector2(Math.Max(viewportSize.X, 1f), Math.Max(viewportSize.Y, 1f)));
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
            if (targetIndex == NoNext || targetIndex >= bmg.FlowNodes.Count)
            {
                to = fromPoint + new Vector2(60f * scale, 0);
                dl.AddRectFilled(to - new Vector2(0, 10f * scale), to + new Vector2(70f * scale, 10f * scale), ImGui.GetColorU32(ColorEndOfFlow), 3f * scale);
                dl.AddText(to - new Vector2(-4f * scale, 8f * scale), 0xFFFFFFFF, "End");
            }
            else if (positions.TryGetValue(targetIndex, out Vector2 targetPos))
            {
                to = origin + targetPos * scale + new Vector2(0, BoxHeightFor(bmg.FlowNodes[targetIndex]) * scale / 2);
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

    private static ushort IndirectionTarget(BMGFile bmg, int slot) =>
        slot >= 0 && slot < bmg.FlowIndirectionTable.Count ? bmg.FlowIndirectionTable[slot] : NoNext;

    private static float BodyRowCenterOffsetY(int rowIndex, float scale)
    {
        float bodyTop = 22f * scale + 6f * scale;
        float rowHeight = ImGui.GetFrameHeightWithSpacing();
        return bodyTop + rowHeight * (rowIndex + 0.5f);
    }

    private void DrawNode(ImDrawListPtr drawList, int index, Vector2 topLeft, Vector2 size, float uiScale)
    {
        if (bmg is null)
        {
            return;
        }

        BMGFlowNode node = bmg.FlowNodes[index];
        Vector2 bottomRight = topLeft + size;
        Vector4 titleColorVec = node switch
        {
            BMGFlowNode.Continuation => ColorContinuation,
            BMGFlowNode.Branch => ColorBranch,
            BMGFlowNode.Event => ColorEvent,
            _ => ColorEvent,
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
        ImGui.InvisibleButton($"##smg1dragnode{index}", new Vector2(size.X, titleHeight));
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

    private static string TitleFor(BMGFlowNode node) => node switch
    {
        BMGFlowNode.Continuation c => $"Message (msg {c.MessageIndex})",
        BMGFlowNode.Branch b => $"Branch (query {b.QueryFunctionId})",
        BMGFlowNode.Event e => $"Event (func {e.EventFunctionId})",
        _ => "?",
    };

    private void DrawNodeBody(int index, BMGFlowNode node, Vector2 pos, float width, float uiScale)
    {
        if (bmg is null)
        {
            return;
        }

        ImGui.SetCursorScreenPos(pos);
        ImGui.PushID(index);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        switch (node)
        {
            case BMGFlowNode.Continuation c:
                string? label = ResolveMessageLabel(c.MessageIndex);
                ImGui.TextDisabled(label ?? "(no label)");
                string preview = c.MessageIndex < bmg.Messages.Count ? PlainText(bmg.Messages[c.MessageIndex]) : "(message index out of range)";
                ImGui.TextWrapped(preview.Length > 90 ? preview[..90] + "…" : preview);
                if (ImGui.Button(L("Edit Text..."), new Vector2(width, 0)))
                {
                    selectedNodeIndex = index;
                    SyncEditableTextFromSelection();
                }

                break;

            case BMGFlowNode.Branch br:
                int queryFunc = br.QueryFunctionId;
                ImGui.SetNextItemWidth(LabelAwareWidth(width, "Query"));
                if (ImGui.InputInt("Query", ref queryFunc))
                {
                    ReplaceNode(index, br with { QueryFunctionId = (ushort)Math.Clamp(queryFunc, 0, 0xFFFF) });
                }

                int queryParam = br.QueryParameter;
                ImGui.SetNextItemWidth(LabelAwareWidth(width, "Param"));
                if (ImGui.InputInt("Param", ref queryParam))
                {
                    ReplaceNode(index, br with { QueryParameter = (ushort)Math.Clamp(queryParam, 0, 0xFFFF) });
                }

                DrawTargetCombo("True", br.IndirectionTableOffset, width);
                DrawTargetCombo("False", br.IndirectionTableOffset + 1, width);
                break;

            case BMGFlowNode.Event ev:
                int eventFunc = ev.EventFunctionId;
                ImGui.SetNextItemWidth(LabelAwareWidth(width, "Func"));
                if (ImGui.InputInt("Func", ref eventFunc))
                {
                    ReplaceNode(index, ev with { EventFunctionId = (byte)Math.Clamp(eventFunc, 0, 0xFF) });
                }

                DrawTargetCombo("Next", ev.IndirectionTableIndex, width);
                break;
        }

        ImGui.PopTextWrapPos();
        ImGui.EndGroup();
        ImGui.PopID();
    }

    private static float LabelAwareWidth(float width, string label) =>
        Math.Max(width - ImGui.CalcTextSize(label).X - ImGui.GetStyle().ItemInnerSpacing.X, 40f);

    private void DrawTargetCombo(string id, int slot, float width)
    {
        if (bmg is null || slot < 0 || slot >= bmg.FlowIndirectionTable.Count)
        {
            return;
        }

        ushort current = bmg.FlowIndirectionTable[slot];
        bool isEnd = current == NoNext || current >= bmg.FlowNodes.Count;
        ImGui.SetNextItemWidth(LabelAwareWidth(width, id));

        if (ImGui.BeginCombo($"{id}##target{slot}", isEnd ? "(End of Flow)" : NodeOptionLabel(current)))
        {
            if (ImGui.Selectable(L("(End of Flow)"), isEnd))
            {
                SetIndirection(slot, NoNext);
            }

            for (int i = 0; i < bmg.FlowNodes.Count; i++)
            {
                if (ImGui.Selectable(NodeOptionLabel(i), i == current))
                {
                    SetIndirection(slot, (ushort)i);
                }
            }

            ImGui.EndCombo();
        }
    }

    private string NodeOptionLabel(int index) => bmg is null ? index.ToString() : $"[{index}] {TitleFor(bmg.FlowNodes[index])}";

    private void SetIndirection(int slot, ushort target)
    {
        if (gameRootDir is null || outputDir is null)
        {
            return;
        }

        SMG1Text.SetFlowIndirection(gameRootDir, outputDir, slot, target);
        (bmg, labelToIndex) = SMG1Text.LoadBMG(gameRootDir, outputDir);
        RebuildGraph();
    }

    private string? ResolveMessageLabel(int messageIndex) =>
        labelToIndex?.FirstOrDefault(kv => kv.Value == messageIndex).Key;

    private static string PlainText(BMGMessage message)
    {
        var sb = new System.Text.StringBuilder();
        foreach (BMGTextRun part in message.Parts)
        {
            if (part is BMGTextRun.Literal literal)
            {
                sb.Append(literal.Value);
            }
        }

        return sb.ToString();
    }

    private void SyncEditableTextFromSelection()
    {
        if (selectedNodeIndex is not int idx || bmg is null || bmg.FlowNodes[idx] is not BMGFlowNode.Continuation c)
        {
            editableText = "";
            return;
        }

        editableText = c.MessageIndex < bmg.Messages.Count ? PlainText(bmg.Messages[c.MessageIndex]) : "";
    }

    private void AddNode(BMGFlowNode node)
    {
        if (bmg is null || gameRootDir is null || outputDir is null)
        {
            return;
        }

        int extraSlots = node is BMGFlowNode.Branch ? 2 : 1;
        SMG1Text.AddFlowNode(gameRootDir, outputDir, node, extraSlots);
        (bmg, labelToIndex) = SMG1Text.LoadBMG(gameRootDir, outputDir);
        if (bmg is null)
        {
            return;
        }

        int newIndex = bmg.FlowNodes.Count - 1;
        unreachableAddedNodes.Add(newIndex);
        nodeIndices.Add(newIndex);
        positions[newIndex] = new Vector2(0, (positions.Count > 0 ? positions.Values.Max(p => p.Y) : 0) + BranchBoxHeight + RowGap);
    }

    private void ReplaceNode(int index, BMGFlowNode updated)
    {
        if (gameRootDir is null || outputDir is null)
        {
            return;
        }

        SMG1Text.SetFlowNode(gameRootDir, outputDir, index, updated);
        (bmg, labelToIndex) = SMG1Text.LoadBMG(gameRootDir, outputDir);
        RebuildGraph();
    }

    private void SaveEditableText()
    {
        if (gameRootDir is null || outputDir is null || selectedNodeIndex is not int idx || bmg is null || bmg.FlowNodes[idx] is not BMGFlowNode.Continuation c)
        {
            return;
        }

        SMG1Text.SetMessageTextByIndex(gameRootDir, outputDir, c.MessageIndex, editableText);
        (bmg, labelToIndex) = SMG1Text.LoadBMG(gameRootDir, outputDir);
    }
}
