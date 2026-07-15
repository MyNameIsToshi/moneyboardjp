// PWA: service worker 登録 + アプリ更新時の再読み込み導線
// skipWaiting は同一originの全タブに反映される（controllerchangeは全タブで発火）ため、
// 「このタブで更新をクリックしたか」を updateRequested で管理し、
// 他タブの未保存編集を巻き込んで無操作リロードしないようにする。
let updateRequested = false;

if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('service-worker.js').then(registration => {
            // 新SWの検知・通知自体は AppUpdateService の version.json ポーリング（#141）に一本化した
            // （Web版も含めた全ユーザーへダイアログで通知するため、常時ポーリングでSWイベントに依存しない）。
            // ここでの登録は SKIP_WAITING 経由のリロード実行（activateWaitingAndReload）に必要な
            // registration の確保と、明示的な更新チェックのためだけに残す。

            // ブラウザの自動更新チェックは前回チェックから24時間未満だとスキップされる仕様のため、
            // アプリをフォアグラウンドに戻した時は明示的に更新確認する（デプロイ直後に再度開いた時も検知できるように）。
            document.addEventListener('visibilitychange', () => {
                if (document.visibilityState === 'visible') {
                    registration.update();
                }
            });
        });
    });

    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (!updateRequested) return;
        updateRequested = false;
        window.location.reload();
    });
}

// waiting中のSWを即活性化してリロードする（更新ダイアログ#141の「更新する」・常設リロードボタン#132で共用）。
function activateWaitingAndReload(waitingWorker) {
    updateRequested = true;
    // このタブでの明示的な更新操作であることを、クリック時刻つきで記録する。直後の自タブ再読込で
    // AppUpdateService（#86）の「vX.Y.Zに更新されました」事後通知と二重に出ないよう抑止するため。
    // 時刻を持たせるのは、reloadが完了しなかった場合（タブを閉じた・iOSでcontrollerchangeが
    // 発火しない等）にフラグが残り続け、無関係な将来の更新まで誤って抑止してしまうのを防ぐため。
    try { localStorage.setItem('mb_pwa_manual_reload', Date.now().toString()); } catch (e) { /* ignore */ }
    waitingWorker.postMessage({ type: 'SKIP_WAITING' });
}

// タイトル横の常設リロードボタン（#132）。ブラウザの自動更新チェックは前回チェックから24時間未満だと
// スキップされる仕様のため、まず registration.update() で明示的にチェックしてから waiting を確認する。
// 新しい waiting worker が見つかればそれを即活性化してリロード、見つからなければ（既に最新・SW未登録等）
// 通常のリロードにフォールバックする。
window.moneyboardPwa = {
    checkForUpdateAndReload: async function () {
        if (!('serviceWorker' in navigator)) { window.location.reload(); return; }

        const registration = await navigator.serviceWorker.getRegistration();
        if (!registration) { window.location.reload(); return; }

        try { await registration.update(); } catch (e) { /* ignore */ }

        // update() は新SWの install 開始時点で resolve されることが多く、その時点ではまだ installing 中で
        // registration.waiting が null のことがある（この場合そのまま waiting を見ると更新を取りこぼし、
        // 旧SW配下の通常リロード＝旧バージョンのまま再読込になる）。installing があれば installed(=waiting)
        // へ遷移するまで待ってから判定する。install が失敗/長引くケースに備え短いタイムアウトで打ち切る。
        const installing = registration.installing;
        if (installing && installing.state === 'installing') {
            await new Promise(resolve => {
                const timer = setTimeout(resolve, 10000);
                installing.addEventListener('statechange', () => {
                    if (installing.state !== 'installing') { clearTimeout(timer); resolve(); }
                });
            });
        }

        if (registration.waiting) {
            activateWaitingAndReload(registration.waiting);
        } else {
            window.location.reload();
        }
    },
    // ホーム画面追加後のスタンドアロン起動か（#132：常設リロードボタンをPWA時のみ出すため）。
    // display-mode は Android/PC Chrome 等、navigator.standalone は iOS Safari 用のフォールバック。
    isStandalone: function () {
        return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    }
};
