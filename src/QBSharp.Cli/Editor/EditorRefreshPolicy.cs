namespace QBSharp.Cli.Editor;

public static class EditorRefreshPolicy
{
    public static bool ShouldDraw(bool programView,bool programFrameChanged,bool uiChanged,long elapsedMilliseconds)
        =>uiChanged || (programView ? programFrameChanged && elapsedMilliseconds>=33 : elapsedMilliseconds>=100);
}
