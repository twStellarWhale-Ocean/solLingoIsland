namespace LingoIsland.Present;

/// <summary>一段朗讀文本及其語言（<see cref="SpeechSegmenter"/> 之輸出單位）。</summary>
/// <param name="Text">本段文字。</param>
/// <param name="Culture">本段語言（culture 名，如 <c>en-US</c>／<c>zh-TW</c>）。</param>
public readonly record struct SpeechSegment(string Text, string Culture);

/// <summary>
/// 朗讀文本之語言切段器（#252，純函式、無 SAPI 相依故可完整單元測試）。
/// <para>
/// 解決的問題：語言原由**呼叫端硬編一個 culture** 決定，故一段只能一種語音——中英混排的電子書段落
/// 遇中文即由英文語音勉強拼讀或無聲，`spec#8`「唸完自動前進形成連續導讀」在混排書上實質中斷。
/// 語言是**文本的屬性**、不是呼叫點的屬性，故改由本切段器依字元 script 判定。
/// </para>
/// <para>
/// 規則：CJK 表意文字（含擴充區）與 CJK 標點歸 <c>cjkCulture</c>、其餘歸 <c>defaultCulture</c>；連續同類合併；
/// <b>ASCII 標點與空白跟隨前一段</b>——否則 <c>Anna: 你好, Ben.</c> 會碎成七八片、每片換聲導致頓挫。
/// 日韓等其他 script 本階段不分（列後續增量）。
/// </para>
/// </summary>
public static class SpeechSegmenter
{
    /// <summary>預設之 CJK 語言（繁中，與本 app 介面語言一致）。</summary>
    public const string DefaultCjkCulture = "zh-TW";

    /// <summary>
    /// 依字元 script 把 <paramref name="text"/> 切成數段、各帶語言。
    /// <para>
    /// <b>零回歸保證</b>：純英文（或純中文）文本回傳**恰一段**，其 <see cref="SpeechSegment.Culture"/> 為
    /// 該 script 對應之 culture——產生的合成結果與改版前等價。
    /// </para>
    /// 空白或 <c>null</c> 回空清單（呼叫端據此不發聲）。
    /// </summary>
    public static IReadOnlyList<SpeechSegment> Split(string? text, string defaultCulture, string cjkCulture = DefaultCjkCulture)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<SpeechSegment>();
        }

        var segments = new List<SpeechSegment>();
        var buf = new System.Text.StringBuilder();
        bool? bufIsCjk = null;   // null＝尚未定調（開頭之中性字元先積著，歸屬由第一個有 script 的字元決定）

        foreach (var ch in text)
        {
            // 中性字元（ASCII 標點、空白、數字）不自成一段——跟隨前一段，避免碎片化導致頓挫。
            // 尚無前一段時（文本以標點開頭）先積著，待第一個有 script 之字元定調。
            if (IsNeutral(ch))
            {
                buf.Append(ch);
                continue;
            }

            var isCjk = IsCjk(ch);
            if (bufIsCjk is null)
            {
                bufIsCjk = isCjk;
            }
            else if (isCjk != bufIsCjk)
            {
                // script 換了 → 收掉目前這段。尾隨之中性字元已在 buf 內、隨前段送出（規則使然）。
                Flush(segments, buf, bufIsCjk.Value, defaultCulture, cjkCulture);
                bufIsCjk = isCjk;
            }
            buf.Append(ch);
        }

        // 收尾：整份皆中性字元時 bufIsCjk 仍為 null，歸 defaultCulture（如純數字或純標點之段落）
        Flush(segments, buf, bufIsCjk ?? false, defaultCulture, cjkCulture);
        return segments;
    }

    private static void Flush(List<SpeechSegment> into, System.Text.StringBuilder buf, bool isCjk,
                              string defaultCulture, string cjkCulture)
    {
        if (buf.Length == 0)
        {
            return;
        }
        into.Add(new SpeechSegment(buf.ToString(), isCjk ? cjkCulture : defaultCulture));
        buf.Clear();
    }

    /// <summary>中性字元：ASCII 標點、空白與數字——不決定語言、跟隨前一段。</summary>
    private static bool IsNeutral(char ch) =>
        ch < 0x2E80 && !char.IsLetter(ch);

    /// <summary>
    /// CJK 字元：統一表意文字（含擴充 A、相容區）、注音符號，以及 CJK 標點與全形符號。
    /// 假名（平假名／片假名）**不納入**——本階段不支援日語語音，納入反而會把日文段落誤指給中文語音。
    /// </summary>
    private static bool IsCjk(char ch) =>
        (ch >= 0x3000 && ch <= 0x303F) ||   // CJK 標點（。、「」等）
        (ch >= 0x3100 && ch <= 0x312F) ||   // 注音符號
        (ch >= 0x3400 && ch <= 0x4DBF) ||   // 統一表意文字擴充 A
        (ch >= 0x4E00 && ch <= 0x9FFF) ||   // 統一表意文字
        (ch >= 0xF900 && ch <= 0xFAFF) ||   // 相容表意文字
        (ch >= 0xFF00 && ch <= 0xFF65);     // 全形 ASCII 與全形標點
}
