using System.IO;
using System.Text.RegularExpressions;
using VersOne.Epub;
using VersOne.Epub.Options;
using VersOne.Epub.Schema;

namespace LingoIsland.Ebook;

/// <summary>
/// 電子書解析前端（[modEbook模組]，spec#4，[techItem電子書解析]＝<c>VersOne.Epub</c>）：輸入本機 <c>.epub</c> 路徑，
/// 以 <see cref="EpubReader.OpenBookAsync(string, EpubReaderOptions)"/> **惰性載入** <c>EpubBookRef</c> 產出 <see cref="EbookInfo"/>；
/// 用完即 <c>Dispose</c>（不整本載入記憶體）。壞檔容錯以 <see cref="EpubReaderOptions"/> 放寬解析；
/// 壞檔／非 EPUB／解析失敗回明確 <see cref="EbookParseResult"/>（不擲未捕例外）。**唯讀原檔、不改寫**。
/// 去重 key（<see cref="DedupeKey"/>）、識別碼推導（<see cref="DeriveIdentifier"/>）、葉節點計數（<see cref="CountLeaves"/>）為純函式、可單元測試。
/// </summary>
public static class EbookReader
{
    /// <summary>
    /// 解析一支本機 <c>.epub</c>：成功回 <see cref="EbookParseResult.Ok"/>＋<see cref="EbookInfo"/>；
    /// 路徑空／檔不存在／壞檔／非 EPUB／任何解析例外皆回 <see cref="EbookParseResult.Fail"/>（不擲例外、供上層略過該檔）。
    /// </summary>
    public static async Task<EbookParseResult> ParseAsync(string? epubPath)
    {
        if (string.IsNullOrWhiteSpace(epubPath)) { return EbookParseResult.Fail("路徑為空"); }
        if (!File.Exists(epubPath)) { return EbookParseResult.Fail("檔案不存在"); }
        try
        {
            // OpenBookAsync＝惰性載入（只取中繼／封面／目錄，不整本讀入）；用完 Dispose。
            using var bookRef = await EpubReader.OpenBookAsync(epubPath, BuildOptions()).ConfigureAwait(false);
            if (bookRef is null) { return EbookParseResult.Fail("無法開啟電子書"); }

            var meta = bookRef.Schema?.Package?.Metadata;
            var title = (bookRef.Title ?? "").Trim();
            var author = JoinAuthors(bookRef.AuthorList);
            var language = FirstLanguage(meta, bookRef.Schema?.Package);
            var dcIds = meta?.Identifiers?.Select(i => i.Identifier) ?? Enumerable.Empty<string?>();

            // 目錄（nav/ncx 統一遞迴樹）→ 投影為可序列化樹 → 葉節點計數＝章數。
            var navRefs = await bookRef.GetNavigationAsync().ConfigureAwait(false);
            var toc = MapToc(navRefs);
            var chapterCount = CountLeaves(toc);

            // spine 閱讀順序（相對路徑）；供後續章節渲染增量。
            var readingOrder = await bookRef.GetReadingOrderAsync().ConfigureAwait(false);
            var spine = readingOrder?
                .Select(r => r.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList() ?? new List<string>();

            // 封面（byte[]?，缺封面回 null——合法）；封面讀取失敗不致整體失敗。
            byte[]? cover = null;
            try { cover = await bookRef.ReadCoverAsync().ConfigureAwait(false); }
            catch { cover = null; }

            var info = new EbookInfo
            {
                Identifier = DeriveIdentifier(dcIds, title, author),
                Title = title,
                Author = author,
                Language = language,
                ChapterCount = chapterCount,
                SpineHrefs = spine,
                Toc = toc,
                CoverBytes = cover is { Length: > 0 } ? cover : null,
            };
            return EbookParseResult.Ok(info);
        }
        catch (Exception ex)
        {
            // 壞檔／非 EPUB／容器解不出／畸形 XML 超出容錯 → 明確失敗、不中斷整批。
            return EbookParseResult.Fail(ex.Message);
        }
    }

    /// <summary>放寬容錯之解析選項：缺目錄／無效 manifest/spine 項／XML 標頭瑕疵不擲例外（提升匯入成功率、spec#4）。</summary>
    public static EpubReaderOptions BuildOptions() => new()
    {
        PackageReaderOptions = new PackageReaderOptions
        {
            IgnoreMissingToc = true,          // 缺目錄不擲例外（章數退 0）
            SkipInvalidManifestItems = true,  // 略過無效 manifest 項
            SkipInvalidSpineItems = true,     // 略過無效 spine 項
        },
        XmlReaderOptions = new XmlReaderOptions
        {
            SkipXmlHeaders = true,            // 容忍畸形 XML 宣告／BOM
        },
    };

    // ---- 純函式（可單元測試，不觸檔案／不依賴 UI） ----

    /// <summary>
    /// 推導識別碼（純函式）：取第一個非空 <c>dc:identifier</c>（去頭尾）；缺識別碼時退以「書名|作者」為識別基底。
    /// 回傳值即 <see cref="EbookInfo.Identifier"/>，去重再以 <see cref="DedupeKey"/> 正規化。
    /// </summary>
    public static string DeriveIdentifier(IEnumerable<string?>? dcIdentifiers, string? title, string? author)
    {
        var id = dcIdentifiers?
            .Select(s => s?.Trim())
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(id)) { return id!; }
        return $"{(title ?? "").Trim()}|{(author ?? "").Trim()}"; // 缺 dc:identifier 之退化基底
    }

    /// <summary>
    /// 去重 key（純函式）：正規化識別碼——折疊連續空白為單一空白、去頭尾、轉小寫（不變文化），
    /// 使同書於不同大小寫／空白呈現仍判為同一本（跨全櫃去重之比較鍵）。空識別碼回空字串。
    /// </summary>
    public static string DedupeKey(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) { return ""; }
        var collapsed = Regex.Replace(identifier, @"\s+", " ").Trim();
        return collapsed.ToLowerInvariant();
    }

    /// <summary>
    /// 葉節點計數（純函式）：目錄樹之葉（無子節點者）數＝章數。空樹回 0；有子節點者不計自身、遞迴計子。
    /// nav（EPUB3）與 ncx（EPUB2）皆由套件統一為此樹、故計數方式一致。
    /// </summary>
    public static int CountLeaves(IReadOnlyList<EbookTocNode> nodes)
    {
        var count = 0;
        foreach (var n in nodes)
        {
            if (n.Children.Count == 0) { count++; }
            else { count += CountLeaves(n.Children); }
        }
        return count;
    }

    // ---- 內部工具 ----

    /// <summary>AuthorList 以「, 」串接（去空白項）；無作者回空字串。</summary>
    private static string JoinAuthors(IEnumerable<string?>? authors) =>
        authors is null ? "" : string.Join(", ", authors.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim()));

    /// <summary>語言：優先取 <c>dc:language</c> 第一個非空；退取 package 之 xml:lang；皆無回空字串。</summary>
    private static string FirstLanguage(EpubMetadata? meta, EpubPackage? pkg)
    {
        var fromMeta = meta?.Languages?
            .Select(l => l.Language)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (!string.IsNullOrWhiteSpace(fromMeta)) { return fromMeta!.Trim(); }
        return string.IsNullOrWhiteSpace(pkg?.Language) ? "" : pkg!.Language!.Trim();
    }

    /// <summary>目錄 ref 樹（VersOne 型別）投影為可序列化 <see cref="EbookTocNode"/> 樹（遞迴）。null 回空清單。</summary>
    private static List<EbookTocNode> MapToc(IEnumerable<EpubNavigationItemRef>? navRefs)
    {
        var result = new List<EbookTocNode>();
        if (navRefs is null) { return result; }
        foreach (var n in navRefs)
        {
            result.Add(new EbookTocNode
            {
                Title = (n.Title ?? "").Trim(),
                Children = MapToc(n.NestedItems),
            });
        }
        return result;
    }
}
