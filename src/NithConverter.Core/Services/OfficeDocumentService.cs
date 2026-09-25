using NithConverter.Core.Helpers;

namespace NithConverter.Core.Services;

/// <summary>
/// Converts Word/Excel/PowerPoint/OpenDocument files to PDF through LibreOffice in headless mode.
/// A private per-job profile avoids conflicts with an interactive LibreOffice session.
/// </summary>
public sealed class OfficeDocumentService
{
    public async Task<ProcessExecutionResult> ConvertToPdfAsync(string sofficePath, string inputPath,
        string outputPdfPath, string workingDirectory, CancellationToken cancellationToken)
    {
        string outputDirectory = Path.GetDirectoryName(outputPdfPath)!;
        Directory.CreateDirectory(outputDirectory);
        string profile = Path.Combine(workingDirectory, "libreoffice-profile");
        Directory.CreateDirectory(profile);
        string profileUri = new Uri(Path.GetFullPath(profile) + Path.DirectorySeparatorChar).AbsoluteUri;

        var arguments = new[]
        {
            "--headless", "--invisible", "--nologo", "--nodefault", "--norestore", "--nolockcheck",
            "--nofirststartwizard", $"-env:UserInstallation={profileUri}",
            "--convert-to", "pdf", "--outdir", outputDirectory, inputPath
        };

        ProcessExecutionResult result = await ProcessHelper.RunAsync(
            sofficePath, arguments, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result.ExitCode != 0)
            return result;

        string expected = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".pdf");
        string? generated = File.Exists(expected)
            ? expected
            : Directory.EnumerateFiles(outputDirectory, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        if (generated is null || new FileInfo(generated).Length == 0)
            return new(1, result.StandardOutput,
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? "LibreOffice terminou sem produzir o PDF intermediário."
                    : result.StandardError);

        if (!Path.GetFullPath(generated).Equals(Path.GetFullPath(outputPdfPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(generated, outputPdfPath, overwrite: true);
        }
        return result;
    }
}
