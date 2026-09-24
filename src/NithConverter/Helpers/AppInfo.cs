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

    public static Version Version => Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0));
    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Math.Max(0, Version.Build)}";

    private static Version Normalize(Version value) => new(
        Math.Max(0, value.Major), Math.Max(0, value.Minor), Math.Max(0, value.Build), Math.Max(0, value.Revision));
}
