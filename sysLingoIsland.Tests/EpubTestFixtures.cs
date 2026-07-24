using System.IO;
using System.IO.Compression;
using System.Text;

namespace LingoIsland.Tests;

/// <summary>
/// 程式產最小合法測試 <c>.epub</c>（[modEbook模組] 測試夾具）：以 zip 組出 mimetype＋container.xml＋content.opf
/// （dc:title/creator/language/identifier）＋xhtml＋目錄（EPUB3 <c>nav</c> 或 EPUB2 <c>ncx</c>）＋可選封面；另可產壞檔。
/// 不依賴任何實體 epub 樣本，測試自足。
/// </summary>
internal static class EpubTestFixtures
{
    /// <summary>合法 1x1 PNG（magic bytes \x89PNG…）——供封面測試（ReadCover 回此、CoverExtension 判 .png）。</summary>
    public static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>合法最小 JPEG 起始（magic FF D8 FF）——供 CoverExtension 判 .jpg（內容非完整影像、僅測副檔名判定用）。</summary>
    public static readonly byte[] SampleJpegMagic = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };

    /// <summary>
    /// 產一支 EPUB 3（<c>nav</c> 目錄）測試檔，回其路徑。<paramref name="identifier"/>＝null 則省略 dc:identifier（測退化 key）；
    /// <paramref name="cover"/>＝null 則無封面；<paramref name="chapterTitles"/> 決定 nav 葉節點與 spine。
    /// </summary>
    public static string WriteEpub3(
        string dir,
        string? identifier,
        string title,
        string author,
        string language,
        IReadOnlyList<string> chapterTitles,
        byte[]? cover)
    {
        var path = Path.Combine(dir, $"epub3-{Guid.NewGuid():N}.epub");
        WriteZip(path, zip =>
        {
            AddText(zip, "META-INF/container.xml", ContainerXml());
            AddText(zip, "OEBPS/content.opf", Epub3Opf(identifier, title, author, language, chapterTitles, cover is not null));
            AddText(zip, "OEBPS/nav.xhtml", Epub3Nav(chapterTitles));
            for (var i = 0; i < chapterTitles.Count; i++)
            {
                AddText(zip, $"OEBPS/c{i + 1}.xhtml", ChapterXhtml(chapterTitles[i]));
            }
            if (cover is not null) { AddBytes(zip, "OEBPS/cover.png", cover); }
        });
        return path;
    }

    /// <summary>產一支 EPUB 2（<c>ncx</c> 目錄）測試檔，回其路徑。<paramref name="cover"/>＝null 則無封面。</summary>
    public static string WriteEpub2(
        string dir,
        string? identifier,
        string title,
        string author,
        string language,
        IReadOnlyList<string> chapterTitles,
        byte[]? cover)
    {
        var path = Path.Combine(dir, $"epub2-{Guid.NewGuid():N}.epub");
        WriteZip(path, zip =>
        {
            AddText(zip, "META-INF/container.xml", ContainerXml());
            AddText(zip, "OEBPS/content.opf", Epub2Opf(identifier, title, author, language, chapterTitles, cover is not null));
            AddText(zip, "OEBPS/toc.ncx", Epub2Ncx(identifier, title, chapterTitles));
            for (var i = 0; i < chapterTitles.Count; i++)
            {
                AddText(zip, $"OEBPS/c{i + 1}.xhtml", ChapterXhtml(chapterTitles[i]));
            }
            if (cover is not null) { AddBytes(zip, "OEBPS/cover.png", cover); }
        });
        return path;
    }

    /// <summary>產一支壞檔（非 zip／非 EPUB 之隨機位元組），回其路徑。</summary>
    public static string WriteBrokenFile(string dir)
    {
        var path = Path.Combine(dir, $"broken-{Guid.NewGuid():N}.epub");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("this is not a real epub / zip file at all"));
        return path;
    }

    // ---- zip 組裝 ----

    private static void WriteZip(string path, Action<ZipArchive> build)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        // mimetype 須為首個 entry、且不壓縮（EPUB 慣例）。
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mime.Open(), new UTF8Encoding(false))) { w.Write("application/epub+zip"); }
        build(zip);
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static void AddBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    // ---- XML 內容 ----

    private static string ContainerXml() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
        "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles>" +
        "</container>";

    private static string Epub3Opf(string? identifier, string title, string author, string language, IReadOnlyList<string> chapters, bool hasCover)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">");
        sb.Append("<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
        sb.Append($"<dc:identifier id=\"bookid\">{Esc(identifier ?? "")}</dc:identifier>");
        sb.Append($"<dc:title>{Esc(title)}</dc:title>");
        sb.Append($"<dc:creator>{Esc(author)}</dc:creator>");
        sb.Append($"<dc:language>{Esc(language)}</dc:language>");
        if (hasCover) { sb.Append("<meta name=\"cover\" content=\"cover-image\"/>"); }
        sb.Append("</metadata>");
        sb.Append("<manifest>");
        sb.Append("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.Append($"<item id=\"c{i + 1}\" href=\"c{i + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>");
        }
        if (hasCover) { sb.Append("<item id=\"cover-image\" href=\"cover.png\" media-type=\"image/png\" properties=\"cover-image\"/>"); }
        sb.Append("</manifest>");
        sb.Append("<spine>");
        for (var i = 0; i < chapters.Count; i++) { sb.Append($"<itemref idref=\"c{i + 1}\"/>"); }
        sb.Append("</spine>");
        sb.Append("</package>");
        return sb.ToString();
    }

    private static string Epub3Nav(IReadOnlyList<string> chapters)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">");
        sb.Append("<head><title>TOC</title></head><body>");
        sb.Append("<nav epub:type=\"toc\" id=\"toc\"><ol>");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.Append($"<li><a href=\"c{i + 1}.xhtml\">{Esc(chapters[i])}</a></li>");
        }
        sb.Append("</ol></nav></body></html>");
        return sb.ToString();
    }

    private static string Epub2Opf(string? identifier, string title, string author, string language, IReadOnlyList<string> chapters, bool hasCover)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"2.0\" unique-identifier=\"bookid\">");
        sb.Append("<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">");
        sb.Append($"<dc:identifier id=\"bookid\" opf:scheme=\"ISBN\">{Esc(identifier ?? "")}</dc:identifier>");
        sb.Append($"<dc:title>{Esc(title)}</dc:title>");
        sb.Append($"<dc:creator>{Esc(author)}</dc:creator>");
        sb.Append($"<dc:language>{Esc(language)}</dc:language>");
        if (hasCover) { sb.Append("<meta name=\"cover\" content=\"cover-image\"/>"); }
        sb.Append("</metadata>");
        sb.Append("<manifest>");
        sb.Append("<item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.Append($"<item id=\"c{i + 1}\" href=\"c{i + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>");
        }
        if (hasCover) { sb.Append("<item id=\"cover-image\" href=\"cover.png\" media-type=\"image/png\"/>"); }
        sb.Append("</manifest>");
        sb.Append("<spine toc=\"ncx\">");
        for (var i = 0; i < chapters.Count; i++) { sb.Append($"<itemref idref=\"c{i + 1}\"/>"); }
        sb.Append("</spine>");
        sb.Append("</package>");
        return sb.ToString();
    }

    private static string Epub2Ncx(string? identifier, string title, IReadOnlyList<string> chapters)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append("<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\">");
        sb.Append($"<head><meta name=\"dtb:uid\" content=\"{Esc(identifier ?? "")}\"/></head>");
        sb.Append($"<docTitle><text>{Esc(title)}</text></docTitle>");
        sb.Append("<navMap>");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.Append($"<navPoint id=\"n{i + 1}\" playOrder=\"{i + 1}\"><navLabel><text>{Esc(chapters[i])}</text></navLabel><content src=\"c{i + 1}.xhtml\"/></navPoint>");
        }
        sb.Append("</navMap></ncx>");
        return sb.ToString();
    }

    private static string ChapterXhtml(string title) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>" + Esc(title) + "</title></head>" +
        "<body><h1>" + Esc(title) + "</h1><p>Hello from " + Esc(title) + ".</p></body></html>";

    private static string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "") ?? "";
}
