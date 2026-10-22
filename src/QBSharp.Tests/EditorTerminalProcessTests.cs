using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using QBSharp.Cli;

namespace QBSharp.Tests;

/// <summary>Real Windows ConPTY: full-screen CLI input/render/save/run/mouse/exit, not redirected stdin.</summary>
public sealed class EditorTerminalProcessTests
{
    [Fact]
    public async Task WindowsPseudoTerminalCanEditSaveRunAndNavigateMouseMenu()
    {
        if (!OperatingSystem.IsWindows()) return; // ConPTY is Windows-only; logical/VT tests run everywhere.
        string root = Path.Combine(Path.GetTempPath(), "qbs-conpty-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string file = Path.Combine(root, "job.bas");
            string cli = Environment.GetEnvironmentVariable("QBS_EDITOR_CLI_PATH") ?? typeof(CliApplication).Assembly.Location;
            string harness = typeof(QBSharp.TerminalHarness.HarnessMarker).Assembly.Location;
            using var terminal = new PseudoTerminal($"dotnet \"{harness}\" --disable-ai --mute-qbasic-audio \"{cli}\" edit \"{file}\"", root);
            await terminal.Until(() => terminal.Screen.Contains("QBSharp", StringComparison.Ordinal));
            // SGR left click Help on top bar and its first item; conhost translates into mouse INPUT_RECORDs.
            terminal.Send("\u001b[<0;38;1M\u001b[<0;38;1m");
            await terminal.Until(() => terminal.Screen.Contains("Keyboard help", StringComparison.Ordinal));
            terminal.Send("\u001b[<0;38;2M\u001b[<0;38;2m");
            await terminal.Until(() => terminal.Screen.Contains("Keyboard / mouse", StringComparison.Ordinal));
            terminal.Send("\u001b[27;0;27;1;0;1_\u001b[27;0;27;0;0;1_"); await Task.Delay(200);
            terminal.Send("Print(42)"); terminal.Send("\u001bOQ"); // F2
            await terminal.Until(() => File.Exists(file) && SavedTextMatches(file,"Print(42)"));
            Assert.Equal("Print(42)", await File.ReadAllTextAsync(file));
            terminal.Send("\u001b[15~"); // F5
            await terminal.Until(() => terminal.Screen.Contains("Completed", StringComparison.Ordinal));
                        terminal.Send("\u001b[18~"); // F7: no credentials/network in this test process.
            await terminal.Until(() => terminal.Screen.Contains("RalfAI disclosure", StringComparison.Ordinal));
            terminal.Send("\u001b[B\r"); // Continue after disclosure.
            await terminal.Until(() => terminal.Screen.Contains("Ralf instruction", StringComparison.Ordinal));
            terminal.Send("change code\r");
            await terminal.Until(() => terminal.Screen.Contains("RalfAI disabled", StringComparison.Ordinal));
            Assert.Equal("Print(42)", await File.ReadAllTextAsync(file)); // No generated/applied/saved code.
            terminal.Send("\u0011"); // Ctrl+Q
            await terminal.Until(() => terminal.Exited);
            Assert.Equal(0u, terminal.ExitCode);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task WindowsPseudoTerminalRendersRealConsoleLibCanvas()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "qbs-native-canvas-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string fixture = Path.Combine(root, "canvas.bas");
            await File.WriteAllTextAsync(fixture, "Dim c = Screen.Create(24,12)\nScreen.Text(c,2,1,\"NATIVE_CANVAS\")\nAssertEqual(100, Screen.Viewport().Width)\nScreen.Render(c,0,8)\nPrint(\"CANVAS_OK\")");
            string cli = Environment.GetEnvironmentVariable("QBS_EDITOR_CLI_PATH") ?? typeof(CliApplication).Assembly.Location;
            string harness = typeof(QBSharp.TerminalHarness.HarnessMarker).Assembly.Location;
            using var terminal = new PseudoTerminal($"dotnet \"{harness}\" --disable-ai --mute-qbasic-audio \"{cli}\" test \"{fixture}\"", root);
            await terminal.Until(() => terminal.Exited); await Task.Delay(300);
            Assert.Equal(0u, terminal.ExitCode); Assert.Contains("CANVAS_OK", terminal.Output); Assert.Contains("NATIVE_CANVAS", terminal.Output);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task QBasicNativeWaitAcceptsKeyAndRestoresBeforeError()
    {
        if(!OperatingSystem.IsWindows())return;
        string root=Path.Combine(Path.GetTempPath(),"qbs-native-poll-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(root,"wait.bas");
            await File.WriteAllTextAsync(file,"SCREEN 0\nPRINT \"QBS_WAIT\";\nDO\nk$=INKEY$\nLOOP UNTIL k$=\"x\"\na=1/0");
            string harness=typeof(QBSharp.TerminalHarness.HarnessMarker).Assembly.Location;
            string cli=typeof(CliApplication).Assembly.Location;
            using var terminal=new PseudoTerminal($"dotnet \"{harness}\" --disable-ai --mute-qbasic-audio \"{cli}\" run \"{file}\" --dialect qbasic",root);
            await terminal.Until(()=>terminal.Output.Contains("QBS_WAIT",StringComparison.Ordinal));
            await Task.Delay(750);Assert.False(terminal.Exited);terminal.Send("x");
            await terminal.Until(()=>terminal.Exited);await Task.Delay(200);Assert.Equal(4u,terminal.ExitCode);
            int restore=terminal.Output.IndexOf("\x1b[?1049l",StringComparison.Ordinal);
            Assert.True(restore>=0);Assert.True(terminal.Output.IndexOf("QBS2400",StringComparison.Ordinal)>restore);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task OriginalGorillasIntroWaitAcceptsKeyWithoutExhaustingInteractiveBudget()
    {
        if(!OperatingSystem.IsWindows())return;
        string root=Path.Combine(Path.GetTempPath(),"qbs-gorillas-input-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root,"Gorillas.ini"),"UseSound=No\nShowIntro=Yes\n");
            string file=Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS");
            string harness=typeof(QBSharp.TerminalHarness.HarnessMarker).Assembly.Location;
            string cli=typeof(CliApplication).Assembly.Location;
            using var terminal=new PseudoTerminal($"dotnet \"{harness}\" --disable-ai --mute-qbasic-audio \"{cli}\" run \"{file}\" --dialect qbasic",root);
            await terminal.Until(()=>terminal.Output.Contains("Press any key to continue",StringComparison.Ordinal));
            await Task.Delay(750);Assert.True(!terminal.Exited,"Exited "+terminal.ExitCode+" / "+terminal.Output[^Math.Min(2000,terminal.Output.Length)..]);terminal.Send(" ");
            await terminal.Until(()=>terminal.Output.Contains("Name of Player 1",StringComparison.Ordinal));
            Assert.False(terminal.Exited);terminal.Send("\u0003");
            await terminal.Until(()=>terminal.Exited);await Task.Delay(200);Assert.True(terminal.ExitCode==130u,"Exit "+terminal.ExitCode+" / "+terminal.Output[^Math.Min(2000,terminal.Output.Length)..]);
            Assert.DoesNotContain("QBS2401",terminal.Output,StringComparison.Ordinal);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task EditorF5RunsOriginalGorillasWithQBasicInputAndF6Stop()
    {
        if(!OperatingSystem.IsWindows())return;
        string root=Path.Combine(Path.GetTempPath(),"qbs-editor-gorilla-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string file=Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS");string before=await File.ReadAllTextAsync(file);
            string harness=typeof(QBSharp.TerminalHarness.HarnessMarker).Assembly.Location;string cli=typeof(CliApplication).Assembly.Location;
            using var terminal=new PseudoTerminal($"dotnet \"{harness}\" --disable-ai --mute-qbasic-audio --terminal-graphics \"{cli}\" edit \"{file}\" --dialect qbasic",root);
            await terminal.Until(()=>terminal.Screen.Contains("qbasic",StringComparison.Ordinal));terminal.Send("\x1b[15~");
            await terminal.Until(()=>terminal.Screen.Contains("Press any key to continue",StringComparison.Ordinal));terminal.Send(" ");
            await terminal.Until(()=>terminal.Screen.Contains("Name of Player 1",StringComparison.Ordinal));terminal.Send("Alice\r");
            await terminal.Until(()=>terminal.Screen.Contains("Name of Player 2",StringComparison.Ordinal));terminal.Send("Bob\r");
            await terminal.Until(()=>terminal.Screen.Contains("Play to how many",StringComparison.Ordinal));terminal.Send("1\r");
            await terminal.Until(()=>terminal.Screen.Contains("Gravity in",StringComparison.Ordinal));terminal.Send("9.8\r");
            await terminal.Until(()=>terminal.Screen.Contains("Your Choice?",StringComparison.Ordinal));terminal.Send("P");
            await Task.Delay(500);Assert.False(terminal.Exited);
            terminal.Send("\x1b[17~");await terminal.Until(()=>terminal.Screen.Contains("Canceled",StringComparison.Ordinal));
            terminal.Send("\u0011");await terminal.Until(()=>terminal.Exited);Assert.Equal(0u,terminal.ExitCode);
            Assert.Equal(before,await File.ReadAllTextAsync(file));Assert.DoesNotContain("QBS1003",terminal.Output,StringComparison.Ordinal);
        }
        finally{Directory.Delete(root,true);}
    }
    private static bool SavedTextMatches(string path,string expected)
    {
        try{return File.ReadAllText(path)==expected;}
        catch(IOException error) when((error.HResult&0xffff) is 32 or 33){return false;} // Retry only transient sharing/lock violations within Until's deadline.
    }
    /// <summary>Minimal VT screen for ConPTY output: text, CR/LF/BS, CUP/CUU/CUD/CUF/CUB/CHA, ECH, EL, ED, DECSC/DECRC; SGR, modes and OSC ignored.</summary>
    private sealed class VtScreen(int width, int height)
    {
        private readonly char[][] rows = Enumerable.Range(0, height).Select(_ => Enumerable.Repeat(' ', width).ToArray()).ToArray();
        private int row, column, savedRow, savedColumn, state; // 0 text, 1 ESC, 2 CSI, 3 OSC
        private readonly StringBuilder parameters = new();
        public string Text => string.Join('\n', rows.Select(r => new string(r)));
        public void Apply(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
            {
                switch (state)
                {
                    case 1:
                        state = c == '[' ? 2 : c == ']' ? 3 : 0; parameters.Clear();
                        if (c == '7') (savedRow, savedColumn) = (row, column); else if (c == '8') (row, column) = (savedRow, savedColumn);
                        continue;
                    case 2:
                        if (c is >= '@' and <= '~') { Csi(c); state = 0; } else parameters.Append(c);
                        continue;
                    case 3:
                        if (c == '\a') state = 0; else if (c == '\x1b') state = 1;
                        continue;
                }
                if (c == '\x1b') state = 1;
                else if (c == '\r') column = 0;
                else if (c == '\n') LineFeed();
                else if (c == '\b') column = Math.Max(0, column - 1);
                else if (!char.IsControl(c)) { if (column >= width) { column = 0; LineFeed(); } rows[row][column++] = c; }
            }
        }
        private void LineFeed()
        {
            if (++row < height) return;
            row = height - 1; for (int y = 1; y < height; y++) rows[y - 1] = rows[y]; rows[height - 1] = Enumerable.Repeat(' ', width).ToArray();
        }
        private void Csi(char command)
        {
            string text = parameters.ToString(); if (text.StartsWith('?')) return;
            int[] n = text.Split(';').Select(p => int.TryParse(p, out int v) ? v : 0).ToArray();
            int First(int fallback) => n.Length > 0 && n[0] > 0 ? n[0] : fallback;
            switch (command)
            {
                case 'H' or 'f': row = Math.Clamp(First(1) - 1, 0, height - 1); column = Math.Clamp((n.Length > 1 && n[1] > 0 ? n[1] : 1) - 1, 0, width - 1); break;
                case 'A': row = Math.Max(0, row - First(1)); break;
                case 'B': row = Math.Min(height - 1, row + First(1)); break;
                case 'C': column = Math.Min(width - 1, column + First(1)); break;
                case 'D': column = Math.Max(0, column - First(1)); break;
                case 'G': column = Math.Clamp(First(1) - 1, 0, width - 1); break;
                case 'X': for (int x = column; x < Math.Min(width, column + First(1)); x++) rows[row][x] = ' '; break;
                case 'K': for (int x = n.Length > 0 && n[0] == 1 ? 0 : column; x < (n.Length > 0 && n[0] == 1 ? column + 1 : width); x++) rows[row][x] = ' '; break;
                case 'J': if (n.Length > 0 && n[0] == 2) foreach (var r in rows) Array.Fill(r, ' '); break;
            }
        }
    }
    private sealed class PseudoTerminal : IDisposable
    {
        private IntPtr pty, input, output, process;
        private readonly StringBuilder captured = new();
        private readonly Task pump;
        private readonly VtScreen screen = new(100, 30);
        public string Output { get { lock (captured) return captured.ToString(); } }
        /// <summary>Visible text of the emulated 100x30 screen (rows joined by newlines).</summary>
        public string Screen { get { lock (captured) return screen.Text; } }
        public bool Exited => WaitForSingleObject(process, 0) == 0;
        public uint ExitCode { get { GetExitCodeProcess(process, out uint value); return value; } }
        public PseudoTerminal(string command, string directory)
        {
            IntPtr inRead = IntPtr.Zero, outWrite = IntPtr.Zero, attribute = IntPtr.Zero;
            try
            {
                Check(CreatePipe(out inRead, out input, IntPtr.Zero, 0)); Check(CreatePipe(out output, out outWrite, IntPtr.Zero, 0));
                int result = CreatePseudoConsole(new Coord(100, 30), inRead, outWrite, 0, out pty);
                if (result != 0) Marshal.ThrowExceptionForHR(result);
                CloseHandle(inRead); inRead = IntPtr.Zero; CloseHandle(outWrite); outWrite = IntPtr.Zero;
                nuint size = 0; InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                attribute = Marshal.AllocHGlobal((nint)size); Check(InitializeProcThreadAttributeList(attribute, 1, 0, ref size));
                Check(UpdateProcThreadAttribute(attribute, 0, (IntPtr)0x00020016, pty, (nuint)IntPtr.Size, IntPtr.Zero, IntPtr.Zero));
                pump = Task.Run(() =>
                {
                    var bytes = new byte[4096]; var chars = new char[4096]; var decoder = Encoding.UTF8.GetDecoder();
                    while (ReadFile(output, bytes, (uint)bytes.Length, out uint count, IntPtr.Zero) && count > 0)
                    {
                        int length = decoder.GetChars(bytes, 0, (int)count, chars, 0);
                        lock (captured) { captured.Append(chars, 0, length); screen.Apply(chars.AsSpan(0, length)); if (captured.Length > 2_000_000) captured.Remove(0, 1_000_000); }
                    }
                });
                var startup = new StartupInfoEx { Info = new() { Size = Marshal.SizeOf<StartupInfoEx>() }, Attributes = attribute };
                Check(CreateProcessW(null, new StringBuilder(command), IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero, directory, ref startup, out var info));
                process = info.Process; CloseHandle(info.Thread);

            }
            catch { DisposeHandles(); throw; }
            finally
            {
                if (inRead != IntPtr.Zero) CloseHandle(inRead); if (outWrite != IntPtr.Zero) CloseHandle(outWrite);
                if (attribute != IntPtr.Zero) { DeleteProcThreadAttributeList(attribute); Marshal.FreeHGlobal(attribute); }
            }
        }
        public void Send(string text) { var bytes = Encoding.UTF8.GetBytes(text); Check(WriteFile(input, bytes, (uint)bytes.Length, out _, IntPtr.Zero)); }
        public async Task Until(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { while (!predicate()) { if (Exited) { await Task.Delay(300); throw new Xunit.Sdk.XunitException("Child exited before expected output: " + ExitCode + " / " + Output); } await Task.Delay(25, timeout.Token); } }
            catch (OperationCanceledException) { throw new Xunit.Sdk.XunitException("ConPTY timeout. Recent output: " + Output[^Math.Min(3000, Output.Length)..]); }
        }
        public void Dispose() { DisposeHandles(); pump.Wait(TimeSpan.FromSeconds(5)); }
        private void DisposeHandles()
        {
            if (process != IntPtr.Zero) { if (!Exited) { TerminateProcess(process, 99); WaitForSingleObject(process, 5000); } CloseHandle(process); process = IntPtr.Zero; }
            if (pty != IntPtr.Zero) { ClosePseudoConsole(pty); pty = IntPtr.Zero; }
            if (input != IntPtr.Zero) { CloseHandle(input); input = IntPtr.Zero; }
            if (output != IntPtr.Zero) { CloseHandle(output); output = IntPtr.Zero; }
        }
        private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    }
    [StructLayout(LayoutKind.Sequential)] private readonly struct Coord(short x, short y) { public readonly short X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public IntPtr Reserved, Desktop, Title; public uint X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public ushort Show, Reserved2; public IntPtr ReservedPointer, StdIn, StdOut, StdErr;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr security, uint size);
    [DllImport("kernel32.dll")] private static extern int CreatePseudoConsole(Coord size, IntPtr input, IntPtr output, uint flags, out IntPtr pty);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr pty);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, uint count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessW(string? application, StringBuilder command, IntPtr security, IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadFile(IntPtr handle, byte[] bytes, uint length, out uint count, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteFile(IntPtr handle, byte[] bytes, uint length, out uint count, IntPtr overlapped);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr handle, uint code);
}
