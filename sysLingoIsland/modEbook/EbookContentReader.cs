using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using LingoIsland.Video;
using VersOne.Epub;

namespace LingoIsland.Ebook;

/// <summary>一段落純文字 cue＋其「當前生效之場景圖」＋是否為標題（增量3，spec#11）：<c>ImageHref</c> 為 null＝該段之前無 <c>&lt;img&gt;</c>（退純文字）；<c>IsHeading</c>＝該段來自 <c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>（渲染為章節標題、非對白）。純解析（<see cref="EbookContentReader.ExtractParagraphsWithImages"/>）時 ImageHref 為相對 href、經 EPUB 層（<see cref="EbookContentReader.ExtractContent"/>）正規化為圖片查找 key（圖檔名小寫）。</summary>
public sealed record EbookParagraph(SubtitleCue Cue, string? ImageHref, bool IsHeading = false);

/// <summary>整本內容（增量3，spec#11）：各章段落（含依位置關聯之場景圖 key）＋全書圖片位元組表（key＝圖檔名小寫）。供圖片為主閱讀視圖。</summary>
public sealed record EbookBookContent(
    IReadOnlyList<IReadOnlyList<EbookParagraph>> Chapters,
    IReadOnlyDictionary<string, byte[]> Images);

/// <summary>
/// 電子書<b>內容側</b>解析（[modEbook模組] 章節段落解析契約，spec#7／#10／#11；增量2＋3；比照 [modVideoCapture模組]
/// <see cref="SubtitleParser"/> 之純函式分工）：把章節 XHTML 拆成可逐段導讀之段落 cue。
/// <see cref="ExtractParagraphs"/>／<see cref="ExtractParagraphsWithImages"/> 為<b>不依賴 UI／檔案之純函式</b>（可單元測試、同書同輸出）；
/// 由 EPUB 取整本內容（<see cref="ReadChaptersAsync"/>／<see cref="ExtractChapters"/>）僅 smoke。
/// 逐段導讀以段序（非時間軸）推進——每段投影為 <see cref="SubtitleCue"/>（<c>StartSec=null</c>＝時間未知，#184 已合法化）。
/// **增量3（spec#11）**：解析時<b>依文件順序關聯每段之場景圖</b>（<c>&lt;img&gt;</c>，見 <see cref="ExtractParagraphsWithImages"/>），
/// 供圖片為主閱讀視圖；圖片<b>取用</b>（相對 href→EPUB bytes）由 <see cref="ReadContentAsync"/>／<see cref="ExtractContent"/> 於 EPUB 層處理。
/// **MVP 取捨**：仍僅段落純文字＋場景圖，表格／複雜排版（雙欄等）略過、不當機；使用者原檔唯讀不改寫。
/// </summary>
public static class EbookContentReader
{
    // <script>／<style> 整塊先移除（其內文非閱讀內容，避免 CSS／JS 漏成段落）。
    private static readonly Regex ScriptStyle = new(
        @"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // <p> 段落（**優先**）：XHTML 之 p 不得巢狀 p，非貪婪配對安全。Singleline 使 . 跨行。
    private static readonly Regex ParagraphTag = new(
        @"<p\b[^>]*>(?<inner>.*?)</p\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 章內無 <p> 時**退回**之區塊層元素（div／li／blockquote；h1–h6 另由 HeadingTag 處理、不重複）：非貪婪配對同名結束標籤
    // （巢狀 div 混排文字為 MVP 邊界、可能漏取——契約已聲明本增量僅純文字段落）。
    private static readonly Regex BlockTag = new(
        @"<(?<tag>div|li|blockquote)\b[^>]*>(?<inner>.*?)</\k<tag>\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 標題 h1–h6（增量3）：獨立於本文區塊匹配、依文件位置合併，渲染為<b>章節標題</b>（非對白內文）。
    private static readonly Regex HeadingTag = new(
        @"<(?<tag>h[1-6])\b[^>]*>(?<inner>.*?)</\k<tag>\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyPStart = new(@"<p\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BrTag = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    // 段內首個 <img src="…">（增量3 內嵌圖）：擷取場景圖相對 href（雙或單引號皆容）。
    private static readonly Regex ImgSrc = new(
        @"<img\b[^>]*\bsrc\s*=\s*(?:""(?<src>[^""]*)""|'(?<src>[^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 由章節 XHTML 萃取<b>段落純文字 cue</b>（純函式）：<b>優先 <c>&lt;p&gt;</c></b>；章內無 <c>&lt;p&gt;</c> 時
    /// <b>退回區塊層</b>（<c>&lt;div&gt;</c>／<c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c>／<c>&lt;li&gt;</c>／<c>&lt;blockquote&gt;</c>）
    /// 以免「文字不在 <c>&lt;p&gt;</c>」之章整章空白。每段：<c>&lt;br&gt;</c> 轉空白、剝其餘標籤、解 HTML 實體、折疊空白、<b>丟空段</b>；
    /// 圖片／表格／複雜排版本增量略過為純文字（MVP）。每段投影為 <see cref="SubtitleCue"/>（<c>StartSec=null</c>＝無時間軸、以段序導讀）；
    /// 段首 <c>Name:</c> 前綴以既有 <see cref="SubtitleParser.ExtractInlineSpeakers"/> 抽說話人
    /// （合唸交 <see cref="PauseDecider.SplitSpeakers"/> 拆原子、≤3 詞／≤24 字防誤判）、無前綴 <c>Speaker=null</c>、<b>免 AI</b>。
    /// null／空／真正空章回空清單、不當機。
    /// </summary>
    public static IReadOnlyList<SubtitleCue> ExtractParagraphs(string? chapterXhtml) =>
        ExtractParagraphsWithImages(chapterXhtml).Select(p => p.Cue).ToList();

    /// <summary>
    /// 由章節 XHTML 萃取段落，並<b>依文件順序關聯每段之「當前生效場景圖」</b>（增量3，spec#11）：每段之
    /// <see cref="EbookParagraph.ImageHref"/>＝該段之前最近一個 <c>&lt;img&gt;</c> 之 <c>src</c>（相對 href、未解析）；
    /// 一章多圖亦支援（讀到哪換到哪、不綁章），前無圖之段為 <c>null</c>（退純文字）。純圖片段
    /// （如 <c>&lt;p class="scene-image"&gt;&lt;img/&gt;&lt;/p&gt;</c>）只更新當前圖、不成段。文字萃取與段首 <c>Name:</c>
    /// 說話人沿用 <see cref="ExtractParagraphs"/> 規則、免 AI。null／空／真正空章回空清單、不當機。純函式。
    /// </summary>
    public static IReadOnlyList<EbookParagraph> ExtractParagraphsWithImages(string? chapterXhtml)
    {
        if (string.IsNullOrWhiteSpace(chapterXhtml)) { return Array.Empty<EbookParagraph>(); }

        var doc = ScriptStyle.Replace(chapterXhtml, " ");
        var imgs = ImgSrc.Matches(doc); // 全章 <img>（依文件順序、含 <figure>/<div>/裸 img，非只 <p> 內——如封面 <figure class="cover-photo">）
        var headings = HeadingTag.Matches(doc); // 全章 h1–h6（增量3：渲染為標題、非對白內文）
        // 本文區塊：**優先 <p>**（與 <p> 並存之外層 <div> 不重覆計段）；無 <p>／<p> 全空 → **退回區塊層**避免整章空白。標題與圖依位置合併。
        var body = AnyPStart.IsMatch(doc) ? ParagraphTag.Matches(doc) : BlockTag.Matches(doc);
        var blocks = MergeBlocks(body, headings, imgs);
        if (blocks.Count == 0) { blocks = MergeBlocks(BlockTag.Matches(doc), headings, imgs); } // 退回區塊層取本文
        if (blocks.Count == 0) { return Array.Empty<EbookParagraph>(); }

        // 每段一 cue（無時間軸）；段首 Name: 說話人以既有行首抽取填入（沿用影片同一函式、免 AI、合唸留待下游 SplitSpeakers 拆原子）。
        var cues = blocks.Select(b => new SubtitleCue(b.Text, null, null)).ToList();
        var withSpeakers = SubtitleParser.ExtractInlineSpeakers(cues); // 不改段數、保序
        var result = withSpeakers.Select((c, i) => new EbookParagraph(c, blocks[i].Image, blocks[i].IsHeading)).ToList();
        // 增量3：把該章**首張場景圖回填至其「之前」的無圖開頭段**（如日期標籤、場景開頭旁白），
        // 使整個場景自開頭即顯示該場景圖（否則游標停在圖前開頭段時無圖）。章間不串（各章獨立解析）；
        // 首張圖「之後」的段落仍各自沿用其前最近的圖（多圖仍依位置切換、不受回填影響）。
        var firstImg = result.FirstOrDefault(p => p.ImageHref is not null)?.ImageHref;
        if (firstImg is not null)
        {
            for (int i = 0; i < result.Count && result[i].ImageHref is null; i++)
            {
                result[i] = result[i] with { ImageHref = firstImg };
            }
        }
        return result;
    }

    /// <summary>
    /// 逐一處理區塊 match（依文件順序）：見 <c>&lt;img src&gt;</c> 即更新「當前生效場景圖」、清洗內文、丟空段
    /// （純圖片／空白段只更新圖不成段）；回 (段落文字, 當前生效圖 href) 序列。
    /// </summary>
    /// <summary>
    /// 把<b>本文區塊</b>（<paramref name="bodyMatches"/>＝&lt;p&gt; 或退回之 div/li/blockquote）與<b>標題</b>（<paramref name="headingMatches"/>＝h1–h6）
    /// 依<b>文件位置</b>合併為有序段落，並逐段關聯「當前生效場景圖」（依 <paramref name="imgMatches"/> 位置推進，含 &lt;figure&gt;/裸 img）。
    /// 標題段標 <c>IsHeading</c>；清洗後之空段丟棄（純圖片段只更新圖、不成段）。
    /// </summary>
    private static List<(string Text, string? Image, bool IsHeading)> MergeBlocks(
        MatchCollection bodyMatches, MatchCollection headingMatches, MatchCollection imgMatches)
    {
        var merged = new List<(Match M, bool IsHeading)>();
        foreach (Match m in bodyMatches) { merged.Add((m, false)); }
        foreach (Match m in headingMatches) { merged.Add((m, true)); }
        merged.Sort((a, b) => a.M.Index.CompareTo(b.M.Index)); // 依文件順序

        var result = new List<(string Text, string? Image, bool IsHeading)>();
        string? currentImage = null;
        var imgIdx = 0;
        foreach (var (m, isHeading) in merged)
        {
            // 依文件順序推進「當前生效圖」至本區塊結束前之最後一個 <img>——含區塊外之 <figure>/<div>/裸 img 與區塊內之 img。
            var blockEnd = m.Index + m.Length;
            while (imgIdx < imgMatches.Count && imgMatches[imgIdx].Index < blockEnd)
            {
                currentImage = WebUtility.HtmlDecode(imgMatches[imgIdx].Groups["src"].Value).Trim();
                imgIdx++;
            }
            var text = CleanBlock(m.Groups["inner"].Value);
            if (text.Length > 0) { result.Add((text, currentImage, isHeading)); }
        }
        return result;
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

    // ==== 增量3（spec#11）：圖片為主閱讀之整本內容（段落＋依位置關聯場景圖＋圖片位元組） ====

    /// <summary>
    /// 由藏書 <c>.epub</c>（<see cref="EpubReader.ReadBookAsync"/> 整本載入，含各章 XHTML 與圖片位元組）＋
    /// <see cref="EbookInfo.SpineHrefs"/> 逐章解析段落與**依閱讀位置關聯之場景圖**，並取出全書圖片位元組（增量3）。
    /// 路徑空／檔不存在／info null／解析失敗回空內容、不擲例外（比照增量2 容錯）。**唯讀原檔、不改寫**。
    /// </summary>
    public static async Task<EbookBookContent> ReadContentAsync(string? epubPath, EbookInfo? info)
    {
        if (string.IsNullOrWhiteSpace(epubPath) || !File.Exists(epubPath) || info is null)
        {
            return EmptyContent();
        }
        try
        {
            var book = await EpubReader.ReadBookAsync(epubPath, EbookReader.BuildOptions()).ConfigureAwait(false);
            return ExtractContent(book, info.SpineHrefs);
        }
        catch
        {
            return EmptyContent();
        }
    }

    /// <summary>
    /// 由已載入之 <see cref="EpubBook"/> ＋ spine href 逐章解析段落（含依位置關聯之場景圖 key）並建全書圖片位元組表（smoke；與開檔分離）。
    /// 每段之 <see cref="EbookParagraph.ImageHref"/> 由相對 href **正規化為圖檔名 key**（存在於本書圖片者，否則 null＝退純文字）。
    /// </summary>
    public static EbookBookContent ExtractContent(EpubBook? book, IReadOnlyList<string>? spineHrefs)
    {
        if (book is null) { return EmptyContent(); }
        var images = BuildImageMap(book);
        var hrefs = spineHrefs is { Count: > 0 }
            ? spineHrefs
            : book.ReadingOrder.Select(f => f.FilePath).ToList();
        var chapters = new List<IReadOnlyList<EbookParagraph>>();
        foreach (var href in hrefs)
        {
            var paras = ExtractParagraphsWithImages(ChapterHtml(book, href));
            // 相對 href → 圖檔名 key（存在於本書圖片者）；供閱讀器以 Images[key] 取位元組。
            chapters.Add(paras.Select(p => p with { ImageHref = NormalizeImageKey(p.ImageHref, images) }).ToList());
        }
        return new EbookBookContent(chapters, images);
    }

    private static EbookBookContent EmptyContent() =>
        new(Array.Empty<IReadOnlyList<EbookParagraph>>(), new Dictionary<string, byte[]>());

    /// <summary>以**圖檔名（小寫）**為 key 建全書圖片位元組表（增量3；對相對／絕對／大小寫 href 差異穩健）。空／無圖回空表。</summary>
    private static Dictionary<string, byte[]> BuildImageMap(EpubBook book)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var images = book.Content?.Images?.Local;
        if (images is null) { return map; }
        foreach (var img in images)
        {
            var name = FileNameKey(img.FilePath);
            if (name.Length == 0 || img.Content is not { Length: > 0 }) { continue; }
            map[name] = img.Content; // 同名後者覆前（罕見）
        }
        return map;
    }

    /// <summary>相對 href → 圖檔名 key（存在於 <paramref name="images"/> 者），否則 null（該段退純文字）。純函式。</summary>
    private static string? NormalizeImageKey(string? rawHref, IReadOnlyDictionary<string, byte[]> images)
    {
        var name = FileNameKey(rawHref);
        return name.Length > 0 && images.ContainsKey(name) ? name : null;
    }

    /// <summary>取 href／路徑最後一段檔名、去查詢字串／片段、小寫化為圖片查找 key；空回空字串。純函式。</summary>
    private static string FileNameKey(string? pathOrHref)
    {
        if (string.IsNullOrWhiteSpace(pathOrHref)) { return ""; }
        var s = pathOrHref.Trim().Replace('\\', '/');
        var cut = s.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) { s = s[..cut]; }
        var slash = s.LastIndexOf('/');
        var name = slash >= 0 ? s[(slash + 1)..] : s;
        return name.ToLowerInvariant();
    }
}
