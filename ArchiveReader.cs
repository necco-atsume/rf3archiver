namespace Rf3Archiver
{
    public class ArchiveReader
    {
        private readonly string filePath;

        public ArchiveReader(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new ArgumentException($"'File {nameof(filePath)}' doesn't exist.", nameof(filePath));
            }

            this.filePath = filePath;
        }

        public List<ArchiveEntry> Read()
        {
            var archive = File.ReadAllBytes(filePath);
            return ReadArchive(archive);
        }

        public static List<ArchiveEntry> ReadArchive(byte[] archive)
        {
            var file = archive.AsMemory();
            List<ArchiveEntry> entries = new();

            using (var stream = new MemoryStream(archive))
            using (var reader = new BinaryReader(stream))
            {
                List<ArchivePointer> pointers = new List<ArchivePointer>();

                ushort fileEntryOffset = reader.ReadUInt16();
                ushort fileEntryLength = reader.ReadUInt16();
                reader.ReadBytes(4);
                uint fileCount = reader.ReadUInt32();
                uint firstFileOffset = fileCount * fileEntryLength + fileEntryOffset;

                for (int i = 0; i < fileCount; i++)
                {
                    uint offset = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();

                    pointers.Add(new ArchivePointer(offset + firstFileOffset, size));
                }

                for (int i = 0; i < fileCount; i++)
                {
                    var ptr = pointers[i];

                    var header = file.Slice((int)ptr.Offset, 4);
                    var data = file.Slice((int)ptr.Offset, (int)ptr.Size);

                    var fileFormat = "bin";
                    if (header.ToArray().Where(c => c != 0).All(c => char.IsAsciiLetterOrDigit((char)c)))
                    {
                        fileFormat = new string(header.ToArray().Where(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                    }

                    entries.Add(new ArchiveEntry(fileFormat, data.ToArray()));
                }
            }

            return entries;
        }

        private readonly record struct ArchivePointer(uint Offset, uint Size);
    }

    public record ArchiveEntry(string EntryType, byte[] Value);
}