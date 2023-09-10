using System.Text;

namespace Rf3Archiver
{
    public class TextSectionWriter
    {
        private readonly record struct TextEntry(int AbsoluteOffset, int Length, byte[] Data);

        private readonly string filePath;
        private readonly List<string> text;

        public TextSectionWriter(string filePath, List<string> text)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException($"'{nameof(filePath)}' cannot be null or whitespace.", nameof(filePath));
            }

            this.filePath = filePath;
            this.text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public void Serialize()
        {
            using var file = File.Open(filePath, FileMode.Truncate, FileAccess.Write);
            file.SetLength(0);
            SerializeToBytes(file, text);
        }

        public static void SerializeToBytes(Stream stream, List<string> textTable)
        {
            var encoded = textTable.Select(text => EncodeString(text)).ToList();
            var entryTable = new List<TextEntry>();

            var runningOffset = 4 /* TEXT magic */ + 4 /* Entry count */ + encoded.Count * 8 /* Entry table size */;
            for (int i = 0; i < encoded.Count; i++)
            {
                var text = encoded[i];
                entryTable.Add(new TextEntry(runningOffset, text.Length, text));
                runningOffset += text.Length + 1; // Account for null byte.
            }

            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)'T');
                writer.Write((byte)'E');
                writer.Write((byte)'X');
                writer.Write((byte)'T');

                writer.Write((uint)entryTable.Count);

                foreach (var entry in entryTable)
                {
                    writer.Write((uint)entry.Length);
                    writer.Write((uint)entry.AbsoluteOffset);
                }

                foreach (var entry in entryTable)
                {
                    writer.Write(entry.Data);
                    writer.Write((byte)0); // Null-terminated string.
                }
            }
        }

        private static byte[] EncodeString(string text) => Encoding.UTF8.GetBytes(text);
    }
}