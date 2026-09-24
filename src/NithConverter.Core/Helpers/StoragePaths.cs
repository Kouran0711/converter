namespace NithConverter.Core.Helpers;

public static class StoragePaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NITH Converter");
}
