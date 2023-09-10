using System.IO;
using System.CommandLine;

using Newtonsoft.Json;

using Rf3Archiver;

var inputArchive = new Option<FileInfo>(name: "--in-archive", description: "The archive file to read.") { IsRequired = true };

var outputArchive = new Option<FileInfo>(name: "--out-archive", description: "The archive file to write to.") { IsRequired = true };

var outputDirectory = new Option<DirectoryInfo>("--out-directory", description: "Directory to dump archived files to. This directory should not exist, as it will be created.") { IsRequired = true };

var inputDirectory = new Option<DirectoryInfo>("--in-directory", description: "Directory to archive files from. Note that this will archive _all_ files with a numeric name in the directory in order. Ensure that you have all the files you need in the directory in the correct order.") { IsRequired = true };

var inText = new Option<FileInfo>("--in-text-section", description: "The JSON file containing the text section to write.") { IsRequired = true };
var outText = new Option<FileInfo>("--out-text-section", description: "Destination to write text section as JSON to.") { IsRequired = true };

var inSection = new Option<FileInfo>("--in-section", description: "The section file to insert verbatim.") { IsRequired = true };
var outSection = new Option<FileInfo>("--out-section", description: "Destination to write the section binary to.") { IsRequired = true };

var sectionId = new Option<int>("--section-id", description: "The section id in the archive to work on (zero indexed).") { IsRequired = true };

var overwrite = new Option<bool>("--overwrite", "Whether to overwrite existing files.");

var test = new Command("test", "round-trips an archive file, and validates that what we output's byte-for-byte identical.")
{
    inputArchive
};
test.SetHandler(async (context) => {
    var file = context.ParseResult.GetValueForOption(inputArchive);

    var arcReader = new ArchiveReader(file!.FullName);
    var archiveFiles = arcReader.Read();

    var textTable = TextSectionReader.ReadBytes(archiveFiles[28].Value);

    byte[] textTableBytes;
    using (var stream = new MemoryStream())
    {
        TextSectionWriter.SerializeToBytes(stream, textTable);
        textTableBytes = stream.ToArray();
    }

    archiveFiles[28] = new ArchiveEntry("TEXT", textTableBytes);

    byte[] actual;
    using (var stream = new MemoryStream())
    {
        ArchiveWriter.SerializeToBytes(stream, archiveFiles);
        actual = stream.ToArray();
    }

    byte[] expected = await File.ReadAllBytesAsync(file.FullName);

    for (int i = 0; i < Math.Min(actual.Length, expected.Length); i++)
    {
        if (expected[i] != actual[i])
        {
            await File.WriteAllBytesAsync("./expected.bin", expected);
            await File.WriteAllBytesAsync("./actual.bin", actual);
            throw new Exception($"Actual differs from expected @ 0x{i:X}: E: 0x{expected[i]:X} != A: 0x{actual[i]:X}");
        }
    }

    if (actual.Length != expected.Length)
    {
        throw new Exception($"Expected length and actual length differ: Expected = {expected.Length}b, Actual = {actual.Length}b");
    }

    Console.WriteLine("It worked! :3");
});

// Dump all sections to a directory
var extract = new Command("extract", "Dump all sections from a file into a directory.") { inputArchive, outputDirectory };
extract.SetHandler(async (context) => {
    var inArc = context.ParseResult.GetValueForOption(inputArchive)!;
    var outDir = context.ParseResult.GetValueForOption(outputDirectory)!;

    if (outDir.Exists) {
        throw new ArgumentException("Output directory already exists.");
    }

    outDir.Create();

    var archive = new ArchiveReader(inArc.FullName);

    var entries = archive.Read();
    for (int i = 0; i < entries.Count; i++) 
    {
        await File.WriteAllBytesAsync(Path.Combine(outDir.FullName, $"{i}.{entries[i].EntryType}"), entries[i].Value);
    }
});

// Roll up a whole directory into an archive file.
var archive = new Command("archive", "Packages all files in a directory into a Rune Factory 3 .arc file.") { inputDirectory, outputArchive, overwrite };
archive.SetHandler(async (context) => {
    var inDir = context.ParseResult.GetValueForOption(inputDirectory)!;
    var outArc = context.ParseResult.GetValueForOption(outputArchive)!;
    var shouldOverwrite = context.ParseResult.GetValueForOption(overwrite);

    if (!shouldOverwrite && outArc.Exists) 
    {
        throw new ArgumentException("Destination archive already exists.");
    }

    var files = inDir.GetFiles()
                       .OrderBy(f => f.FullName)
                       .Where(f => int.TryParse(f.Name.Split('.')[0], out _))
                       .Select(f => File.ReadAllBytes(f.FullName))
                       .Select(f => new ArchiveEntry("na", f))
                       .ToList();

    ArchiveWriter writer = new(outArc.FullName, files);
    writer.Serialize();

    await Task.CompletedTask;
});

// Dump a specific section
var dumpSection = new Command("dump-section", "Dump a section from a .arc file to a file.") { inputArchive, outSection, sectionId, overwrite };
dumpSection.SetHandler(async (context) => {
    var inArc = context.ParseResult.GetValueForOption(inputArchive)!;
    var outSec = context.ParseResult.GetValueForOption(outSection)!;
    var id = context.ParseResult.GetValueForOption(sectionId);
    var shouldOverwrite = context.ParseResult.GetValueForOption(overwrite);

    if (!shouldOverwrite && outSec.Exists) 
    {
        throw new ArgumentException("Destination section file already exists.");
    }

    ArchiveReader reader = new(inArc.FullName);

    var allFiles = reader.Read();

    await File.WriteAllBytesAsync(outSec.FullName, allFiles[id].Value);
});

// Replace a section in place from a binary file
var replaceSection = new Command("replace-section", "Replaces a section in an archive file in place.") { inputArchive, outputArchive, inSection, overwrite, sectionId };
replaceSection.SetHandler(async (context) => {
    var inArc = context.ParseResult.GetValueForOption(inputArchive)!;
    var outArc = context.ParseResult.GetValueForOption(outputArchive)!;
    var inSec = context.ParseResult.GetValueForOption(inSection)!;
    var id = context.ParseResult.GetValueForOption(sectionId);
    var shouldOverwrite = context.ParseResult.GetValueForOption(overwrite);

    if (!shouldOverwrite && outArc.Exists) 
    {
        throw new ArgumentException("Destination archive file already exists.");
    }

    var reader = new ArchiveReader(inArc.FullName);
    var archive = reader.Read();
    var section = await File.ReadAllBytesAsync(inSec.FullName);

    archive[id] = new ArchiveEntry(archive[id].EntryType, section);

    var writer = new ArchiveWriter(outArc.FullName, archive);
    writer.Serialize();
});

// Extract a text section file to JSON
var extractTextFile = new Command("extract-text-file", "Extracts a .TEXT file into human readable JSON.") { inSection, outText, overwrite, sectionId };
extractTextFile.SetHandler(async (context) => {
    var inSec = context.ParseResult.GetValueForOption(inSection)!;
    var text = context.ParseResult.GetValueForOption(outText)!;
    var id = context.ParseResult.GetValueForOption(sectionId);
    var shouldOverwrite = context.ParseResult.GetValueForOption(overwrite);

    if (!shouldOverwrite && text.Exists) 
    {
        throw new ArgumentException("Destination JSON file already exists.");
    }

    var reader = new TextSectionReader(inSec.FullName);
    var textTable = reader.Read();

    var serialized = JsonConvert.SerializeObject(textTable, Formatting.Indented);

    await File.WriteAllTextAsync(text.FullName, serialized);
});

var replaceTextSection = new Command("replace-text-section", "Imports a TEXT section in place into an archive file.") { inputArchive, outputArchive, inText, sectionId, overwrite };
replaceTextSection.SetHandler(async (context) => {
    var inArc = context.ParseResult.GetValueForOption(inputArchive)!;
    var outArc = context.ParseResult.GetValueForOption(outputArchive)!;
    var text = context.ParseResult.GetValueForOption(inText)!;
    var id = context.ParseResult.GetValueForOption(sectionId);
    var shouldOverwrite = context.ParseResult.GetValueForOption(overwrite);

    if (!shouldOverwrite && outArc.Exists) 
    {
        throw new ArgumentException("Destination archive file already exists.");
    }

    var reader = new ArchiveReader(inArc.FullName);
    var sections = reader.Read();

    var section = sections[id];

    var json = await File.ReadAllTextAsync(text.FullName);
    var textTable = JsonConvert.DeserializeObject<List<string>>(json);

    using (var stream = new MemoryStream())
    {
        TextSectionWriter.SerializeToBytes(stream, textTable!);
        sections[id] = new ArchiveEntry("TEXT", stream.ToArray());
    }

    var writer = new ArchiveWriter(outArc.FullName, sections);
    writer.Serialize();

    await Task.CompletedTask;
});

// Extract a text section in the archive to JSON
var extractTextSection = new Command("extract-text-section", "Extracts a TEXT section into human readable JSON.") { inputArchive, outText, overwrite, sectionId };
extractTextSection.SetHandler(async (context) => {
    var inArc = context.ParseResult.GetValueForOption(inputArchive)!;
    var text = context.ParseResult.GetValueForOption(outText)!;
    var id = context.ParseResult.GetValueForOption(sectionId);
    var shouldOverwrite = context.ParseResult.GetValueForOption(overwrite);

    if (!shouldOverwrite && text.Exists) 
    {
        throw new ArgumentException("Destination JSON file already exists.");
    }

    var reader = new ArchiveReader(inArc.FullName);
    var archive = reader.Read();

    var textTable = TextSectionReader.ReadBytes(archive[id].Value);

    var serialized = JsonConvert.SerializeObject(textTable, Formatting.Indented);
    
    await File.WriteAllTextAsync(text.FullName, serialized);
});

var rootCommand = new RootCommand("Rune Factory 3 Nintendo DS / Special Edition archiver tool.") { 
    test, 
    extract, 
    archive, 
    dumpSection, 
    replaceSection, 
    extractTextFile, 
    extractTextSection, 
    replaceTextSection 
};

return await rootCommand.InvokeAsync(args);
