using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using LingoIsland.Video;
using VersOne.Epub;

namespace LingoIsland.Ebook;

/// <summary>
/// 電子書<b>內容側</b>解析（[modEbook模組] 章節段落解析契約，spec#7／#10；增量2；比照 [modVideoCapture模組]
/// <see cref="SubtitleParser"/> 之純函式分工）：把章節 XHTML 拆成可逐段導讀之段落 cue。
/// <see cref="ExtractParagraphs"/> 為<b>不依賴 UI／檔案之純函式</b>（可單元測試、同書同輸出）；
/// 由 EPUB 取整本內容（<see cref="ReadChaptersAsync"/>／<see cref="ExtractChapters"/>）僅 smoke。
/// 逐段導讀以段序（非時間軸）推進——每段投影為 <see cref="SubtitleCue"/>（<c>StartSec=null</c>＝時間未知，#184 已合法化）。
/// **MVP 取捨**：章節渲染僅段落純文字，EPUB 內嵌圖片／表格／複雜排版（CSS／雙欄等）略過為純文字、不當機；使用者原檔唯讀不改寫。
/// </summary>
public static class EbookContentReader
{
    // <script>／<style> 整塊先移除（其內文非閱讀內容，避免 CSS／JS 漏成段落）。
    private static readonly Regex ScriptStyle = new(
        @"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // <p> 段落（**優先**）：XHTML 之 p 不得巢狀 p，非貪婪配對安全。Singleline 使 . 跨行。
    private static readonly Regex ParagraphTag = new(
        @"<p\b[^>]*>(?<inner>.*?)</p\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 章內無 <p> 時**退回**之區塊層元素（div／h1–h6／li／blockquote）：非貪婪配對同名結束標籤
    // （巢狀 div 混排文字為 MVP 邊界、可能漏取——契約已聲明本增量僅純文字段落）。
    private static readonly Regex BlockTag = new(
        @"<(?<tag>div|h[1-6]|li|blockquote)\b[^>]*>(?<inner>.*?)</\k<tag>\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyPStart = new(@"<p\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BrTag = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// 由章節 XHTML 萃取<b>段落純文字 cue</b>（純函式）：<b>優先 <c>&lt;p&gt;</c></b>；章內無 <c>&lt;p&gt;</c> 時
    /// <b>退回區塊層</b>（<c>&lt;div&gt;</c>／<c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>／<c>&lt;li&gt;</c>／<c>&lt;blockquote&gt;</c>）
    /// 以免「文字不在 <c>&lt;p&gt;</c>」之章整章空白。每段：<c>&lt;br&gt;</c> 轉空白、剝其餘標籤、解 HTML 實體、折疊空白、<b>丟空段</b>；
    /// 圖片／表格／複雜排版本增量略過為純文字（MVP）。每段投影為 <see cref="SubtitleCue"/>（<c>StartSec=null</c>＝無時間軸、以段序導讀）；
    /// 段首 <c>Name:</c> 前綴以既有 <see cref="SubtitleParser.ExtractInlineSpeakers"/> 抽說話人
    /// （合唸交 <see cref="PauseDecider.SplitSpeakers"/> 拆原子、≤3 詞／≤24 字防誤判）、無前綴 <c>Speaker=null</c>、<b>免 AI</b>。
    /// null／空／真正空章回空清單、不當機。
    /// </summary>
    public static IReadOnlyList<SubtitleCue> ExtractParagraphs(string? chapterXhtml)
    {
        if (string.IsNullOrWhiteSpace(chapterXhtml)) { return Array.Empty<SubtitleCue>(); }

        var doc = ScriptStyle.Replace(chapterXhtml, " ");
        var texts = new List<string>();
        // **優先 <p>**（與 <p> 並存之外層 <div> 不重覆計段）。
        if (AnyPStart.IsMatch(doc)) { CollectBlocks(ParagraphTag.Matches(doc), texts); }
        // 無 <p>、或 <p> 全為空（文字不在 <p>）→ **退回區塊層**，避免整章空白。
        if (texts.Count == 0) { CollectBlocks(BlockTag.Matches(doc), texts); }
        if (texts.Count == 0) { return Array.Empty<SubtitleCue>(); }

        // 每段一 cue（無時間軸）；段首 Name: 說話人以既有行首抽取填入（沿用影片同一函式、免 AI、合唸留待下游 SplitSpeakers 拆原子）。
        var cues = texts.Select(t => new SubtitleCue(t, null, null)).ToList();
        return SubtitleParser.ExtractInlineSpeakers(cues);
    }

    /// <summary>逐一清洗區塊 match 之內文、丟空段（純圖片／空白段）後收入 <paramref name="texts"/>。</summary>
    private static void CollectBlocks(MatchCollection matches, List<string> texts)
    {
        foreach (Match m in matches)
        {
            var text = CleanBlock(m.Groups["inner"].Value);
            if (text.Length > 0) { texts.Add(text); }
        }
    }

    /// <summary>
    /// 清一段區塊內文（純函式）：<c>&lt;br&gt;</c>→空白、<b>先剝標籤再解實體</b>（避免 <c>&amp;lt;</c> 解出之 <c>&lt;</c> 被當標籤）、
    /// 解 HTML 實體（<c>&amp;amp; &amp;lt; &amp;#39; &amp;nbsp;</c> 等）、折疊所有空白（含 <c>&amp;nbsp;</c> 之 U+00A0 屬 <c>\p{Z}</c>）為單一空白、去頭尾。
    /// </summary>
    private static string CleanBlock(string raw)
    {
        var s = BrTag.Replace(raw, " "); // 段內軟換行＝空白（一段仍為一 cue）
        s = AnyTag.Replace(s, "");        // 剝其餘行內標籤（em／strong／a／span／img…）
        s = WebUtility.HtmlDecode(s);     // 解實體（於剝標籤後）
        return Ws.Replace(s, " ").Trim(); // 折疊空白（nbsp／tab／換行）
    }

    /// <summary>
    /// 由藏書 <c>.epub</c>（<see cref="EpubReader.ReadBookAsync"/> 取整本內容——閱讀需整本、非增量1 惰性 <c>OpenBookAsync</c>）
    /// ＋ <see cref="EbookInfo.SpineHrefs"/> 逐章取 XHTML → <see cref="ExtractParagraphs"/> 之<b>組合方法</b>（smoke）。
    /// 回各章之段落 cue 清單（依 spine 順序、外層 index 對應 <c>LastReadChapter</c>、內層 index 對應 <c>LastReadParagraph</c>）。
    /// 路徑空／檔不存在／解析失敗回空清單、不擲例外（比照增量1 容錯）。**唯讀原檔、不改寫**（讀藏書資料夾之 <c>.epub</c> 複本）。
    /// </summary>
    public static async Task<IReadOnlyList<IReadOnlyList<SubtitleCue>>> ReadChaptersAsync(string? epubPath, EbookInfo? info)
    {
        if (string.IsNullOrWhiteSpace(epubPath) || !File.Exists(epubPath) || info is null)
        {
            return Array.Empty<IReadOnlyList<SubtitleCue>>();
        }
        try
        {
            // ReadBookAsync＝整本載入（含各章 XHTML 內容）；沿用增量1 之容錯選項。
            var book = await EpubReader.ReadBookAsync(epubPath, EbookReader.BuildOptions()).ConfigureAwait(false);
            return ExtractChapters(book, info.SpineHrefs);
        }
        catch
        {
            return Array.Empty<IReadOnlyList<SubtitleCue>>();
        }
    }

    /// <summary>
    /// 由已載入之 <see cref="EpubBook"/> ＋ spine href 清單逐章萃取段落（smoke；與開檔分離以利 smoke 測試）。
    /// <paramref name="spineHrefs"/> 空時退回 <see cref="EpubBook.ReadingOrder"/> 之 <c>FilePath</c> 序。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<SubtitleCue>> ExtractChapters(EpubBook? book, IReadOnlyList<string>? spineHrefs)
    {
        var chapters = new List<IReadOnlyList<SubtitleCue>>();
        if (book is null) { return chapters; }
        var hrefs = spineHrefs is { Count: > 0 }
            ? spineHrefs
            : book.ReadingOrder.Select(f => f.FilePath).ToList();
        foreach (var href in hrefs)
        {
            chapters.Add(ExtractParagraphs(ChapterHtml(book, href)));
        }
        return chapters;
    }

    /// <summary>
    /// 由 <c>EpubBook.Content.Html</c> 依 spine href 取章 XHTML（<see cref="EbookInfo.SpineHrefs"/> 存各內容檔 <c>FilePath</c>——
    /// 增量1 由 <c>ReadingOrder</c> 之 <c>FilePath</c> 落地）：先以 <c>TryGetLocalFileByFilePath</c> 對，退以 manifest <c>Key</c> 對；
    /// 找不到回空字串（該章退空段、不當機）。
    /// </summary>
    private static string ChapterHtml(EpubBook book, string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) { return ""; }
        var html = book.Content?.Html;
        if (html is null) { return ""; }
        if (html.TryGetLocalFileByFilePath(href, out var byPath) && byPath is not null) { return byPath.Content ?? ""; }
        if (html.ContainsLocalFileWithKey(href)) { return html.GetLocalFileByKey(href)?.Content ?? ""; }
        return "";
    }
}
