namespace Linq2Dashboard.Blazor;

/// <summary>Joins the library's classes with a host's, and picks the default input skin when the host gives none (design §9).</summary>
internal static class CssClasses
{
    /// <summary>The library's default look for a text, search or number input. Rendered only when the host passes no <c>InputClass</c>.</summary>
    public const string DefaultInput = "l2d-input";

    /// <summary>The library classes followed by the host's, or the library classes alone when the host gives none.</summary>
    public static string Join(string libraryClasses, string? hostClasses) =>
        string.IsNullOrWhiteSpace(hostClasses) ? libraryClasses : $"{libraryClasses} {hostClasses.Trim()}";

    /// <summary>An input's classes: the stable hook, then either the host's classes or the default skin.</summary>
    public static string Input(string hook, string? hostClasses) =>
        Join(hook, string.IsNullOrWhiteSpace(hostClasses) ? DefaultInput : hostClasses);
}
