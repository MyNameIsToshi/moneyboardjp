namespace MoneyBoardShared;

/// <summary>
/// 年月（"yyyyMM"）を表す値型。文字列キーのパース・整形・前後移動・比較を一元化する。
/// 保存形式は従来どおり "yyyyMM" 文字列のまま（ToString / Parse で相互変換）。
/// </summary>
public readonly record struct Ym(int Year, int Month) : IComparable<Ym>
{
    public static Ym Parse(string s) => new(int.Parse(s[..4]), int.Parse(s[4..6]));

    public static bool TryParse(string? s, out Ym ym)
    {
        ym = default;
        if (s is null || s.Length != 6) return false;
        if (!int.TryParse(s[..4], out var y) || !int.TryParse(s[4..6], out var m)) return false;
        if (m is < 1 or > 12) return false;
        ym = new Ym(y, m);
        return true;
    }

    public override string ToString() => $"{Year}{Month:D2}";
    public string Label => $"{Year}年{Month}月";

    public Ym Prev() => Month == 1 ? new Ym(Year - 1, 12) : new Ym(Year, Month - 1);
    public Ym Next() => Month == 12 ? new Ym(Year + 1, 1) : new Ym(Year, Month + 1);

    public int CompareTo(Ym other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Month.CompareTo(other.Month);

    public static bool operator <(Ym a, Ym b) => a.CompareTo(b) < 0;
    public static bool operator >(Ym a, Ym b) => a.CompareTo(b) > 0;
    public static bool operator <=(Ym a, Ym b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Ym a, Ym b) => a.CompareTo(b) >= 0;

    public static Ym FromDate(DateTime d) => new(d.Year, d.Month);
    public static Ym Today => FromDate(DateTime.Today);
}
