namespace Screen9.Web;

/// <summary>A page in the site menu. <paramref name="Aliases"/> are other routes of the same page (an old URL).</summary>
public sealed record NavItem(string Title, string Path, string? Note = null, params string[] Aliases)
{
    public bool Matches(string path) => Same(Path, path) || Aliases.Any(a => Same(a, path));
    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>A group of pages: one drop-down in the header and, on any of its pages, the side navigation.</summary>
public sealed record NavSection(string Title, IReadOnlyList<NavItem> Items);

/// <summary>The one description of the site's menu, read by the header drop-downs and by the section navigation
/// shown beside every page (the current page highlighted).</summary>
public static class SiteNavigation
{
    public static IReadOnlyList<NavSection> Sections { get; } =
    [
        new("Screen9",
        [
            new("Home", "/"),
            new("Mode 9 history", "/history", "From QBasic's SCREEN 9 to the engine and the web"),
            new("Library", "/library", "Coding Guide", "/libreria"),
        ]),
        new("Gorilla Scripted",
        [
            new("Gorillas — QBasic (.bas)", "/gorillas-bas"),
            new("Gorillas — Python (PySharp)", "/gorillas-python"),
            new("Gorillas — C# script (.csx)", "/gorillas-csx"),
            new("QBasic Editor", "/editor"),
            new("Monaco Editor", "/editor-monaco", "Runs .bas, .py and .csx"),
        ]),
        new("C# Games",
        [
            new("Gorillas", "/gorillas-csharp"),
            new("Rally-X", "/rally-x", "Work in progress"),
        ]),
    ];

    /// <summary>The section a page belongs to and the page itself, or null for pages outside the menu (errors, 404).</summary>
    public static (NavSection Section, NavItem Item)? Find(string path)
    {
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];
        foreach (var section in Sections)
            foreach (var item in section.Items)
                if (item.Matches(path)) return (section, item);
        return null;
    }
}
