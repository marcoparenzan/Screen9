// Blazor Server's own circuit-reconnect dialog, wired as a class instead of the scaffolded loose
// functions — same behavior (Blazor.reconnect()/resumeCircuit(), retry-on-visibility), one ES6 class.
class ReconnectModal {
    constructor() {
        this.dialog = document.getElementById('components-reconnect-modal');
        this.dialog.addEventListener('components-reconnect-state-changed', (e) => this.onStateChanged(e));
        document.getElementById('components-reconnect-button').addEventListener('click', () => this.retry());
        document.getElementById('components-resume-button').addEventListener('click', () => this.resume());
        this.onVisible = () => this.retryWhenVisible();
    }

    onStateChanged(event) {
        switch (event.detail.state) {
            case 'show': this.dialog.showModal(); break;
            case 'hide': this.dialog.close(); break;
            case 'failed': document.addEventListener('visibilitychange', this.onVisible); break;
            case 'rejected': location.reload(); break;
        }
    }

    async retry() {
        document.removeEventListener('visibilitychange', this.onVisible);
        try {
            const reconnected = await Blazor.reconnect();
            if (!reconnected) {
                const resumed = await Blazor.resumeCircuit();
                if (!resumed) location.reload(); else this.dialog.close();
            }
        } catch {
            document.addEventListener('visibilitychange', this.onVisible);
        }
    }

    async resume() {
        try {
            if (!(await Blazor.resumeCircuit())) location.reload();
        } catch {
            this.dialog.classList.replace('components-reconnect-paused', 'components-reconnect-resume-failed');
        }
    }

    async retryWhenVisible() {
        if (document.visibilityState === 'visible') await this.retry();
    }
}

new ReconnectModal();
