// 画面幅（スマホ判定）を matchMedia で監視し、変化を .NET (ViewportService) へ通知する。
// 単一のブレークポイントを ViewportService 側から渡す（query 文字列）。
window.moneyboardViewport = (function () {
    let dotnet = null;
    let mql = null;
    // 複数のコンポーネント（各タブ・MainLayout のお知らせ等）が独立にダイアログを開閉しうるため、
    // 背面スクロールロックは参照カウント方式にする。1件でも開いていればロックを維持し、
    // 全件閉じた（0になった）時だけ実際に解除する（先に閉じた側が他の保持者ごと解除する競合を防ぐ）。
    let lockCount = 0;
    function onChange(e) {
        if (dotnet) dotnet.invokeMethodAsync('OnViewportChanged', e.matches);
    }
    // ロック開始時にスクロールバーが消えて背面のレイアウトが右へ詰まらないよう、消える幅ぶんを
    // padding-right で埋める。幅は overflow:hidden を適用する前（lockCount 0→1 の瞬間）に測る。
    function applyLock() {
        var docEl = document.documentElement;
        var appEl = document.querySelector('.app-scroll');
        var docGap = window.innerWidth - docEl.clientWidth;
        docEl.style.paddingRight = docGap > 0 ? docGap + 'px' : '';
        docEl.classList.add('dialog-lock');
        if (appEl) {
            var appGap = appEl.offsetWidth - appEl.clientWidth;
            appEl.style.paddingRight = appGap > 0 ? appGap + 'px' : '';
            appEl.classList.add('dialog-lock');
        }
    }
    function releaseLock() {
        var docEl = document.documentElement;
        var appEl = document.querySelector('.app-scroll');
        docEl.classList.remove('dialog-lock');
        docEl.style.paddingRight = '';
        if (appEl) {
            appEl.classList.remove('dialog-lock');
            appEl.style.paddingRight = '';
        }
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
        },
        // ダイアログ表示中は背面（PC=ウィンドウ／スマホ=.app-scroll）のスクロールを止める。
        // CSS だけだとダイアログが画面に収まる時にホイールが背面へ抜けるため、ここでロックする。
        // 呼び出し側（各コンポーネント）は自身の表示状態が変化した時だけ true/false を1回ずつ
        // 呼ぶ（edge-triggered）ため、ここでの参照カウントは常に増減が対になる。
        setBodyScrollLock: function (locked) {
            if (locked) {
                lockCount++;
                if (lockCount === 1) applyLock();
            } else {
                lockCount = Math.max(0, lockCount - 1);
                if (lockCount === 0) releaseLock();
            }
        }
    };
})();
