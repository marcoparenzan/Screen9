namespace Screen9;

/// <summary>Vista CPU della memoria video e sottoinsieme delle porte EGA/VGA usato dai programmi QBasic:
/// map mask (3C4/3C5 indice 2), read map select (3CE/3CF indice 4) e DAC VGA (3C6-3C9).
/// Nessun latch, set/reset, bit mask, rotate o write mode diverso da 0: le scritture CPU copiano il byte nei piani abilitati.</summary>
public sealed partial class PCCanvas
{
    // Attribute controller dopo il BIOS: colore logico -> registro DAC (modi VGA a 16 e 2 colori).
    private static readonly byte[] Attributes16 = [0, 1, 2, 3, 4, 5, 0x14, 7, 0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F];
    private static readonly byte[] Attributes2 = [0, 0x3F];
    private readonly byte[] dac = new byte[256 * 3];
    private readonly byte[] dacPending = new byte[3];
    private readonly byte[] sequencer = [0, 0, 0x0F, 0, 0, 0, 0, 0];
    private readonly byte[] graphicsController = new byte[16];
    private int sequencerIndex, graphicsIndex, dacWriteIndex, dacReadIndex, dacWriteComponent, dacReadComponent, dacState;
    private byte pelMask = 0xFF;

    /// <summary>Segmento della finestra video vista dalla CPU: B800 per CGA, A000 per EGA/VGA.</summary>
    public int VideoSegment => Adapter == PCAdapter.CGA ? 0xB800 : 0xA000;
    /// <summary>Byte indirizzabili nella finestra: 32 KiB per CGA (16 KiB ripetuti), 64 KiB per EGA/VGA.</summary>
    public int VideoWindowBytes => Adapter == PCAdapter.CGA ? 0x8000 : 0x10000;

    /// <summary>Lettura CPU nella finestra video. Nei modi planari legge il piano scelto da read map select (3CF indice 4).</summary>
    public byte ReadVideoMemory(int offset)
    {
        CheckWindow(offset);
        if (VideoMode.Interleaved) return memory[0][offset & 0x3FFF];
        if (VideoMode.Linear) return memory[0][offset];
        int plane = graphicsController[4] & 3;
        return plane < PlaneCount ? memory[plane][offset] : (byte)0;
    }

    /// <summary>Scrittura CPU nella finestra video. Nei modi planari scrive il byte in ogni piano abilitato dalla map mask (3C5 indice 2, default tutti).</summary>
    public void WriteVideoMemory(int offset, byte value)
    {
        CheckWindow(offset);
        if (VideoMode.Interleaved) { WriteByte(offset & 0x3FFF, value); return; }
        if (VideoMode.Linear) { WriteByte(offset, value); return; }
        int mask = sequencer[2] & 0x0F; bool written = false;
        for (int p = 0; p < PlaneCount; p++) if ((mask >> p & 1) != 0) { memory[p][offset] = value; written = true; }
        if (written) DecodePlanar(offset);
    }

    private void CheckWindow(int offset)
    {
        if ((uint)offset >= (uint)VideoWindowBytes) throw new ArgumentOutOfRangeException(nameof(offset));
    }

    /// <summary>Scrittura su porta (QBasic OUT). Valori DAC oltre 63 sono troncati a 6 bit come sull'hardware.</summary>
    public void WritePort(int port, byte value)
    {
        switch (port)
        {
            case 0x3C4: RequireEgaOrVga(); sequencerIndex = value & 7; break;
            case 0x3C5: RequireEgaOrVga(); sequencer[sequencerIndex] = value; break;
            case 0x3CE: RequireEgaOrVga(); graphicsIndex = value & 15; break;
            case 0x3CF: RequireEgaOrVga(); graphicsController[graphicsIndex] = value; break;
            case 0x3C6: RequireDac(); pelMask = value; break;
            case 0x3C7: RequireDac(); dacReadIndex = value; dacReadComponent = 0; dacState = 3; break;
            case 0x3C8: RequireDac(); dacWriteIndex = value; dacWriteComponent = 0; dacState = 0; break;
            case 0x3C9:
                RequireDac(); dacPending[dacWriteComponent++] = (byte)(value & 63);
                if (dacWriteComponent == 3)
                {
                    dacPending.CopyTo(dac, dacWriteIndex * 3); RefreshFromDac(dacWriteIndex);
                    dacWriteIndex = (dacWriteIndex + 1) & 255; dacWriteComponent = 0;
                }
                break;
            default: throw UnsupportedPort(port);
        }
    }

    /// <summary>Lettura da porta (QBasic INP).</summary>
    public byte ReadPort(int port)
    {
        switch (port)
        {
            case 0x3C4: RequireEgaOrVga(); return (byte)sequencerIndex;
            case 0x3C5: RequireEgaOrVga(); return sequencer[sequencerIndex];
            case 0x3CE: RequireEgaOrVga(); return (byte)graphicsIndex;
            case 0x3CF: RequireEgaOrVga(); return graphicsController[graphicsIndex];
            case 0x3C6: RequireDac(); return pelMask;
            case 0x3C7: RequireDac(); return (byte)dacState;
            case 0x3C8: RequireDac(); return (byte)dacWriteIndex;
            case 0x3C9:
                RequireDac(); byte component = dac[dacReadIndex * 3 + dacReadComponent++];
                if (dacReadComponent == 3) { dacReadIndex = (dacReadIndex + 1) & 255; dacReadComponent = 0; }
                return component;
            default: throw UnsupportedPort(port);
        }
    }

    private static ArgumentOutOfRangeException UnsupportedPort(int port)
        => new(nameof(port), $"Port 0x{port:X} is not emulated. Supported: 3C4/3C5 (map mask), 3CE/3CF (read map select), 3C6-3C9 (VGA DAC).");
    private void RequireEgaOrVga()
    {
        if (Adapter == PCAdapter.CGA) throw new InvalidOperationException("Sequencer and graphics controller ports require an EGA or VGA mode.");
    }
    private void RequireDac()
    {
        if (Adapter != PCAdapter.VGA) throw new InvalidOperationException("The VGA DAC requires a VGA mode.");
    }

    // Registro DAC usato da un colore logico: identità nel modo 13, attribute controller negli altri modi VGA.
    private int DacIndex(int color) => ColorCount == 256 ? color : ColorCount == 16 ? Attributes16[color] : Attributes2[color];

    private void RefreshFromDac(int dacIndex)
    {
        var rgb = new PCRgb(Expand(dac[dacIndex * 3]), Expand(dac[dacIndex * 3 + 1]), Expand(dac[dacIndex * 3 + 2]));
        for (int color = 0; color < ColorCount; color++)
            if (DacIndex(color) == dacIndex) { palette[color] = rgb; UpdateInk(color); }
    }

    private static byte Expand(int six) => (byte)((six * 255 + 31) / 63);

    /// <summary>DAC predefinito: modo 13 con 16 colori PC, rampa di grigi e cubo 6x6x6; modi a 16/2 colori con i 64 codici EGA. Non identico alla ROM BIOS.</summary>
    private void ResetDac()
    {
        Array.Clear(dac);
        void Set(int index, int r, int g, int b) { dac[index * 3] = (byte)r; dac[index * 3 + 1] = (byte)g; dac[index * 3 + 2] = (byte)b; }
        if (ColorCount == 256)
        {
            for (int i = 0; i < 16; i++) Set(i, Six(PCColors[i].Red), Six(PCColors[i].Green), Six(PCColors[i].Blue));
            for (int i = 0; i < 16; i++) { int gray = (i * 63 + 7) / 15; Set(16 + i, gray, gray, gray); }
            for (int r = 0; r < 6; r++) for (int g = 0; g < 6; g++) for (int b = 0; b < 6; b++) Set(32 + r * 36 + g * 6 + b, (r * 63 + 2) / 5, (g * 63 + 2) / 5, (b * 63 + 2) / 5);
        }
        else
        {
            for (int code = 0; code < 64; code++)
                Set(code, (code >> 2 & 1) * 42 + (code >> 5 & 1) * 21, (code >> 1 & 1) * 42 + (code >> 4 & 1) * 21, (code & 1) * 42 + (code >> 3 & 1) * 21);
        }
        dacWriteIndex = dacReadIndex = dacWriteComponent = dacReadComponent = dacState = 0; pelMask = 0xFF;
        for (int color = 0; color < ColorCount; color++)
        {
            int index = DacIndex(color);
            palette[color] = new(Expand(dac[index * 3]), Expand(dac[index * 3 + 1]), Expand(dac[index * 3 + 2])); UpdateInk(color);
        }
    }

    private static int Six(byte eight) => eight switch { 0 => 0, 85 => 21, 170 => 42, _ => 63 };
}
