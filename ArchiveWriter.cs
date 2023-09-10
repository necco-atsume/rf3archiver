namespace Rf3Archiver
{
    public class ArchiveWriter
    {
        private readonly string filePath;
        private readonly List<ArchiveEntry> entries;

        public ArchiveWriter(string filePath, List<ArchiveEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException($"'{nameof(filePath)}' cannot be null or whitespace.", nameof(filePath));
            }

            this.filePath = filePath;
            this.entries = entries;
        }

        public void Serialize()
        {
            using var file = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            file.SetLength(0);

            SerializeToBytes(file, entries);
        }

        public static void SerializeToBytes(Stream stream, List<ArchiveEntry> entries)
        {
            int fileCount = entries.Count;

            using var writer = new BinaryWriter(stream);

            writer.Write((ushort)12); // File entry table start. 
            writer.Write((ushort)8); // File entry length (bytes).
            writer.Write((uint)0); // Padding bytes.
            writer.Write((uint)fileCount); // Number of entries.

            int currentOffset = 0;
            foreach (var entry in entries)
            {
                writer.Write(currentOffset);
                writer.Write(entry.Value.Length);
                currentOffset += entry.Value.Length;
            }

            foreach (var entry in entries)
            {
                writer.Write(entry.Value);
            }
        }

        private readonly record struct ArchiveMetadata(int Offset, int Length, byte[] Data);
    }
}