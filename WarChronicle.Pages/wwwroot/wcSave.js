window.wcSave = {
    key: "warChronicle.browser.save.v1",
    hasSave: function () {
        return window.localStorage.getItem(this.key) !== null;
    },
    load: function () {
        return window.localStorage.getItem(this.key);
    },
    save: function (json) {
        window.localStorage.setItem(this.key, json);
    },
    clear: function () {
        window.localStorage.removeItem(this.key);
    }
};

window.wcGameLog = {
    key: "warChronicle.browser.gameLogs.v1",
    activeKey: "warChronicle.browser.activeGameLog.v1",

    saveActive: function (json) {
        window.localStorage.setItem(this.activeKey, json);
    },

    clearActive: function () {
        window.localStorage.removeItem(this.activeKey);
    },

    readArchive: function () {
        try {
            const raw = window.localStorage.getItem(this.key);
            const value = raw ? JSON.parse(raw) : [];
            return Array.isArray(value) ? value : [];
        } catch {
            return [];
        }
    },

    filenameFor: function (json) {
        let ended = new Date();
        try {
            const doc = typeof json === "string" ? JSON.parse(json) : json;
            if (doc && doc.endedUtc) {
                const parsed = new Date(doc.endedUtc);
                if (!Number.isNaN(parsed.getTime()))
                    ended = parsed;
            }
        } catch {
            // Fall back to the current browser-local time.
        }

        const pad = value => String(value).padStart(2, "0");
        return `WC_GameLog_${ended.getFullYear()}-${pad(ended.getMonth() + 1)}-${pad(ended.getDate())}_${pad(ended.getHours())}-${pad(ended.getMinutes())}-${pad(ended.getSeconds())}.json`;
    },

    archive: function (json) {
        const doc = JSON.parse(json);
        const filename = this.filenameFor(doc);
        const logs = this.readArchive();
        const gameId = doc && doc.gameId ? String(doc.gameId) : filename;
        const entry = {
            gameId: gameId,
            filename: filename,
            endedUtc: doc ? doc.endedUtc : null,
            json: json
        };

        const existingIndex = logs.findIndex(x => x && String(x.gameId) === gameId);
        if (existingIndex >= 0)
            logs[existingIndex] = entry;
        else
            logs.push(entry);

        window.localStorage.setItem(this.key, JSON.stringify(logs));
        window.localStorage.removeItem(this.activeKey);
        return filename;
    },

    download: function (json, filename) {
        const blob = new Blob([json], { type: "application/json;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = filename || this.filenameFor(json);
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};
