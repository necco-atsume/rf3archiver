using System.Text;

namespace Rf3Archiver
{
    public class TextSectionReader
    {
        private readonly string filePath;

        // HACK: Let's keep track of every character we don't know about yet, and make sure they're mapped correctly to something human readable.
        public static HashSet<char> UnmappedCharacters { get; } = new();

        public TextSectionReader(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException($"'{nameof(filePath)}' cannot be null or whitespace.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new ArgumentException($"File '{nameof(filePath)}' doesn't exist.", nameof(filePath));
            }

            this.filePath = filePath;
        }

        // Parses the file as a TEXT section, returning a string list.
        public List<string> Read()
        {
            byte[] data = File.ReadAllBytes(filePath);
            return ReadBytes(data);
        }

        // Parses a TEXT section, returning a string list.
        public static List<string> ReadBytes(byte[] data)
        {
            var mem = data.AsMemory();

            List<byte[]> textEntryBlobs = new();

            using var stream = new MemoryStream(data);
            using var reader = new BinaryReader(stream);

            // The magic header is four bytes, and should be "TEXT".
            byte[] magic = reader.ReadBytes(4); // "TEXT";
            if (magic.Length != 4 || magic[0] != (byte)'T' || magic[1] != (byte)'E' || magic[2] != (byte)'X' || magic[3] != (byte)'T')
            {
                throw new InvalidDataException("Expected 'TEXT' magic at beginning of text section.");
            }

            // u32: Number of text items in table.
            var count = reader.ReadUInt32();

            for (int i = 0; i < count; i++)
            {
                // For each text item, store its length and offset:
                // u32 Length in bytes of the UTF-8 string, not including its null terminator.
                // u32 Absolute offset in bytes, relative to the start of the file. 

                var len = reader.ReadInt32();
                var absoluteOffset = reader.ReadInt32();

                // Unsurprisingly, every text string is stored at *absoluteOffset, and is of length 'len'.
                // They're UTF-8 encoded, and even in the EN version of the script.
                // (Variable names are denoted by @var_name@ and are usually in Japanese.)
                var slice = mem.Slice(absoluteOffset, len);

                textEntryBlobs.Add(slice.ToArray());
            }

            // TODO: Ensure that a \0 follows, as a sanity check.
            return textEntryBlobs.Select(ParseUtf8String).ToList();
        }

        private static string ParseUtf8String(byte[] stringBytes)
        {
            // TODO: Map to the human-readable equivalent. 
            var result = Encoding.UTF8.GetString(stringBytes);

            foreach (char c in result)
            {
                if (c >= 128)
                {
                    UnmappedCharacters.Add(c);
                }
            }

            return result;
        }
    };
}