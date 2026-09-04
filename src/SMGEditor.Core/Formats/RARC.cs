using System.Buffers.Binary;
using System.Text;

namespace SMGEditor.Core.Formats;

public sealed class RARCFile
{
    public required string Name { get; init; }
    public ushort FileId { get; init; }
    public required byte[] Data { get; init; }

    public byte Flags { get; init; } = 0x11;
}

public sealed class RARCDirectory
{
    public required string Name { get; init; }
    public List<RARCDirectory> Directories { get; } = [];
    public List<RARCFile> Files { get; } = [];

    public RARCDirectory? FindDirectory(string path)
    {
        RARCDirectory current = this;
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            RARCDirectory? next = current.Directories.Find(d => string.Equals(d.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    public RARCFile? FindFile(string path)
    {
        int lastSlash = path.LastIndexOf('/');
        string fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        RARCDirectory? dir = lastSlash >= 0 ? FindDirectory(path[..lastSlash]) : this;
        return dir?.Files.Find(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
    }

    public RARCFile? FindFileByName(string name)
    {
        RARCFile? direct = Files.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return direct;
        }

        foreach (RARCDirectory sub in Directories)
        {
            RARCFile? found = sub.FindFileByName(name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public RARCDirectory? FindContainingDirectory(string fileName)
    {
        if (Files.Exists(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return this;
        }

        foreach (RARCDirectory sub in Directories)
        {
            RARCDirectory? found = sub.FindContainingDirectory(fileName);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public void ReplaceFileData(RARCFile file, byte[] newData)
    {
        int index = Files.IndexOf(file);
        if (index >= 0)
        {
            Files[index] = new RARCFile { Name = file.Name, FileId = file.FileId, Data = newData, Flags = file.Flags };
        }
    }

    public bool ReplaceFileDataByName(string name, byte[] newData)
    {
        RARCFile? file = Files.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (file is not null)
        {
            ReplaceFileData(file, newData);
            return true;
        }

        foreach (RARCDirectory sub in Directories)
        {
            if (sub.ReplaceFileDataByName(name, newData))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<RARCFile> FindFilesByExtension(string extension)
    {
        foreach (RARCFile file in Files)
        {
            if (file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (RARCDirectory sub in Directories)
        {
            foreach (RARCFile file in sub.FindFilesByExtension(extension))
            {
                yield return file;
            }
        }
    }
}

public sealed class RARCArchive
{
    public required RARCDirectory Root { get; init; }

    public ushort SyncFileIds { get; init; }

    public ushort? NextAvailFileId { get; init; }

    // these are flags for how the file is to be loaded. probably not going to end up allowing the user to mess with these
    [Flags]
    private enum EntryFlags : byte
    {
        File = 1 << 0,
        Folder = 1 << 1,
        Compressed = 1 << 2,
        PreloadToMRam = 1 << 4,
        PreloadToARam = 1 << 5,
        PreloadToDvd = 1 << 6,
        Yaz0Compressed = 1 << 7,
    }

    public static RARCArchive Load(string path) => Load(File.ReadAllBytes(path));

    public static RARCArchive Load(byte[] raw)
    {
        byte[] data = Yaz0.IsCompressed(raw) ? Yaz0.Decompress(raw) : raw;
        return Parse(data);
    }

    private static RARCArchive Parse(byte[] data)
    {
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4));
        if (magic != 0x52415243)
        {
            throw new InvalidDataException("Not a RARC archive.");
        }

        uint headerSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x8, 4));
        uint fileDataOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0xC, 4));

        int infoBlockStart = (int)headerSize;
        uint dirCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(infoBlockStart + 0x0, 4));
        uint dirOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(infoBlockStart + 0x4, 4));
        uint fileCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(infoBlockStart + 0x8, 4));
        uint fileOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(infoBlockStart + 0xC, 4));
        uint stringTableOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(infoBlockStart + 0x14, 4));
        ushort syncFileIds = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(infoBlockStart + 0x1A, 2));
        ushort nextAvailFileId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(infoBlockStart + 0x18, 2));

        int dirsStart = infoBlockStart + (int)dirOffset;
        int filesStart = infoBlockStart + (int)fileOffset;
        int stringTableStart = infoBlockStart + (int)stringTableOffset;
        int dataStart = (int)headerSize + (int)fileDataOffset;

        var dirs = new (uint NameOffset, ushort FirstFileIndex, ushort FileCountInDir)[dirCount];
        for (int i = 0; i < dirCount; i++)
        {
            int entryStart = dirsStart + i * 0x10;
            uint nameOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0x4, 4));
            ushort nrFiles = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entryStart + 0xA, 2));
            uint firstFileIndex = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0xC, 4));
            dirs[i] = (nameOffset, (ushort)firstFileIndex, nrFiles);
        }

        var visited = new HashSet<uint>();
        RARCDirectory root = ReadDirectory(0);
        return new RARCArchive { Root = root, SyncFileIds = syncFileIds, NextAvailFileId = nextAvailFileId };

        RARCDirectory ReadDirectory(uint dirIndex)
        {
            var (nameOffset, firstFileIndex, fileCountInDir) = dirs[dirIndex];
            var dir = new RARCDirectory { Name = ReadCString(data, stringTableStart + (int)nameOffset) };

            if (!visited.Add(dirIndex))
            {
                return dir;
            }

            for (int i = 0; i < fileCountInDir; i++)
            {
                int entryStart = filesStart + (firstFileIndex + i) * 0x14;
                ushort fileId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(entryStart + 0x0, 2));
                uint flagAndNameOffset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0x4, 4));
                var flags = (EntryFlags)(flagAndNameOffset >> 24);
                uint entryNameOffset = flagAndNameOffset & 0xFFFFFF;
                uint dataOffsetOrDirIndex = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0x8, 4));
                uint dataSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryStart + 0xC, 4));

                string entryName = ReadCString(data, stringTableStart + (int)entryNameOffset);

                if (flags.HasFlag(EntryFlags.Folder))
                {
                    if (entryName is "." or "..")
                    {
                        continue;
                    }

                    RARCDirectory child = ReadDirectory(dataOffsetOrDirIndex);
                    dir.Directories.Add(child);
                }
                else
                {
                    byte[] fileData = new byte[dataSize];
                    Array.Copy(data, dataStart + (int)dataOffsetOrDirIndex, fileData, 0, (int)dataSize);
                    dir.Files.Add(new RARCFile { Name = entryName, FileId = fileId, Data = fileData, Flags = (byte)flags });
                }
            }

            return dir;
        }
    }

    private static string ReadCString(byte[] data, int offset)
    {
        int end = offset;
        while (data[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    private const int Alignment = 32;

    private static int AlignUp(int value) => (value + Alignment - 1) / Alignment * Alignment;

    private static ushort NameHash(string name)
    {
        uint hash = 0;
        foreach (char c in name)
        {
            hash = (byte)c + (hash * 3);
        }

        return (ushort)hash;
    }

    private static byte[] DirectoryId(string name, bool isRoot)
    {
        if (isRoot)
        {
            return "ROOT"u8.ToArray();
        }

        byte[] id = [0x20, 0x20, 0x20, 0x20];
        for (int i = 0; i < Math.Min(4, name.Length); i++)
        {
            id[i] = (byte)char.ToUpperInvariant(name[i]);
        }

        return id;
    }

    private sealed class PendingEntry
    {
        public required string Name { get; init; }
        public required bool IsFolder { get; init; }
        public int ReferencedDirIndex { get; init; }
        public RARCFile? File { get; init; }
        public int DataOffset { get; set; }
    }

    public byte[] Save()
    {
        var allDirs = new List<RARCDirectory>();
        var dirIndexOf = new Dictionary<RARCDirectory, int>();
        var parentOf = new List<int>();

        void VisitDir(RARCDirectory dir, int parentIdx)
        {
            int myIndex = allDirs.Count;
            allDirs.Add(dir);
            dirIndexOf[dir] = myIndex;
            parentOf.Add(parentIdx);
            foreach (RARCDirectory sub in dir.Directories)
            {
                VisitDir(sub, myIndex);
            }
        }

        VisitDir(Root, -1);

        using var pool = new MemoryStream();
        var nameOffsetOfDir = new Dictionary<RARCDirectory, int>();
        var nameOffsetOfFile = new Dictionary<RARCFile, int>();

        int WritePoolString(string s)
        {
            int offset = (int)pool.Position;
            pool.Write(Encoding.ASCII.GetBytes(s));
            pool.WriteByte(0);
            return offset;
        }

        WritePoolString(".");
        WritePoolString("..");
        foreach (RARCDirectory dir in allDirs)
        {
            nameOffsetOfDir[dir] = WritePoolString(dir.Name);
            foreach (RARCFile file in dir.Files)
            {
                nameOffsetOfFile[file] = WritePoolString(file.Name);
            }
        }

        var entries = new List<PendingEntry>();
        var firstFileIndexOfDir = new int[allDirs.Count];
        var fileCountOfDir = new int[allDirs.Count];

        for (int i = 0; i < allDirs.Count; i++)
        {
            RARCDirectory dir = allDirs[i];
            firstFileIndexOfDir[i] = entries.Count;

            foreach (RARCFile file in dir.Files)
            {
                entries.Add(new PendingEntry { Name = file.Name, IsFolder = false, File = file });
            }

            foreach (RARCDirectory sub in dir.Directories)
            {
                entries.Add(new PendingEntry { Name = sub.Name, IsFolder = true, ReferencedDirIndex = dirIndexOf[sub] });
            }

            entries.Add(new PendingEntry { Name = ".", IsFolder = true, ReferencedDirIndex = i });
            entries.Add(new PendingEntry { Name = "..", IsFolder = true, ReferencedDirIndex = parentOf[i] });

            fileCountOfDir[i] = entries.Count - firstFileIndexOfDir[i];
        }

        int realFileCounter = 0;
        int dataCursor = 0;
        foreach (PendingEntry entry in entries)
        {
            if (entry.File is { } file)
            {
                entry.DataOffset = dataCursor;
                dataCursor = AlignUp(dataCursor + file.Data.Length);
                realFileCounter++;
            }
        }

        int totalDataSize = dataCursor;
        int nextAvailFileId = NextAvailFileId ?? (SyncFileIds != 0 ? entries.Count : realFileCounter);

        const int headerSize = 0x20;
        int infoBlockHeaderSize = 0x1C;
        int dirsStart = AlignUp(headerSize + infoBlockHeaderSize);
        int filesStart = AlignUp(dirsStart + (allDirs.Count * 0x10));
        int stringTableStart = AlignUp(filesStart + (entries.Count * 0x14));
        int dataStart = AlignUp(stringTableStart + (int)pool.Length);

        using var output = new MemoryStream();

        Span<byte> header = stackalloc byte[headerSize];
        BinaryPrimitives.WriteUInt32BigEndian(header[0..4], 0x52415243);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)(dataStart + totalDataSize));
        BinaryPrimitives.WriteUInt32BigEndian(header[8..12], headerSize);
        BinaryPrimitives.WriteUInt32BigEndian(header[12..16], (uint)(dataStart - headerSize));
        BinaryPrimitives.WriteUInt32BigEndian(header[16..20], (uint)totalDataSize);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..24], (uint)totalDataSize);
        BinaryPrimitives.WriteUInt32BigEndian(header[24..28], 0);
        BinaryPrimitives.WriteUInt32BigEndian(header[28..32], 0);
        output.Write(header);

        Span<byte> infoBlock = stackalloc byte[infoBlockHeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(infoBlock[0..4], (uint)allDirs.Count);
        BinaryPrimitives.WriteUInt32BigEndian(infoBlock[4..8], (uint)(dirsStart - headerSize));
        BinaryPrimitives.WriteUInt32BigEndian(infoBlock[8..12], (uint)entries.Count);
        BinaryPrimitives.WriteUInt32BigEndian(infoBlock[12..16], (uint)(filesStart - headerSize));
        BinaryPrimitives.WriteUInt32BigEndian(infoBlock[16..20], (uint)(dataStart - stringTableStart));
        BinaryPrimitives.WriteUInt32BigEndian(infoBlock[20..24], (uint)(stringTableStart - headerSize));
        BinaryPrimitives.WriteUInt16BigEndian(infoBlock[24..26], (ushort)nextAvailFileId);
        BinaryPrimitives.WriteUInt16BigEndian(infoBlock[26..28], SyncFileIds);
        output.Write(infoBlock);

        PadTo(output, dirsStart);

        Span<byte> dirEntry = stackalloc byte[0x10];
        for (int i = 0; i < allDirs.Count; i++)
        {
            RARCDirectory dir = allDirs[i];
            dirEntry.Clear();
            DirectoryId(dir.Name, i == 0).CopyTo(dirEntry[0..4]);
            BinaryPrimitives.WriteUInt32BigEndian(dirEntry[4..8], (uint)nameOffsetOfDir[dir]);
            BinaryPrimitives.WriteUInt16BigEndian(dirEntry[8..10], NameHash(dir.Name));
            BinaryPrimitives.WriteUInt16BigEndian(dirEntry[10..12], (ushort)fileCountOfDir[i]);
            BinaryPrimitives.WriteUInt32BigEndian(dirEntry[12..16], (uint)firstFileIndexOfDir[i]);
            output.Write(dirEntry);
        }

        PadTo(output, filesStart);

        Span<byte> fileEntry = stackalloc byte[0x14];
        foreach (PendingEntry entry in entries)
        {
            fileEntry.Clear();
            bool isDotEntry = entry.Name is "." or "..";
            int nameOffset = entry.File is { } f ? nameOffsetOfFile[f]
                : isDotEntry ? (entry.Name == "." ? 0 : 2)
                : nameOffsetOfDir[allDirs[entry.ReferencedDirIndex]];

            ushort fileId = entry.IsFolder ? (ushort)0xFFFF : entry.File!.FileId;
            ushort hash = NameHash(entry.Name);
            byte flag = entry.IsFolder ? (byte)0x02 : entry.File!.Flags;
            uint dataOffsetOrDirIndex = entry.IsFolder
                ? (entry.ReferencedDirIndex < 0 ? 0xFFFFFFFFu : (uint)entry.ReferencedDirIndex)
                : (uint)entry.DataOffset;
            uint dataSize = entry.IsFolder ? 16u : (uint)entry.File!.Data.Length;

            BinaryPrimitives.WriteUInt16BigEndian(fileEntry[0..2], fileId);
            BinaryPrimitives.WriteUInt16BigEndian(fileEntry[2..4], hash);
            BinaryPrimitives.WriteUInt32BigEndian(fileEntry[4..8], (uint)((flag << 24) | (nameOffset & 0xFFFFFF)));
            BinaryPrimitives.WriteUInt32BigEndian(fileEntry[8..12], dataOffsetOrDirIndex);
            BinaryPrimitives.WriteUInt32BigEndian(fileEntry[12..16], dataSize);
            output.Write(fileEntry);
        }

        PadTo(output, stringTableStart);
        pool.Position = 0;
        pool.CopyTo(output);

        PadTo(output, dataStart);
        foreach (PendingEntry entry in entries)
        {
            if (entry.File is { } file)
            {
                PadTo(output, dataStart + entry.DataOffset);
                output.Write(file.Data);
            }
        }

        PadTo(output, dataStart + totalDataSize);

        return output.ToArray();
    }

    private static void PadTo(MemoryStream stream, int targetPosition)
    {
        while (stream.Position < targetPosition)
        {
            stream.WriteByte(0);
        }
    }
}
