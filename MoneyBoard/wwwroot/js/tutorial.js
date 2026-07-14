// チュートリアル: コーチマーク（スポットライト）対象要素の位置取得（#111）。
// BottomNav/SideNav は position:fixed / sticky で画面内に常時在席するため、
// 表示のたび1回測るだけでよい（スクロール追従は行わない）。
window.moneyboardTutorial = {
    getRect: function (selector) {
        var el = document.querySelector(selector);
        if (!el) return null;
        var r = el.getBoundingClientRect();
        return {
            top: r.top,
            left: r.left,
            width: r.width,
            height: r.height,
            viewportWidth: window.innerWidth,
            viewportHeight: window.innerHeight
        };
    },
    // PWA追加方法チュートリアル（#111）：初期表示するOSタブのヒントにのみ使う（UAは偽装・誤判定
    // がありうるため強制切替はせず、タブは常に手動で選び直せる）。'ios' | 'android' | 'pc'。
    detectPlatform: function () {
        var ua = navigator.userAgent || '';
        // iPadOS 13+ は既定でデスクトップ版UA（Macintosh）を名乗るため、タッチ対応で判別する。
        var isIPadOs13 = /Macintosh/i.test(ua) && navigator.maxTouchPoints > 1;
        if (/iPhone|iPad|iPod/i.test(ua) || isIPadOs13) return 'ios';
        if (/Android/i.test(ua)) return 'android';
        return 'pc';
    }
};
