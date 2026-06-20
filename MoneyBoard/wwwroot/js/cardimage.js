// カード明細スクショ読み取り用のクライアント前処理。
// 画像を canvas で縮小し JPEG(base64) 化してから API へ送る（本文上限内に収める＋トークン削減）。
// 入力は「ファイル選択（複数可）」と「PCのクリップボード貼り付け（Ctrl+V）」の2系統。
window.cardImage = {
    _pasteHandler: null,

    // Blob 配列を縮小して [{ data, mediaType }] を返す（画像でないものは除外）。
    async _processBlobs(blobs, maxDim, quality) {
        const out = [];
        for (const blob of blobs) {
            if (!blob || !blob.type || !blob.type.startsWith('image/')) continue;
            try { out.push(await this._downscale(blob, maxDim, quality)); }
            catch (e) { console.warn('image downscale failed', e); }
        }
        return out;
    },

    // 1枚を長辺 maxDim まで縮小（拡大はしない）して JPEG base64 を返す。
    _downscale(blob, maxDim, quality) {
        return new Promise((resolve, reject) => {
            const url = URL.createObjectURL(blob);
            const img = new Image();
            img.onload = () => {
                try {
                    let w = img.naturalWidth, h = img.naturalHeight;
                    const scale = Math.min(1, maxDim / Math.max(w, h));
                    w = Math.max(1, Math.round(w * scale));
                    h = Math.max(1, Math.round(h * scale));
                    const canvas = document.createElement('canvas');
                    canvas.width = w; canvas.height = h;
                    canvas.getContext('2d').drawImage(img, 0, 0, w, h);
                    const dataUrl = canvas.toDataURL('image/jpeg', quality);
                    resolve({ data: dataUrl.split(',')[1], mediaType: 'image/jpeg' });
                } catch (e) { reject(e); }
                finally { URL.revokeObjectURL(url); }
            };
            img.onerror = (e) => { URL.revokeObjectURL(url); reject(e); };
            img.src = url;
        });
    },

    // <input type="file" multiple> の選択ファイルを処理。再選択できるよう value をクリアする。
    async fromInput(input, maxDim, quality) {
        const files = input && input.files ? Array.from(input.files) : [];
        const res = await this._processBlobs(files, maxDim, quality);
        if (input) input.value = '';
        return res;
    },

    // Ctrl+V 貼り付けを購読し、画像があれば .NET の OnPastedImages を呼ぶ。多重購読は防ぐ。
    attachPaste(dotnet, maxDim, quality) {
        this.detachPaste();
        const handler = async (e) => {
            const items = e.clipboardData && e.clipboardData.items;
            if (!items) return;
            const blobs = [];
            for (const it of items) {
                if (it.type && it.type.startsWith('image/')) {
                    const f = it.getAsFile();
                    if (f) blobs.push(f);
                }
            }
            if (blobs.length === 0) return;
            e.preventDefault();
            const res = await this._processBlobs(blobs, maxDim, quality);
            if (res.length > 0) await dotnet.invokeMethodAsync('OnPastedImages', res);
        };
        document.addEventListener('paste', handler);
        this._pasteHandler = handler;
    },

    detachPaste() {
        if (this._pasteHandler) {
            document.removeEventListener('paste', this._pasteHandler);
            this._pasteHandler = null;
        }
    }
};
