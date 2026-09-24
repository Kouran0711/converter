using System.Reflection;

namespace NithConverter.Helpers;

public static class AppInfo
{
    public const string ProductName = "NITH Converter";
    public const string Publisher = "Nith Digital";
    public const string Signature = "Nith Digital - nithdigital.com.br";
    public const string Website = "https://nithdigital.com.br";
    public const string GitHubOwner = "Kouran0711";
    public const string GitHubRepository = "converter";
    public const string ReleasesUrl = "https://github.com/Kouran0711/converter/releases";

    private static readonly Version ResolvedVersion = ResolveVersion();

    /// <summary>
    /// Versão real gravada no build. Dá preferência a InformationalVersion/FileVersion,
    /// porque são definidos explicitamente pelo workflow de release.
    /// </summary>
    public static Version Version => ResolvedVersion;
    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";

    private static Version ResolveVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (TryParseVersion(informational, out Version? parsed)) return parsed!;

        string? fileVersion = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (TryParseVersion(fileVersion, out parsed)) return parsed!;

        return Normalize(assembly.GetName().Version ?? new Version(1, 0, 0, 0));
    }

    private static bool TryParseVersion(string? value, out Version? version)
    {
        if (string.IsNullOrWhiteSpace(value)) { version = null; return false; }
        string clean = value.Trim().TrimStart('v', 'V');
        int suffix = clean.IndexOfAny(['-', '+']);
        if (suffix >= 0) clean = clean[..suffix];
        if (!System.Version.TryParse(clean, out Version? parsed)) { version = null; return false; }
        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version value) => new(
        Math.Max(0, value.Major), Math.Max(0, value.Minor), Math.Max(0, value.Build), Math.Max(0, value.Revision));
}
