namespace Screen9.Web;

/// <summary>A browser keydown event, exactly the shape <c>wwwroot/js/keyCapture.js</c> posts to .NET via
/// <c>DotNetObjectReference</c>. <see cref="Code"/> is the physical key (layout-independent, e.g. "ArrowUp",
/// "F5"); <see cref="Key"/> is the character it produces (layout- and Shift-aware, e.g. "a", "A", "Enter").</summary>
public sealed record BrowserKeyEvent(string Key, string Code, bool CtrlKey, bool AltKey, bool ShiftKey);
