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
        waitingWorker.postMessage({ type: 'SKIP_WAITING' });
    });
}
