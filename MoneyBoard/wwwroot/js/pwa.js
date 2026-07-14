// PWA: service worker 登録 + アプリ更新時の再読み込み導線
// skipWaiting は同一originの全タブに反映される（controllerchangeは全タブで発火）ため、
// 「このタブで更新をクリックしたか」を updateRequested で管理し、
// 他タブの未保存編集を巻き込んで無操作リロードしないようにする。
let updateRequested = false;

if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('service-worker.js').then(registration => {
            // 前回セッションでインストール済みのまま waiting になっている場合も検知する
            // （updatefound は今回のページロード後に新規インストールが始まった時にしか発火しないため）
            if (registration.waiting && navigator.serviceWorker.controller) {
                showUpdateToast(registration.waiting);
            }

            registration.addEventListener('updatefound', () => {
                const newWorker = registration.installing;
                if (!newWorker) return;
                newWorker.addEventListener('statechange', () => {
                    if (newWorker.state === 'installed' && navigator.serviceWorker.controller) {
                        showUpdateToast(newWorker);
                    }
                });
            });

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

function showUpdateToast(waitingWorker) {
    if (document.getElementById('pwa-update-toast')) return;

    const toast = document.createElement('div');
    toast.id = 'pwa-update-toast';
    toast.innerHTML = '新しいバージョンがあります。 <a href="" class="pwa-update-reload">更新する</a>';
    document.body.appendChild(toast);

    toast.querySelector('.pwa-update-reload').addEventListener('click', event => {
        event.preventDefault();
        updateRequested = true;
        // このタブでの明示的な更新操作であることを、クリック時刻つきで記録する。直後の自タブ再読込で
        // AppUpdateService（#86）の「vX.Y.Zに更新されました」事後通知と二重に出ないよう抑止するため。
        // 時刻を持たせるのは、reloadが完了しなかった場合（タブを閉じた・iOSでcontrollerchangeが
        // 発火しない等）にフラグが残り続け、無関係な将来の更新まで誤って抑止してしまうのを防ぐため。
        try { localStorage.setItem('mb_pwa_manual_reload', Date.now().toString()); } catch (e) { /* ignore */ }
        waitingWorker.postMessage({ type: 'SKIP_WAITING' });
    });
}
