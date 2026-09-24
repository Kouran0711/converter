namespace NithConverter.Core.Models;

public static class OutputNameRules
{
    public static string? GetError(string? name)
    {
        // Null preserves the default filename for callers that do not request a rename.
        if (name is null) return null;
        if (string.IsNullOrWhiteSpace(name)) return "Digite um nome para o arquivo de saída.";
        if (name.Length > 240) return "Use um nome com até 240 caracteres.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "O nome não pode conter caracteres como \\ / : * ? \" < > |.";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "O nome não pode terminar com ponto ou espaço.";
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT"))
                && "123456789¹²³".Contains(stem[3])))
            return "Esse nome é reservado pelo Windows. Escolha outro nome.";
        return null;
    }
}
