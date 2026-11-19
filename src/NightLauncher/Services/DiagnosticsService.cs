using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace NightLauncher.Services;

public sealed record DiagnosticsExportResult(string ArchivePath, int LogFilesIncluded, long UncompressedLogBytes);

public sealed class DiagnosticsService
{
    private const long MaximumLogFileBytes = 2 * 1024 * 1024;
    private const long MaximumTotalLogBytes = 8 * 1024 * 1024;
    private static readonly string[] AllowedLogNames =
    [
        "latest.log",
        "latest.1.log",
        "latest.2.log",
        "latest.3.log"
    ];

    public DiagnosticsService(string? logDirectory = null)
    {
        LogDirectory = Path.GetFullPath(logDirectory ?? NightLauncher.App.LogDirectory);
    }

    public string LogDirectory { get; }

    public DiagnosticsExportResult Export(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException("Choose an absolute path for the diagnostics ZIP.", nameof(destinationPath));
        }

        var archivePath = Path.GetFullPath(destinationPath);
        if (!string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The diagnostics export must use a .zip file extension.", nameof(destinationPath));
        }

        var logRoot = Path.TrimEndingDirectorySeparator(LogDirectory) + Path.DirectorySeparatorChar;
        if (archivePath.StartsWith(logRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Save the diagnostics ZIP outside the log folder.", nameof(destinationPath));
        }

        var parent = Path.GetDirectoryName(archivePath)
            ?? throw new InvalidOperationException("The diagnostics archive has no parent folder.");
        Directory.CreateDirectory(parent);

        var temporaryPath = archivePath + $".{Guid.NewGuid():N}.tmp";
        var includedFiles = new List<string>();
        long totalBytes = 0;
        try
        {
            using (var temporaryStream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(temporaryStream, ZipArchiveMode.Create))
            {
                foreach (var fileName in AllowedLogNames)
                {
                    var sourcePath = Path.Combine(LogDirectory, fileName);
                    if (!File.Exists(sourcePath))
                    {
                        continue;
                    }

                    var attributes = File.GetAttributes(sourcePath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    using var input = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    var bytesToCopy = input.Length;
                    if (bytesToCopy > MaximumLogFileBytes)
                    {
                        throw new InvalidDataException($"The log file {fileName} exceeds the 2 MB export limit.");
                    }

                    totalBytes = checked(totalBytes + bytesToCopy);
                    if (totalBytes > MaximumTotalLogBytes)
                    {
                        throw new InvalidDataException("The combined log files exceed the 8 MB export limit.");
                    }

                    var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
                    using (var output = entry.Open())
                    {
                        var buffer = new byte[81920];
                        var remaining = bytesToCopy;
                        while (remaining > 0)
                        {
                            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read == 0)
                            {
                                throw new EndOfStreamException($"The log file {fileName} changed while it was being exported.");
                            }

                            output.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }

                    includedFiles.Add(fileName);
                }

                var summaryEntry = archive.CreateEntry("diagnostics-summary.txt", CompressionLevel.Optimal);
                using var writer = new StreamWriter(summaryEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine("NightLauncher diagnostics summary");
                writer.WriteLine($"NightLauncher version: {typeof(DiagnosticsService).Assembly.GetName().Version?.ToString() ?? "unknown"}");
                writer.WriteLine($"Operating system: {Environment.OSVersion.VersionString}");
                writer.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
                writer.WriteLine($"Generated UTC: {DateTimeOffset.UtcNow:O}");
                writer.WriteLine($"Log files included: {includedFiles.Count}");
                foreach (var name in includedFiles)
                {
                    writer.WriteLine($"- {name}");
                }
            }

            File.Move(temporaryPath, archivePath, overwrite: true);
            return new DiagnosticsExportResult(archivePath, includedFiles.Count, totalBytes);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A stale temporary archive can be safely removed manually.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original export error.
                }
            }
        }
    }
}
