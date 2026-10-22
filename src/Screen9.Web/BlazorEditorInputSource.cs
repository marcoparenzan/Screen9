using System.Collections.Concurrent;
using QBSharp.Cli.Editor;

namespace Screen9.Web;

/// <summary>IEditorInputSource fed by browser keydown events instead of ConPTY/VT bytes — the page's keydown
/// handler calls <see cref="Enqueue"/>, EditorApplication's own loop calls <see cref="Poll"/>.</summary>
public sealed class BlazorEditorInputSource : IEditorInputSource
{
    private readonly ConcurrentQueue<EditorInput> queue = new();

    public void Enqueue(EditorInput input)
    {
        if (queue.Count < 1024) queue.Enqueue(input);
    }

    public EditorInput? Poll() => queue.TryDequeue(out var input) ? input : null;

    public void Dispose() { }
}
