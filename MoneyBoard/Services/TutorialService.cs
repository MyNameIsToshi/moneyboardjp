using MoneyBoardShared;

namespace MoneyBoard.Services;

/// <summary>
/// チュートリアルの表示状態（コーチマーク→モーダルの2段階）を管理する（#111）。
/// 既読は AppState.TutorialSeenVersion（サーバー保存）で持つため、判定・保存は LedgerService 経由。
/// WASM では Scoped＝実質シングルトンで、AnnouncementService と同じライフサイクル。
/// </summary>
public class TutorialService(LedgerService svc, AnnouncementService announce)
{
    public bool ShowCoachMark { get; private set; }
    public bool ShowModal { get; private set; }

    /// <summary>表示状態が変化したとき。購読側は InvokeAsync(StateHasChanged) すること。</summary>
    public event Action? Changed;

    /// <summary>
    /// IsLoaded 完了後に Home から呼ぶ（AnnouncementService.Changed でも再試行する）。
    /// 既読版が現行版未満の初回だけコーチマークを開始する。お知らせ（What's New／一覧）はどちらも
    /// AppTitle の初期化（LedgerService の読込より早く走りうる）で独立に強制表示されるため、
    /// 同時に2つの強制オーバーレイが重ならないよう、お知らせ側が閉じるまで待つ
    /// （コーチマークの対象＝マイページのナビ項目は、お知らせダイアログ表示中は
    /// CSS で下部バーごと隠れており測定できないため）。
    /// </summary>
    public void TriggerIfNeeded()
    {
        if (ShowCoachMark || ShowModal) return;
        if (announce.ShowList || announce.UnreadItems.Count > 0) return;
        if (!svc.IsLoaded) return;
        if (!TutorialMath.ShouldForceShow(svc.State.TutorialSeenVersion)) return;
        ShowCoachMark = true;
        Changed?.Invoke();
    }

    /// <summary>コーチマークの「次へ」：モーダルへ進む。</summary>
    public void AdvanceFromCoachMark()
    {
        ShowCoachMark = false;
        ShowModal = true;
        Changed?.Invoke();
    }

    /// <summary>マイページの？ボタンから任意に再表示（コーチマークは挟まず直接モーダル）。</summary>
    public void OpenManually()
    {
        ShowCoachMark = false;
        ShowModal = true;
        Changed?.Invoke();
    }

    /// <summary>スキップ／閉じる／完了：いずれも既読化して現行版を保存する。</summary>
    public async Task CloseAsync()
    {
        var wasOpen = ShowCoachMark || ShowModal;
        ShowCoachMark = false;
        ShowModal = false;
        Changed?.Invoke();
        if (wasOpen && svc.State.TutorialSeenVersion < TutorialMath.CurrentVersion)
        {
            svc.State.TutorialSeenVersion = TutorialMath.CurrentVersion;
            await svc.SaveAsync();
        }
    }
}
