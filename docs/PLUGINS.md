# Writing a Supernova plugin

A plugin is a .NET assembly that adds a **standalone editor window** to Supernova. It is meant for
things Supernova doesn't handle natively. Most often SMG2 hacks extensions that introduce their
own BCSV tables or modified fields.

## How it works

- Supernova scans the `plugins/` folder (next to the executable) on startup, recursively, for
  `.dll` files.
- Every non-abstract class with a public parameterless constructor that implements
  `SMGEditor.PluginApi.ISupernovaEditorPlugin` becomes a plugin.
- Plugins are listed under **Settings** on the galaxy-selection screen. A plugin only does anything
  once it is enabled there; the enabled set is remembered in `editorsettings.json`.
- When an enabled plugin exists, the level editor's **Tools** menu gains a **Plugins** submenu.
  Each entry toggles that plugin's window.
- Adding or updating a plugin file requires a restart. Enabling/disabling does not.

## The contract

Reference `SMGEditor.PluginApi` (it comes with a transitive reference to `SMGEditor.Core`, so you
get `RARCArchive`, `Yaz0`, `BCSVTable`, etc.).

```csharp
public interface ISupernovaEditorPlugin
{
    PluginInfo Info { get; }
    int SupportedGame { get; }                       // 1, 2, or 0 for "either"

    void OnProjectOpened(IPluginContext context) {}  // a project became active
    void OnProjectClosed() {}                        // no project is active
    void DrawWindow(IPluginContext context);         // called every frame while the window is open
}

public sealed record PluginInfo(
    string Id,          // stable unique id, e.g. "com.yourname.myeditor"
    string Name,        // shown in menus and the window title
    string? Description = null,
    string? Author = null,
    string? Version = null);
```

`DrawWindow` runs **inside** a window Supernova already opened for you (`ImGui.Begin`/`End` are
handled by the host) and they just emit widgets. Exceptions thrown from any plugin method are caught and
shown; they will not crash Supernova.

`Info.Id` identifies the plugin for the enabled-list, so keep it stable across versions.

## The host API

```csharp
public interface IPluginContext
{
    bool HasProject { get; }         // false on the hub / between projects
    int Game { get; }                // 1 or 2
    string GameRootDir { get; }      // extracted retail files (read-only)
    string OutputDir { get; }        // where your edits must be written
    string? GalaxyName { get; }      // the open galaxy, or null
    float UiScale { get; }

    byte[]? ReadFile(string relativePath);              // OutputDir first, then GameRootDir
    void WriteOutputFile(string relativePath, byte[] data);

    RARCArchive? LoadArchive(string relativePath, out bool wasCompressed);
    void SaveArchive(string relativePath, RARCArchive archive, bool compress);

    void Status(string message);     // shows in the editor's status bar
}
```

Always write into `OutputDir`; never touch `GameRootDir`. `LoadArchive` transparently prefers a
copy already in `OutputDir`, handles Yaz0, and returns `null` if the file does not exist.

## Building

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <GenerateDependencyFile>false</GenerateDependencyFile>
    <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
  </PropertyGroup>
  <ItemGroup>
    <!-- reference the assemblies Supernova ships, but never copy them -->
    <Reference Include="SMGEditor.PluginApi"><HintPath>libs/SMGEditor.PluginApi.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="SMGEditor.Core"><HintPath>libs/SMGEditor.Core.dll</HintPath><Private>false</Private></Reference>
    <PackageReference Include="ImGui.NET" Version="1.91.6.1"><ExcludeAssets>runtime;native</ExcludeAssets></PackageReference>
  </ItemGroup>
</Project>
```

Copy `SMGEditor.PluginApi.dll` and `SMGEditor.Core.dll` out of a Supernova build to reference
them (or, if you build inside this repo, use `<ProjectReference>` to the two projects with
`<Private>false</Private>`, as `samples/BcsvPeekPlugin` does).

Use the **same `ImGui.NET` version Supernova ships** (`1.91.6.1`). With the settings above,
`dotnet build` produces just your plugin's `.dll`. If your plugin pulls its own NuGet packages,
their DLLs are copied next to yours and load fine - just never ship copies of `SMGEditor.*`,
`ImGui.NET`, `Silk.NET.*`, or `StbImage*`.

## Install

1. `dotnet build -c Release`
2. Copy your plugin's `.dll` (and any private dependency DLLs) into `plugins/`.
3. Start Supernova, open **Settings** on the galaxy list, tick your plugin.
4. Open a galaxy, then **Tools ▸ Plugins ▸ <your plugin>**.

See [`samples/BcsvPeekPlugin`](../samples/BcsvPeekPlugin) for a complete example.
