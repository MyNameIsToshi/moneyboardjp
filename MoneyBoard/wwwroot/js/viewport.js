// 画面幅（スマホ判定）を matchMedia で監視し、変化を .NET (ViewportService) へ通知する。
// 単一のブレークポイントを ViewportService 側から渡す（query 文字列）。
window.moneyboardViewport = (function () {
    let dotnet = null;
    let mql = null;
    function onChange(e) {
        if (dotnet) dotnet.invokeMethodAsync('OnViewportChanged', e.matches);
    }
    return {
        // dotnetRef: DotNetObjectReference<ViewportService>, query: 例 "(max-width: 640px)"
        // 戻り値＝現在マッチしているか（初期 IsMobile）
        init: function (dotnetRef, query) {
            dotnet = dotnetRef;
            mql = window.matchMedia(query);
            if (mql.addEventListener) mql.addEventListener('change', onChange);
            else mql.addListener(onChange); // 古い Safari 向けフォールバック
            return mql.matches;
        },
        // スマホのアプリシェルは中身(.app-scroll)だけが内部スクロールするため、
        // タブ/ページ遷移時にスクロール位置が維持される。遷移ごとに先頭へ戻す。
        scrollAppTop: function () {
            var el = document.querySelector('.app-scroll');
            if (el) el.scrollTop = 0;
        }
    };
})();
