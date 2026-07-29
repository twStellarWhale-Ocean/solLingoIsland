namespace LingoIsland.Ebook;

/// <summary>
/// 章界雙擊換章判定（[EbookPage] 內容閱讀器契約·【內容】頁快速鍵，#267）：純函式、<b>不讀時鐘</b>——
/// 兩次按鍵之間隔由呼叫端傳入，故同輸入同輸出、可單元測試。
/// <para>
/// 語意邊界（design ＜[EbookPage] 內容閱讀器契約·invariant＞）：<b>只在章界生效</b>——游標已在章末時，
/// 於門檻內連按兩下 <c>Space</c>／<c>Down</c> 進下一章；已在章首時連按兩下 <c>Up</c> 回上一章。
/// 章末之「下一段」與章首之「上一段」本就是 no-op，該處第二下並無既有語意可搶，
/// 故<b>正常閱讀途中單擊行為完全不變、零延遲</b>（不採「等雙擊窗過了才動作」）。
/// <b>兩下都須在章界</b>（只驗第二下會讓「自倒數第二段連按兩下」跳過末段）；換鍵即中斷；
/// 換章後呼叫端須<see cref="ChapterHopState.Reset"/>，防第三下被判為第二次雙擊而連跳兩章。
/// </para>
/// </summary>
public static class ChapterHopDecider
{
    /// <summary>雙擊門檻（毫秒）：取 Windows 預設雙擊時間（500ms）之保守值。</summary>
    public const long DoubleTapWindowMs = 400;

    /// <summary>判定結果：不換章／回上一章／進下一章。</summary>
    public enum Hop
    {
        /// <summary>不換章——維持該鍵之既有單擊語意。</summary>
        None,
        /// <summary>回上一章（章首連按兩下 <c>Up</c>）。</summary>
        PrevChapter,
        /// <summary>進下一章（章末連按兩下 <c>Space</c>／<c>Down</c>）。</summary>
        NextChapter,
    }

    /// <summary>參與章界雙擊之鍵（其餘鍵一律 <see cref="Hop.None"/>）。</summary>
    public enum HopKey
    {
        /// <summary>不參與雙擊之鍵。</summary>
        Other,
        /// <summary>空白鍵（單擊＝播放/繼續）。</summary>
        Space,
        /// <summary>下方向鍵（單擊＝下一段）。</summary>
        Down,
        /// <summary>上方向鍵（單擊＝上一段）。</summary>
        Up,
    }

    /// <summary>
    /// 游標是否已在該鍵方向之章界（<c>Up</c>＝章首、<c>Space</c>／<c>Down</c>＝章末）。空章一律 false。
    /// </summary>
    public static bool AtBoundary(HopKey key, int cursor, int cueCount)
    {
        if (cueCount <= 0) { return false; }
        return key switch
        {
            HopKey.Up => cursor <= 0,
            HopKey.Space or HopKey.Down => cursor >= cueCount - 1,
            _ => false,
        };
    }

    /// <summary>
    /// 判定本次按鍵是否構成「章界雙擊換章」。
    /// <para>
    /// <b>兩下都必須在章界</b>——只驗第二下會出現漏洞：自<b>倒數第二段</b>連按兩下 <c>↓</c> 時，
    /// 第一下走到章末、第二下即跳章，<b>最後一段被整段跳過</b>。要求兩下皆在章界，
    /// 等同於「按第一下之前就已停在章界」（章界處該鍵本為 no-op、游標不動），語意才與
    /// 「已經到最底端了，按兩下才會換章」一致。
    /// </para>
    /// </summary>
    /// <param name="key">本次按下之鍵。</param>
    /// <param name="lastKey">上一次按下之鍵（無則 <see cref="HopKey.Other"/>）。</param>
    /// <param name="elapsedMs">上一次按鍵至本次之間隔毫秒（呼叫端以單調時鐘量測；負值視為不成雙擊）。</param>
    /// <param name="atBoundary">本次按鍵當下是否已在章界（見 <see cref="AtBoundary"/>）。</param>
    /// <param name="lastAtBoundary">上一次按鍵當下是否已在章界。</param>
    /// <returns>換章方向；不成立時 <see cref="Hop.None"/>。</returns>
    public static Hop Decide(HopKey key, HopKey lastKey, long elapsedMs, bool atBoundary, bool lastAtBoundary)
    {
        if (key == HopKey.Other) { return Hop.None; }
        if (key != lastKey) { return Hop.None; }                      // 換鍵即中斷
        if (elapsedMs < 0 || elapsedMs > DoubleTapWindowMs) { return Hop.None; }
        if (!atBoundary || !lastAtBoundary) { return Hop.None; }      // 兩下都須在章界

        return key == HopKey.Up ? Hop.PrevChapter : Hop.NextChapter;
    }
}

/// <summary>
/// 章界雙擊之按鍵記錄（呼叫端持有）：只記「上一次是哪個鍵、在哪個時刻」，判定仍歸
/// <see cref="ChapterHopDecider.Decide"/>。時刻採單調時鐘刻度（如 <c>Environment.TickCount64</c>），
/// 不受系統時間調整影響。
/// </summary>
public sealed class ChapterHopState
{
    private ChapterHopDecider.HopKey _lastKey = ChapterHopDecider.HopKey.Other;
    private long _lastTick;
    private bool _lastAtBoundary;

    /// <summary>
    /// 記入本次按鍵並判定是否換章；不論結果為何都會更新「上一次按鍵」記錄。
    /// </summary>
    /// <param name="key">本次按下之鍵。</param>
    /// <param name="nowTick">本次按鍵之單調時鐘刻度（毫秒）。</param>
    /// <param name="cursor">當前段游標 index。</param>
    /// <param name="cueCount">本章段數。</param>
    public ChapterHopDecider.Hop Press(ChapterHopDecider.HopKey key, long nowTick, int cursor, int cueCount)
    {
        var elapsed = _lastKey == ChapterHopDecider.HopKey.Other ? -1 : nowTick - _lastTick;
        var atBoundary = ChapterHopDecider.AtBoundary(key, cursor, cueCount);
        var hop = ChapterHopDecider.Decide(key, _lastKey, elapsed, atBoundary, _lastAtBoundary);
        _lastKey = key;
        _lastTick = nowTick;
        _lastAtBoundary = atBoundary;
        return hop;
    }

    /// <summary>清除按鍵記錄（換章後必呼叫，防第三下被判為第二次雙擊而連跳兩章）。</summary>
    public void Reset()
    {
        _lastKey = ChapterHopDecider.HopKey.Other;
        _lastTick = 0;
        _lastAtBoundary = false;
    }
}
