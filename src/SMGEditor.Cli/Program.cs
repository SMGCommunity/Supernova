using SMGEditor.Core.Formats;

// CLI project is for testing things quickly through CLI instead of directly in the editor
// DEBUG ONLY
string path = args.Length > 0 ? args[0] : FindDefaultStagePath();

Console.WriteLine($"Opening stage: {path}");
RARCArchive archive = RARCArchive.Load(path);

Console.WriteLine("Contents:");
PrintDirectory(archive.Root, 1);

RARCFile? objInfo = FindFile(archive.Root, "ObjInfo");
if (objInfo is not null)
{
    Console.WriteLine();
    Console.WriteLine("ObjInfo.bcsv:");
    BCSVTable table = BCSVTable.Load(objInfo.Data);
    PrintBCSV(table);
}

return;

static void PrintDirectory(RARCDirectory dir, int depth)
{
    string indent = new(' ', depth * 2);

    foreach (RARCFile file in dir.Files)
    {
        Console.WriteLine($"{indent}{file.Name} ({file.Data.Length:N0} bytes)");
    }

    foreach (RARCDirectory sub in dir.Directories)
    {
        Console.WriteLine($"{indent}{sub.Name}/");
        PrintDirectory(sub, depth + 1);
    }
}

static RARCFile? FindFile(RARCDirectory dir, string name)
{
    foreach (RARCFile file in dir.Files)
    {
        if (string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return file;
        }
    }

    foreach (RARCDirectory sub in dir.Directories)
    {
        RARCFile? found = FindFile(sub, name);
        if (found is not null)
        {
            return found;
        }
    }

    return null;
}

static void PrintBCSV(BCSVTable table)
{
    Console.WriteLine($"  Fields: {string.Join(", ", table.Fields.Select(f => $"{f.Name}:{f.Type}"))}");
    Console.WriteLine($"  {table.Rows.Count} row(s):");

    foreach (IReadOnlyDictionary<string, object?> row in table.Rows.Take(5))
    {
        string values = string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value}"));
        Console.WriteLine($"    {{ {values} }}");
    }
}

static string FindDefaultStagePath()
{
    for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "smg_files", "1", "DATA", "files", "StageData", "PeachCastleGardenGalaxy.arc");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    throw new FileNotFoundException(
        "Could not locate smg_files/1/DATA/files/StageData/PeachCastleGardenGalaxy.arc automatically. Pass a stage .arc path as an argument.");
}
