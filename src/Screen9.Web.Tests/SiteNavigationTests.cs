using System.Reflection;
using Microsoft.AspNetCore.Components;
using Screen9.Web;

namespace Screen9.Web.Tests;

public class SiteNavigationTests
{
    private static IEnumerable<string> Routes() => typeof(SiteNavigation).Assembly.GetTypes()
        .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
        .Select(r => r.Template);

    [Fact]
    public void EveryMenuEntryIsARealPage()
    {
        var routes = Routes().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in SiteNavigation.Sections.SelectMany(s => s.Items))
        {
            Assert.Contains(item.Path, routes);
            foreach (var alias in item.Aliases) Assert.Contains(alias, routes);
        }
    }

    [Fact]
    public void EveryPageIsInTheMenuExceptErrorsAndNotFound()
    {
        var inMenu = SiteNavigation.Sections.SelectMany(s => s.Items).SelectMany(i => i.Aliases.Prepend(i.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var route in Routes().Where(r => !r.Equals("/Error", StringComparison.OrdinalIgnoreCase) && !r.Equals("/not-found", StringComparison.OrdinalIgnoreCase)))
            Assert.True(inMenu.Contains(route), $"page {route} is not in the menu");
    }

    [Theory]
    [InlineData("/", "Screen9", "Home")]
    [InlineData("/library", "Screen9", "Library")]
    [InlineData("/libreria", "Screen9", "Library")]
    [InlineData("/history", "Screen9", "Mode 9 history")]
    [InlineData("/architecture", "Screen9", "Architecture")]
    [InlineData("/wasm", "Screen9", "WebAssembly and AOT")]
    [InlineData("/roadmap", "Screen9", "Roadmap")]
    [InlineData("/editor-monaco?file=gorillas.py", "Gorilla Scripted", "Monaco Editor")]
    [InlineData("/Rally-X/", "C# Games", "Rally-X")]
    [InlineData("/plasma-demo", "C# Demos", "Plasma (mode 13)")]
    [InlineData("/scene-demo", "C# Demos", "Scene (cell canvas)")]
    public void ThePageFindsItsSectionAndItself(string path, string section, string title)
    {
        var found = SiteNavigation.Find(path);
        Assert.NotNull(found);
        Assert.Equal((section, title), (found.Value.Section.Title, found.Value.Item.Title));
    }

    [Fact]
    public void UnknownPagesHaveNoSection() => Assert.Null(SiteNavigation.Find("/nowhere"));

    [Fact]
    public void PathsAreUniqueAcrossTheMenu()
    {
        var all = SiteNavigation.Sections.SelectMany(s => s.Items).SelectMany(i => i.Aliases.Prepend(i.Path)).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
