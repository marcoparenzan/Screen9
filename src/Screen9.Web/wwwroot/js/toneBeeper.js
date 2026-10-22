// PC-speaker tones (PLAY/BEEP/SOUND) as real Web Audio, in place of PCSpeaker.Beep/Console.Beep: a square
// wave at the given frequency for the given duration. AudioContext needs a user gesture before it can
// play, same as a browser needs a click before any autoplay — ensureContext() resumes it lazily on the
// first tone, which by then follows the key press that started the program.
export class ToneBeeper {
    constructor() { this.context = null; }

    ensureContext() {
        this.context ??= new (window.AudioContext || window.webkitAudioContext)();
        if (this.context.state === 'suspended') this.context.resume();
        return this.context;
    }

    play(frequencyHz, durationMs) {
        if (frequencyHz <= 0 || durationMs <= 0) return;
        const context = this.ensureContext();
        const oscillator = context.createOscillator();
        const gain = context.createGain();
        oscillator.type = 'square';
        oscillator.frequency.value = frequencyHz;
        gain.gain.value = 0.15; // the PC speaker was loud; keep this listenable
        oscillator.connect(gain).connect(context.destination);
        const now = context.currentTime;
        oscillator.start(now);
        oscillator.stop(now + durationMs / 1000);
    }

    dispose() { this.context?.close(); this.context = null; }
}

export function createToneBeeper() {
    return new ToneBeeper();
}
