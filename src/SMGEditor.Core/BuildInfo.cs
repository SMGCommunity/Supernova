using System.Reflection;

namespace SMGEditor.Core;

public static class BuildInfo
{
    public static string Version { get; } = ResolveVersion();

    public static string DisplayVersion => Version == "dev" ? "dev build" : $"build {Version}";

    private static string ResolveVersion()
    {
        string? informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrEmpty(informational) ? "dev" : informational;
    }
}
