namespace MoneyBoard.Services;

/// <summary>
/// モーダル/シート/ビジー状態の中央レジストリ（#141）。アプリ更新ダイアログが「操作中でないか」を
/// DOMクラスの直接監視ではなく判定するために新設した。各タブ/ページは背面スクロールロックと同じ
/// 判定（AnyDialogOpen）をそのままここへも報告する（新しい判定基準を増やさない・二重管理を避けるため）。
/// 同種のタブ/ページは同時に複数マウントされない（PC版マイページの各設定セクションのように複数の
/// 異なる型が同時にマウントされることはあっても、同一型が2つ同時に存在することはない）ため、
/// キーはコンポーネント型名の文字列で足りる。
/// </summary>
public class OverlayRegistryService
{
    private readonly HashSet<string> _open = new();

    public bool IsAnyOpen => _open.Count > 0;

    /// <summary>開いている件数が 0件⇔1件以上 をまたいで変化したときのみ発火する。</summary>
    public event Action? Changed;

    public void SetOpen(string key, bool isOpen)
    {
        var wasAny = IsAnyOpen;
        if (isOpen) _open.Add(key);
        else _open.Remove(key);
        if (wasAny != IsAnyOpen) Changed?.Invoke();
    }
}
