// Firebase Authentication（compat 版）の薄いラッパー。Blazor から JS interop で呼ぶ。
// apiKey は Web 用の公開クライアント設定（秘密ではない）。
(function () {
  var firebaseConfig = {
    apiKey: "AIzaSyAs680ksXUCNsTcJqHt_Bmqw4TQZrpF5sA",
    authDomain: "money-board-jp.firebaseapp.com",
    projectId: "money-board-jp",
    appId: "1:686851896467:web:5335637a9fd7763778c966"
  };
  firebase.initializeApp(firebaseConfig);

  var started = false;
  window.mbAuth = {
    // 認証状態の監視を開始し、変化を .NET に通知（初回はサインイン判定後に必ず1回発火）。
    start: function (dotNetRef) {
      if (started) return;
      started = true;
      firebase.auth().onAuthStateChanged(function (u) {
        dotNetRef.invokeMethodAsync('OnAuthStateChanged',
          u ? { uid: u.uid, email: u.email, name: u.displayName } : null);
      });
    },
    signInGoogle: function () {
      var provider = new firebase.auth.GoogleAuthProvider();
      return firebase.auth().signInWithPopup(provider);
    },
    signOut: function () { return firebase.auth().signOut(); },
    // 有効な ID トークン（JWT）を返す。期限切れは getIdToken が自動更新する。
    getToken: function () {
      var u = firebase.auth().currentUser;
      return u ? u.getIdToken() : Promise.resolve(null);
    }
  };
})();
