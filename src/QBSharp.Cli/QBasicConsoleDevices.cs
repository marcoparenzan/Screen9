using QBSharp.QBasic;
using Screen9;

namespace QBSharp.Cli;

internal static class QBasicConsoleDevices
{
    public static string ReadKey()=>!Console.IsInputRedirected&&Console.KeyAvailable?PCKeyboard.Encode(Console.ReadKey(true)):"";
    /// <summary>QBS_RASTER_FONT=compact forces the 5x7 fallback; anything else (default) uses IBM OEM fonts when found.</summary>
    public static QBasicRasterFont ConfigureRasterFont(string? mode)=>string.Equals(mode?.Trim(),"compact",StringComparison.OrdinalIgnoreCase)?QBasicRasterFont.Compact:QBasicRasterFont.Auto;
    public static Func<PCTone,CancellationToken,ValueTask>? ConfigureAudio(string? mode,bool interactive)
        =>mode=="mute"?PCSpeaker.Silent:mode=="1"&&interactive?PCSpeaker.Beep:null;
    /// <summary>Editor: the speaker is on by default (Windows), QBS_QBASIC_AUDIO=mute keeps durations in silence.</summary>
    public static Func<PCTone,CancellationToken,ValueTask> ConfigureEditorAudio(string? mode)
        =>mode=="mute"||!OperatingSystem.IsWindows()?PCSpeaker.Silent:PCSpeaker.Beep;
}
