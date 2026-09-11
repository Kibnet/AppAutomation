namespace AppAutomation.Abstractions;

internal static class GridPropertyPath
{
    public static string Normalize(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        var segments = path.Split('.').Select(static segment => segment.Trim()).ToArray();
        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Grid property path must contain readable property names.", parameterName);
        }

        return string.Join('.', segments);
    }
}
