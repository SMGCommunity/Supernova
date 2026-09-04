using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SMGEditor.Editor;

[SupportedOSPlatform("windows")]
internal static class NativeCrashDialog
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(string text) => MessageBoxW(IntPtr.Zero, text, "Supernova crashed", 0x10);
}
