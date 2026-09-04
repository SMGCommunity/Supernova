using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace SMGEditor.Editor;

internal sealed record PluginScanResult(
    string Sha256,
    IReadOnlyList<string> PluginTypeNames,
    IReadOnlyList<string> Capabilities,
    string? Error)
{
    public bool HasPluginTypes => PluginTypeNames.Count > 0;
}

internal static class PluginScanner
{
    private const string PluginInterfaceFullName = "SMGEditor.PluginApi.ISupernovaEditorPlugin";

    public static PluginScanResult Scan(string dllPath)
    {
        string sha;
        try
        {
            using FileStream stream = File.OpenRead(dllPath);
            sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            return new PluginScanResult("", [], [], $"Could not read the file: {ex.Message}");
        }

        try
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            if (!pe.HasMetadata)
            {
                return new PluginScanResult(sha, [], [], "Not a managed .NET assembly.");
            }

            MetadataReader md = pe.GetMetadataReader();
            return new PluginScanResult(sha, FindPluginTypes(md), DetectCapabilities(md), null);
        }
        catch (Exception ex)
        {
            return new PluginScanResult(sha, [], [], $"Could not read metadata: {ex.Message}");
        }
    }

    private static List<string> FindPluginTypes(MetadataReader md)
    {
        var result = new List<string>();

        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            TypeDefinition type = md.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.Abstract) != 0 || (type.Attributes & TypeAttributes.Interface) != 0)
            {
                continue;
            }

            bool implementsPlugin = false;
            foreach (InterfaceImplementationHandle implHandle in type.GetInterfaceImplementations())
            {
                InterfaceImplementation impl = md.GetInterfaceImplementation(implHandle);
                if (FullName(md, impl.Interface) == PluginInterfaceFullName)
                {
                    implementsPlugin = true;
                    break;
                }
            }

            if (implementsPlugin)
            {
                string ns = md.GetString(type.Namespace);
                string name = md.GetString(type.Name);
                result.Add(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
            }
        }

        return result;
    }

    private static List<string> DetectCapabilities(MetadataReader md)
    {
        var caps = new SortedSet<string>(StringComparer.Ordinal);

        foreach (MethodDefinitionHandle handle in md.MethodDefinitions)
        {
            MethodDefinition method = md.GetMethodDefinition(handle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0
                || (method.ImplAttributes & MethodImplAttributes.Unmanaged) != 0)
            {
                caps.Add("Call native code");
                break;
            }
        }

        foreach (TypeReferenceHandle handle in md.TypeReferences)
        {
            TypeReference typeRef = md.GetTypeReference(handle);
            string ns = md.GetString(typeRef.Namespace);
            string name = md.GetString(typeRef.Name);

            if (ns == "System.Net" || ns.StartsWith("System.Net.", StringComparison.Ordinal))
            {
                caps.Add("Network access");
            }
            else if (ns == "System.Diagnostics" && name is "Process" or "ProcessStartInfo")
            {
                caps.Add("Launch other programs");
            }
            else if (ns == "System.Runtime.InteropServices" && name is "DllImportAttribute" or "LibraryImportAttribute" or "NativeLibrary")
            {
                caps.Add("Call native code");
            }
            else if (ns.StartsWith("System.Reflection.Emit", StringComparison.Ordinal))
            {
                caps.Add("Generate and run new code");
            }
            else if (ns == "System.Runtime.Loader" && name == "AssemblyLoadContext")
            {
                caps.Add("Load other assemblies");
            }
            else if (ns == "Microsoft.Win32" && name is "Registry" or "RegistryKey")
            {
                caps.Add("Windows registry access");
            }
        }

        foreach (MemberReferenceHandle handle in md.MemberReferences)
        {
            MemberReference memberRef = md.GetMemberReference(handle);
            if (memberRef.GetKind() != MemberReferenceKind.Method)
            {
                continue;
            }

            string member = md.GetString(memberRef.Name);
            string parent = FullName(md, memberRef.Parent);

            if (parent is "System.IO.File" or "System.IO.Directory" && member is "Delete" or "Move")
            {
                caps.Add("Delete or move files");
            }
            else if (parent == "System.Reflection.Assembly" && member is "Load" or "LoadFrom" or "LoadFile" or "UnsafeLoadFrom")
            {
                caps.Add("Load other assemblies");
            }
            else if (parent == "System.Environment" && member == "SetEnvironmentVariable")
            {
                caps.Add("Change environment variables");
            }
        }

        return [.. caps];
    }

    private static string FullName(MetadataReader md, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                TypeReference typeRef = md.GetTypeReference((TypeReferenceHandle)handle);
                string ns = md.GetString(typeRef.Namespace);
                string name = md.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            case HandleKind.TypeDefinition:
            {
                TypeDefinition typeDef = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                string ns = md.GetString(typeDef.Namespace);
                string name = md.GetString(typeDef.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            }

            default:
                return "";
        }
    }
}
