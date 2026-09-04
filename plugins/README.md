# Plugins

Drop a plugin's `.dll` (plus any private dependencies it ships) into this folder, then open
**Settings** on the galaxy list and **approve** it. A file that has not been approved is never
loaded. Once approved, open it from **Tools > Plugins** inside the level editor.

Approval is tied to the file's SHA-256, so replacing a plugin means approving it again. See the
Security section of [`docs/PLUGINS.md`](../docs/PLUGINS.md).

Plugins are standalone editors - useful for SMG2 romhack extensions that add their own BCSVs or
files. See [`docs/PLUGINS.md`](../docs/PLUGINS.md) for how to write one, and
[`samples/BcsvPeekPlugin`](../samples/BcsvPeekPlugin) for a working example.

Do **not** copy `SMGEditor.*.dll`, `ImGui.NET.dll`, `Silk.NET.*.dll`, or `StbImage*.dll` here -
Supernova provides those, and duplicates will stop the plugin from loading.
