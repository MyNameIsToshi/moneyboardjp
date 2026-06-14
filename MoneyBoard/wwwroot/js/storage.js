window.moneyboardStorage = (function () {
    const DB = 'moneyboard', STORE = 'kv', VER = 1;
    function open() {
        return new Promise((res, rej) => {
            const r = indexedDB.open(DB, VER);
            r.onupgradeneeded = () => r.result.createObjectStore(STORE);
            r.onsuccess = () => res(r.result);
            r.onerror = () => rej(r.error);
        });
    }
    return {
        async get(key) {
            const db = await open();
            return new Promise((res, rej) => {
                const req = db.transaction(STORE, 'readonly').objectStore(STORE).get(key);
                req.onsuccess = () => res(req.result ?? null);
                req.onerror = () => rej(req.error);
            });
        },
        async set(key, val) {
            const db = await open();
            return new Promise((res, rej) => {
                const req = db.transaction(STORE, 'readwrite').objectStore(STORE).put(val, key);
                req.onsuccess = () => res(true);
                req.onerror = () => rej(req.error);
            });
        }
    };
})();

// Shift-JIS（JCB CSV など）をブラウザの TextDecoder でデコードする
window.decodeShiftJis = function (bytes) {
    return new TextDecoder('shift_jis').decode(new Uint8Array(bytes));
};

// 指定 id の要素までスムーズスクロールする（存在しなければ何もしない）
window.scrollToElementId = function (id) {
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};
