using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using QBSharp.TerminalHarness;

if (!OperatingSystem.IsWindows() || args.Length == 0) return 2;
while(args.Length>0 && args[0] is "--disable-ai" or "--mute-qbasic-audio" or "--terminal-graphics")
{
    if(args[0]=="--disable-ai")Environment.SetEnvironmentVariable("QBS_AI_PROVIDER", "");
    else if(args[0]=="--terminal-graphics")Environment.SetEnvironmentVariable("QBS_GRAPHICS","terminal");
    else Environment.SetEnvironmentVariable("QBS_QBASIC_AUDIO","mute");
    args=args[1..];
}
if (args.Length == 0) return 2;
// Test host only. ConPTY children can inherit the test runner's redirected standard
// handles. Reopen the attached console with READ|WRITE before .NET Console caches
// handle capabilities. cmd >CONOUT$ opens write-only: GetConsoleMode then fails.
var input = Native.Open("CONIN$");
var output = Native.Open("CONOUT$");
try
{
    Native.Set(-10, input); Native.Set(-11, output); Native.Set(-12, output);
    string cli = Path.GetFullPath(args[0]);
    var resolver = new AssemblyDependencyResolver(cli);
    AssemblyLoadContext.Default.Resolving += (_, name) => resolver.ResolveAssemblyToPath(name) is { } path ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
    var entry = AssemblyLoadContext.Default.LoadFromAssemblyPath(cli).EntryPoint ?? throw new InvalidOperationException("CLI has no entry point.");
    var result = entry.Invoke(null, [args[1..]]);
    return result is Task<int> task ? await task : result is int code ? code : 0;
}
catch (TargetInvocationException error) { throw error.InnerException ?? error; }
finally { Native.CloseHandle(input); Native.CloseHandle(output); }

namespace QBSharp.TerminalHarness
{
    public sealed class HarnessMarker;
    internal static class Native
    {
        public static IntPtr Open(string name)
        {
            var handle = CreateFileW(name, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return handle;
        }
        public static void Set(int n, IntPtr handle) { if (!SetStdHandle(n, handle)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetStdHandle(int n, IntPtr handle);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    }
}
