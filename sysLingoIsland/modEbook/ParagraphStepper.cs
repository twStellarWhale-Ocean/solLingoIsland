using LingoIsland.Video;

namespace LingoIsland.Ebook;

/// <summary>
/// 逐段導讀之段序游標推進（[EbookPage] 內容閱讀器契約，spec#7；增量2）：純函式、不依賴 UI／時間軸——
/// 把影片 <see cref="PauseDecider"/> 之「到句暫停」概念<b>平移</b>為「段序前進」（cue＝段落、<c>StartSec=null</c>、以段 index 推進）。
/// 沿用 <see cref="PauseDecider.PauseMatchesSet"/>／<see cref="PauseDecider.SplitSpeakers"/> 之說話人判定：
/// 於指定說話人暫停時，導讀前進<b>跳過非勾選說話人之段、停在符合者</b>。以假段落 cue 清單注入可單元測試（同輸入同輸出）。
/// </summary>
public static class ParagraphStepper
{
    /// <summary>
    /// 自 <paramref name="current"/> 向後找下一個「應停」之段 index（導讀前進／繼續／TTS 唸完自動前進共用）：
    /// 跳過說話人不符 <paramref name="targets"/>／<paramref name="noSpeaker"/> 之段（沿用 <see cref="PauseDecider.PauseMatchesSet"/>），
    /// 回第一個符合者；無更多符合者回 <c>-1</c>（章末）。
    /// <paramref name="targets"/>＝null＝不指定（每段皆停）、空集合＝指定但無人（無段符合→ -1）、非空＝該組任一名字符合（含合唸句拆原子）。
    /// <paramref name="noSpeaker"/>＝true＝於未標示說話人之段停。<paramref name="current"/>＝-1 時自第 0 段起找。
    /// </summary>
    public static int NextStop(IReadOnlyList<SubtitleCue> paragraphs, int current,
        IReadOnlyCollection<string>? targets = null, bool noSpeaker = false)
    {
        if (paragraphs is null || paragraphs.Count == 0) { return -1; }
        var next = current + 1;
        if (next < 0) { next = 0; }
        while (next < paragraphs.Count && !PauseDecider.PauseMatchesSet(targets, noSpeaker, paragraphs[next].Speaker)) { next++; }
        return next < paragraphs.Count ? next : -1;
    }

    /// <summary>
    /// 自 <paramref name="current"/> 向前找上一個「應停」之段 index（沿用同一說話人判定；供「上一段」於指定說話人暫停時亦落在符合段）：
    /// 回第一個符合者；無更多符合者回 <c>-1</c>（章首）。<paramref name="current"/> 逾上界時自末段起找。參數語意同 <see cref="NextStop"/>。
    /// </summary>
    public static int PrevStop(IReadOnlyList<SubtitleCue> paragraphs, int current,
        IReadOnlyCollection<string>? targets = null, bool noSpeaker = false)
    {
        if (paragraphs is null || paragraphs.Count == 0) { return -1; }
        var prev = Math.Min(current, paragraphs.Count) - 1;
        while (prev >= 0 && !PauseDecider.PauseMatchesSet(targets, noSpeaker, paragraphs[prev].Speaker)) { prev--; }
        return prev;
    }

    /// <summary>把段序游標鉗制在 <c>[0, count-1]</c>（開書以 <c>GetReadingProgress</c> 還原進度時防越界；空章回 -1）。純函式。</summary>
    public static int ClampCursor(int index, int count) => count <= 0 ? -1 : Math.Clamp(index, 0, count - 1);
}
