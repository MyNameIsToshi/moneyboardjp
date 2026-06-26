// Shift-JIS（JCB CSV など）をブラウザの TextDecoder でデコードする
window.decodeShiftJis = function (bytes) {
    return new TextDecoder('shift_jis').decode(new Uint8Array(bytes));
};

// 指定 id の要素までスムーズスクロールする（存在しなければ何もしない）
window.scrollToElementId = function (id) {
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};
