using System.Numerics;
using ImGuiNET;

namespace SMGEditor.Editor;

// custom file browser implementation
// did this because macOS sucks ass and we had to implement our own because Windows
internal sealed class FileBrowser
{
    public enum Mode { Folder, File }
    public enum DrawResult { None, Confirmed, Cancelled }

    private const string PopupId = "Browse##FileBrowser";

    private Mode _mode;
    private string[] _extensions = [];
    private string _currentDir = "";
    private string _selectedFile = "";
    private string _manualPath = "";
    private bool _openRequested;
    private bool _confirmRequested;
    private bool _shouldBeOpen;

    public string SelectedPath { get; private set; } = "";

    public void OpenFolder(string startDir) => Open(Mode.Folder, startDir, []);

    public void OpenFile(string startDir, params string[] extensions) => Open(Mode.File, startDir, extensions);

    private void Open(Mode mode, string startDir, string[] extensions)
    {
        _mode = mode;
        _extensions = extensions;
        _currentDir = ResolveStartDir(startDir);
        _manualPath = _currentDir;
        _selectedFile = "";
        _confirmRequested = false;
        _openRequested = true;
        _shouldBeOpen = true;
    }

    public DrawResult Draw(float uiScale)
    {
        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;
        }
        else if (_shouldBeOpen && !ImGui.IsPopupOpen(PopupId))
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _shouldBeOpen = false;
                return DrawResult.Cancelled;
            }

            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowSize(new Vector2(720, 540) * uiScale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(PopupId, ImGuiWindowFlags.NoResize))
        {
            return DrawResult.None;
        }

        DrawResult result = DrawResult.None;

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _shouldBeOpen = false;
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return DrawResult.Cancelled;
        }

        ImGui.SetNextItemWidth(-90 * uiScale);
        if (ImGui.InputText("##path", ref _manualPath, 1024, ImGuiInputTextFlags.EnterReturnsTrue) && Directory.Exists(_manualPath))
        {
            _currentDir = Path.GetFullPath(_manualPath);
            _selectedFile = "";
        }

        ImGui.SameLine();
        if (ImGui.Button(L("Up"), new Vector2(80 * uiScale, 0)))
        {
            NavigateUp();
        }

        DrawRoots(uiScale);

        ImGui.BeginChild("##entries", new Vector2(0, -44 * uiScale), ImGuiChildFlags.Borders);
        DrawEntries();
        ImGui.EndChild();

        bool canConfirm = _mode == Mode.Folder ? Directory.Exists(_currentDir) : _selectedFile.Length > 0;

        ImGui.BeginDisabled(!canConfirm);
        if (ImGui.Button(_mode == Mode.Folder ? L("Select this folder") : L("Open"), new Vector2(150 * uiScale, 0)) || _confirmRequested)
        {
            SelectedPath = _mode == Mode.Folder ? _currentDir : Path.Combine(_currentDir, _selectedFile);
            result = DrawResult.Confirmed;
            _shouldBeOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        _confirmRequested = false;

        ImGui.SameLine();
        if (ImGui.Button(L("Cancel"), new Vector2(100 * uiScale, 0)))
        {
            result = DrawResult.Cancelled;
            _shouldBeOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return result;
    }

    private void DrawRoots(float uiScale)
    {
        bool first = true;
        foreach (string root in EnumerateRoots())
        {
            if (!first)
            {
                ImGui.SameLine();
            }

            first = false;
            if (ImGui.SmallButton($"{root}##root"))
            {
                _currentDir = root;
                _manualPath = root;
                _selectedFile = "";
            }
        }
    }

    private void DrawEntries()
    {
        string[] dirs;
        string[] files;
        try
        {
            dirs = Directory.GetDirectories(_currentDir);
            files = _mode == Mode.File
                ? Directory.GetFiles(_currentDir).Where(MatchesExtension).ToArray()
                : [];
        }
        catch (Exception ex)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.5f, 0.4f, 1f), LF("Cannot open this folder: {0}", ex.Message));
            return;
        }

        Array.Sort(dirs, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
        Array.Sort(files, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

        foreach (string dir in dirs)
        {
            string name = Path.GetFileName(dir);
            if (ImGui.Selectable($"[ dir ]  {name}##d_{name}", false, ImGuiSelectableFlags.AllowDoubleClick)
                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                _currentDir = dir;
                _manualPath = dir;
                _selectedFile = "";
            }
        }

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (ImGui.Selectable($"{name}##f_{name}", name == _selectedFile, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedFile = name;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    _confirmRequested = true;
                }
            }
        }
    }

    private bool MatchesExtension(string path) =>
        _extensions.Length == 0 || _extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void NavigateUp()
    {
        string? parent = Path.GetDirectoryName(_currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
        {
            _currentDir = parent;
            _manualPath = parent;
            _selectedFile = "";
        }
    }

    private static IEnumerable<string> EnumerateRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            yield return home;
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    yield return drive.Name;
                }
            }
        }
        else
        {
            yield return "/";
        }
    }

    private static string ResolveStartDir(string startDir)
    {
        if (!string.IsNullOrWhiteSpace(startDir))
        {
            if (Directory.Exists(startDir))
            {
                return startDir;
            }

            string? parent = Path.GetDirectoryName(startDir);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 ? home : OperatingSystem.IsWindows() ? "C:\\" : "/";
    }
}
