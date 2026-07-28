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
    // <p> 段落：XHTML 之 p 不得巢狀 p，非貪婪配對安全。Singleline 使 . 跨行。
    private static readonly Regex ParagraphTag = new(
        @"<p\b[^>]*>(?<inner>.*?)</p\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 會巢狀之區塊層元素（li／blockquote／div）之起訖標記（#262）：此三者**可自我巢狀**（<li> 內含 <ul><li>、<div> 內含 <div>），
    // 非貪婪同名配對會把外層結束標籤配到內層的 </li>、內層元素則整個配不出來（去重救不了）——故改以**深度感知掃描**（見 ScanNestable）。
    // <p> 不在此列：XHTML 之 p 不得巢狀 p，沿用非貪婪正則、不動既有行為。
    private static readonly Regex NestableToken = new(
        @"<(?<slash>/?)(?<tag>li|blockquote|div)\b[^>]*?(?<self>/?)>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 標題 h1–h6（增量3）：獨立於本文區塊匹配、依文件位置合併，渲染為<b>章節標題</b>（非對白內文）。
    private static readonly Regex HeadingTag = new(
        @"<(?<tag>h[1-6])\b[^>]*>(?<inner>.*?)</\k<tag>\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyPStart = new(@"<p\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BrTag = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    // 語意說話人標記（#262）：<strong class="speaker">Staff:</strong> 等——EPUB 以 class 明示說話人時**優先採信**，
    // 免受行首正則之 ≤3 詞／≤24 字與「冒號後須空白」邊界所限。
    private static readonly Regex SpeakerMark = new(
        @"<(?<t>strong|span|b)\b[^>]*\bclass\s*=\s*(?:""[^""]*\bspeaker\b[^""]*""|'[^']*\bspeaker\b[^']*')[^>]*>(?<name>.*?)</\k<t>\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 連結元素（純導覽清單項判定用）：<li> 剝掉所有 <a>…</a> 後若無剩餘文字，該 li ＝目錄／導覽項、不成段。
    private static readonly Regex AnchorTag = new(
        @"<a\b[^>]*>.*?</a\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 段內首個 <img src="…">（增量3 內嵌圖）：擷取場景圖相對 href（雙或單引號皆容）。
    private static readonly Regex ImgSrc = new(
        @"<img\b[^>]*\bsrc\s*=\s*(?:""(?<src>[^""]*)""|'(?<src>[^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 由章節 XHTML 萃取<b>內文純文字 cue</b>（純函式）：本文區塊<b>逐區塊判定、非整章二選一</b>（#262）——
    /// <c>&lt;p&gt;</c>／<c>&lt;li&gt;</c>／<c>&lt;blockquote&gt;</c> 依文件位置<b>合流</b>（旁白 <c>&lt;p&gt;</c> 與對白
    /// <c>&lt;ul&gt;&lt;li&gt;</c> 混排之章兩者皆成段、對白零遺漏），<c>&lt;div&gt;</c> 僅在章內無 <c>&lt;p&gt;</c> 時納入，
    /// 巢狀取內層，<b>純導覽 <c>&lt;li&gt;</c>（僅單一 <c>&lt;a&gt;</c>）不成段</b>；<c>&lt;h1&gt;</c>–<c>&lt;h6&gt;</c> 另以標題段合併。
    /// 每段：<c>&lt;br&gt;</c> 轉空白、剝其餘標籤、解 HTML 實體、折疊空白、<b>丟空段</b>；表格／複雜排版略過為純文字（MVP）。
    /// 每段投影為 <see cref="SubtitleCue"/>（<c>StartSec=null</c>＝無時間軸、以段序導讀）；說話人採<b>兩段式</b>——
    /// 先取語意標記（<c>class="speaker"</c>），無則退回 <see cref="SubtitleParser.ExtractInlineSpeakers"/> 之段首 <c>Name:</c> 抽取
    /// （合唸交 <see cref="PauseDecider.SplitSpeakers"/> 拆原子、≤3 詞／≤24 字防誤判），皆無則 <c>Speaker=null</c>、<b>免 AI</b>。
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
        // 本文區塊（#262：**逐區塊判定、非整章二選一**）：<p>／<li>／<blockquote> 恆合流，<div> 僅在章內無 <p> 時納入。
        var blocks = MergeBlocks(CollectBody(doc, includeDiv: !AnyPStart.IsMatch(doc)), headings, imgs);
        // <p> 存在但全空（文字不在 <p>、亦無清單項）→ 退回含 <div> 之區塊層，避免整章空白。
        if (blocks.Count == 0) { blocks = MergeBlocks(CollectBody(doc, includeDiv: true), headings, imgs); }
        if (blocks.Count == 0) { return Array.Empty<EbookParagraph>(); }

        // 每段一 cue（無時間軸）；說話人兩段式——① 區塊已由語意標記（class="speaker"）取得者直接帶入、
        // ② 其餘交既有行首 Name: 抽取（沿用影片同一函式、免 AI、合唸留待下游 SplitSpeakers 拆原子；已有 Speaker 者該函式不動）。
        var cues = blocks.Select(b => new SubtitleCue(b.Text, null, b.Speaker)).ToList();
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
    private static List<(string Text, string? Image, bool IsHeading, string? Speaker)> MergeBlocks(
        IReadOnlyList<Block> bodyBlocks, MatchCollection headingMatches, MatchCollection imgMatches)
    {
        var merged = new List<Block>(bodyBlocks);
        foreach (Match m in headingMatches)
        {
            merged.Add(new Block(m.Index, m.Length, m.Groups["inner"].Value, true));
        }
        merged.Sort((a, b) => a.Index.CompareTo(b.Index)); // 依文件順序

        var result = new List<(string Text, string? Image, bool IsHeading, string? Speaker)>();
        string? currentImage = null;
        var imgIdx = 0;
        foreach (var block in merged)
        {
            var isHeading = block.IsHeading;
            // 依文件順序推進「當前生效圖」至本區塊結束前之最後一個 <img>——含區塊外之 <figure>/<div>/裸 img 與區塊內之 img。
            var blockEnd = block.Index + block.Length;
            while (imgIdx < imgMatches.Count && imgMatches[imgIdx].Index < blockEnd)
            {
                currentImage = WebUtility.HtmlDecode(imgMatches[imgIdx].Groups["src"].Value).Trim();
                imgIdx++;
            }
            var inner = block.Inner;
            string? speaker = null;
            if (!isHeading)
            {
                // 語意說話人標記（#262）：取首個 class="speaker" 元素為說話人，並自內文移除該前綴（標題段不參與說話人）。
                var mark = SpeakerMark.Match(inner);
                if (mark.Success)
                {
                    var name = CleanBlock(mark.Groups["name"].Value).TrimEnd('：', ':').Trim();
                    if (name.Length > 0)
                    {
                        speaker = name;
                        inner = inner.Remove(mark.Index, mark.Length);
                    }
                }
            }
            var text = CleanBlock(inner);
            if (text.Length > 0) { result.Add((text, currentImage, isHeading, speaker)); }
        }
        return result;
    }

    /// <summary>
    /// 蒐集本文區塊（#262，純函式）：<c>&lt;p&gt;</c>／<c>&lt;li&gt;</c>／<c>&lt;blockquote&gt;</c> 恆納入、
    /// <c>&lt;div&gt;</c> 依 <paramref name="includeDiv"/>（僅章內無 <c>&lt;p&gt;</c> 時為 true）；再套兩條收斂規則：
    /// <b>巢狀取內層</b>（範圍完全包住另一區塊者捨去，如 <c>&lt;div&gt;</c> 包 <c>&lt;ul&gt;</c>、<c>&lt;li&gt;</c> 內含 <c>&lt;p&gt;</c>，
    /// 避免同段文字重複計段）與<b>純導覽清單項排除</b>（<c>&lt;li&gt;</c> 剝去所有 <c>&lt;a&gt;</c> 後無剩餘文字＝目錄／導覽項，不成段、不朗讀）。
    /// 回依文件位置排序之區塊 match。
    /// </summary>
    private static List<Block> CollectBody(string doc, bool includeDiv)
    {
        var raw = new List<Block>();
        foreach (Match m in ParagraphTag.Matches(doc))
        {
            raw.Add(new Block(m.Index, m.Length, m.Groups["inner"].Value, false));
        }
        foreach (var b in ScanNestable(doc))
        {
            if (b.Tag == "div" && !includeDiv) { continue; }
            // 純導覽清單項不成段（結構判定、不靠 class 名猜測）。<blockquote>／<div> 不適用此判定。
            if (b.Tag == "li" && CleanBlock(AnchorTag.Replace(b.Inner, " ")).Length == 0) { continue; }
            raw.Add(new Block(b.Index, b.Length, b.Inner, false));
        }

        // 巢狀取內層前**先濾掉無文字區塊**（純圖片段、空 <p>、空白容器）——否則「<div>有文字<p></p></div>」之空 <p>
        // 會把唯一帶文字的外層 <div> 判為容器而丟掉，該章反而整章空白（退回路徑之既有保障不得因本次修正失效）。
        var solid = raw.Where(b => CleanBlock(b.Inner).Length > 0).ToList();
        // 巢狀取內層：捨去「完全包住另一個候選區塊」者（如 <div> 包 <ul>、<li> 內含 <p>；範圍相等不可能發生，故用嚴格包含）。
        var kept = solid.Where(a => !solid.Any(b => b.Index >= a.Index
                                                    && b.Index + b.Length <= a.Index + a.Length
                                                    && b.Length < a.Length))
                        .ToList();
        kept.Sort((a, b) => a.Index.CompareTo(b.Index));
        return kept;
    }

    /// <summary>
    /// 深度感知掃描可巢狀之區塊層元素（<c>li</c>／<c>blockquote</c>／<c>div</c>；#262，純函式）：以每個標籤名各一支堆疊配對
    /// 起訖標記，故 <c>&lt;li&gt;Outer&lt;ul&gt;&lt;li&gt;Nested&lt;/li&gt;&lt;/ul&gt;&lt;/li&gt;</c> 之內外層皆能各自取得
    /// （非貪婪正則只會配出一個錯範圍）。回<b>所有深度</b>之元素，取內層與否交呼叫端之巢狀去重。
    /// 未配對之孤兒結束標記略過、未閉合之起始標記捨去（畸形 XHTML 不當機——MVP 邊界）。
    /// </summary>
    private static List<(string Tag, int Index, int Length, string Inner)> ScanNestable(string doc)
    {
        var open = new Dictionary<string, Stack<Match>>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Tag, int Index, int Length, string Inner)>();
        foreach (Match t in NestableToken.Matches(doc))
        {
            if (t.Groups["self"].Value.Length > 0) { continue; } // 自閉合＝無內容
            var tag = t.Groups["tag"].Value.ToLowerInvariant();
            if (t.Groups["slash"].Value.Length == 0)
            {
                if (!open.TryGetValue(tag, out var stack)) { open[tag] = stack = new Stack<Match>(); }
                stack.Push(t);
            }
            else if (open.TryGetValue(tag, out var stack) && stack.Count > 0)
            {
                var start = stack.Pop();
                var innerStart = start.Index + start.Length;
                result.Add((tag, start.Index, t.Index + t.Length - start.Index, doc[innerStart..t.Index]));
            }
        }
        return result;
    }

    /// <summary>本文區塊（#262）：文件位置、範圍長度、原始內文（未清洗）。<c>IsHeading</c> 由 <see cref="MergeBlocks"/> 另行帶入。</summary>
    private readonly record struct Block(int Index, int Length, string Inner, bool IsHeading);

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
