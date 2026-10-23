// Forwards keydown events on one screen to a .NET [JSInvokable] method, as the small, serializable shape
// BrowserKeyEvent.cs expects. Two rules, both about not letting the browser steal the program's keys:
//  - Function keys (F5 reload chief among them) are always ours while a screen exists, wherever focus is:
//    an element-only listener let a stray click elsewhere pass F5 straight to a full page reload.
//  - Everything else (arrows, Space, Backspace, Tab, Escape, Ctrl+letter) is only taken — and only
//    forwarded — while this screen has focus, so a neighbouring text field or code editor keeps working.
const functionKeys = new Set(['F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10']);
const screenKeys = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', ' ', 'Backspace', 'Tab', 'Escape']);

export class KeyCapture {
    constructor(element, dotNetRef, methodName) {
        this.element = element;
        this.dotNetRef = dotNetRef;
        this.methodName = methodName;
        this.onKeyDown = (event) => this.handleKeyDown(event);
        element.tabIndex = 0; // focusable, so a click lands here for ordinary typing too
        document.addEventListener('keydown', this.onKeyDown, true); // capture phase: ahead of the browser's own handling
    }

    handleKeyDown(event) {
        const owns = this.element === document.activeElement;
        if (functionKeys.has(event.key)) event.preventDefault();
        if (!owns) return;
        if (screenKeys.has(event.key) || (event.ctrlKey && event.key.length === 1)) event.preventDefault();
        this.dotNetRef.invokeMethodAsync(this.methodName, {
            key: event.key, code: event.code,
            ctrlKey: event.ctrlKey, altKey: event.altKey, shiftKey: event.shiftKey,
        });
    }

    focus() { this.element.focus(); }

    dispose() { document.removeEventListener('keydown', this.onKeyDown, true); }
}

export function createKeyCapture(element, dotNetRef, methodName) {
    return new KeyCapture(element, dotNetRef, methodName);
}
