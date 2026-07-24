using System.IO;
using LingoIsland.Ebook;
using LingoIsland.Video;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// 電子書內容側段落解析（[modEbook模組] 章節段落解析契約，spec#7／#10；增量2）：<see cref="EbookContentReader.ExtractParagraphs"/>
/// 為純函式——優先 <c>&lt;p&gt;</c>／無 <c>&lt;p&gt;</c> 退回區塊層、解 HTML 實體、折疊空白、丟空段、段首 <c>Name:</c> 說話人（含合唸/防誤判）、
/// 旁白無說話人、空章退空清單。由 EPUB 取整本內容之組合方法（<see cref="EbookContentReader.ReadChaptersAsync"/>）僅 smoke。
/// </summary>
public class EbookContentReaderTests
{
    private static string NewTempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"lingo-ebk-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    // ---- <p> 段落：每段一 cue、StartSec=null、Speaker=null ----

    [Fact]
    public void ExtractParagraphs_MultipleP_EachBecomesCue_NoTimeNoSpeaker()
    {
        var cues = EbookContentReader.ExtractParagraphs("<body><p>First para.</p><p>Second para.</p></body>");
        Assert.Equal(2, cues.Count);
        Assert.Equal("First para.", cues[0].Text);
        Assert.Equal("Second para.", cues[1].Text);
        Assert.All(cues, c => Assert.Null(c.StartSec));   // 無時間軸（以段序導讀）
        Assert.All(cues, c => Assert.Null(c.Speaker));    // 旁白無說話人
    }

    [Fact]
    public void ExtractParagraphs_StripsInlineTagsAndAttributes()
    {
        var cues = EbookContentReader.ExtractParagraphs(
            "<p class=\"lead\" id=\"x\">Hello <em>brave</em> <a href=\"u\">new</a> <strong>world</strong>.</p>");
        Assert.Single(cues);
        Assert.Equal("Hello brave new world.", cues[0].Text);
    }

    // ---- HTML 實體 ----

    [Fact]
    public void ExtractParagraphs_DecodesHtmlEntities()
    {
        var cues = EbookContentReader.ExtractParagraphs(
            "<p>Tom &amp; Jerry &lt;3 &quot;fun&quot; &#39;yes&#39;&nbsp;end</p>");
        Assert.Single(cues);
        Assert.Equal("Tom & Jerry <3 \"fun\" 'yes' end", cues[0].Text);   // 實體解碼；nbsp 折為空白
    }

    [Fact]
    public void ExtractParagraphs_EntityBrackets_NotReparsedAsTags()
    {
        // 先剝標籤再解實體 → &lt;b&gt; 解出之 <b> 為字面文字、不被當標籤剝掉。
        var cues = EbookContentReader.ExtractParagraphs("<p>use &lt;b&gt; for bold</p>");
        Assert.Equal("use <b> for bold", cues[0].Text);
    }

    // ---- 折疊空白 ----

    [Fact]
    public void ExtractParagraphs_CollapsesWhitespace()
    {
        var cues = EbookContentReader.ExtractParagraphs("<p>  lots\n\n  of \t  spaces  </p>");
        Assert.Equal("lots of spaces", cues[0].Text);
    }

    [Fact]
    public void ExtractParagraphs_BrBecomesSpace()
    {
        var cues = EbookContentReader.ExtractParagraphs("<p>Line one<br/>line two<br>line three</p>");
        Assert.Equal("Line one line two line three", cues[0].Text);   // 段內軟換行＝空白、仍為一段
    }

    // ---- 丟空段（含純圖片、script/style） ----

    [Fact]
    public void ExtractParagraphs_DropsEmptyParagraphs_AndImageOnly()
    {
        var cues = EbookContentReader.ExtractParagraphs(
            "<p></p><p>   </p><p>Real text.</p><p><img src=\"pic.png\" alt=\"\"/></p>");
        Assert.Single(cues);                    // 空段／純圖片段皆丟（MVP 圖片略過為純文字＝無）
        Assert.Equal("Real text.", cues[0].Text);
    }

    [Fact]
    public void ExtractParagraphs_SkipsScriptAndStyleBlocks()
    {
        var cues = EbookContentReader.ExtractParagraphs(
            "<p>Before<style>.x{color:red}</style> and <script>alert(1)</script>after</p>");
        Assert.Single(cues);
        Assert.Equal("Before and after", cues[0].Text);   // style/script 內文不漏成段落
    }

    // ---- 無 <p> 退回區塊層（div/h1–h6/li/blockquote） ----

    [Fact]
    public void ExtractParagraphs_NoP_FallsBackToBlockLevel()
    {
        var cues = EbookContentReader.ExtractParagraphs(
            "<body><h2>Chapter Title</h2><div>A div paragraph.</div>" +
            "<ul><li>First item.</li><li>Second item.</li></ul>" +
            "<blockquote>A wise quote.</blockquote></body>");
        Assert.Equal(new[] { "Chapter Title", "A div paragraph.", "First item.", "Second item.", "A wise quote." },
            cues.Select(c => c.Text));
    }

    [Fact]
    public void ExtractParagraphs_PrefersP_IgnoresSurroundingDivWhenPPresent()
    {
        // 有 <p> 即用 <p>；外層 <div> 之框線文字不重覆計段（否則同段落被 div＋p 各算一次）。
        var cues = EbookContentReader.ExtractParagraphs("<div>wrapper noise<p>Only this paragraph.</p></div>");
        Assert.Single(cues);
        Assert.Equal("Only this paragraph.", cues[0].Text);
    }

    [Fact]
    public void ExtractParagraphs_EmptyP_ButDivHasText_FallsBackToDiv()
    {
        // <p> 全空（文字不在 <p>）→ 仍退回區塊層取得段落、不整章空白（契約：避免整章空白）。
        var cues = EbookContentReader.ExtractParagraphs("<p></p><div>Text lives in a div.</div>");
        Assert.Single(cues);
        Assert.Equal("Text lives in a div.", cues[0].Text);
    }

    // ---- 段首 Name: 說話人（沿用 ExtractInlineSpeakers；含合唸／防誤判） ----

    [Fact]
    public void ExtractParagraphs_LeadingNameColon_ExtractsSpeaker_StripsPrefix()
    {
        var cues = EbookContentReader.ExtractParagraphs(
            "<p>Ryder: Ready for action, pups?</p><p>CHASE: Chase is on the case!</p>");
        Assert.Equal("Ryder", cues[0].Speaker);
        Assert.Equal("Ready for action, pups?", cues[0].Text);   // 前綴已剝
        Assert.Equal("CHASE", cues[1].Speaker);
        Assert.Equal("Chase is on the case!", cues[1].Text);
    }

    [Fact]
    public void ExtractParagraphs_ChorusSpeaker_KeptRaw_ForDownstreamAtomSplit()
    {
        // 合唸前綴原樣存 Speaker（拆原子交下游 PauseDecider.SplitSpeakers，比照影片頁面板）。
        var cues = EbookContentReader.ExtractParagraphs("<p>Peppa/Suzy: We love jumping in puddles!</p>");
        Assert.Equal("Peppa/Suzy", cues[0].Speaker);
        Assert.Equal("We love jumping in puddles!", cues[0].Text);
        Assert.Equal(new[] { "Peppa", "Suzy" }, PauseDecider.SplitSpeakers(cues[0].Speaker));   // 下游可拆為原子
    }

    [Fact]
    public void ExtractParagraphs_Narration_NoSpeaker()
    {
        var cues = EbookContentReader.ExtractParagraphs("<p>The sun rose slowly over the quiet hills.</p>");
        Assert.Null(cues[0].Speaker);
        Assert.Equal("The sun rose slowly over the quiet hills.", cues[0].Text);
    }

    [Theory]
    [InlineData("<p>Well, honestly: this has a comma so it is not a name.</p>")] // 逗號＋>3 詞→非說話人
    [InlineData("<p>This is a really long clause that eventually has: a colon.</p>")] // 前綴過長/多詞
    [InlineData("<p>No colon here at all in this line.</p>")]
    public void ExtractParagraphs_NonSpeakerColon_NotMisjudged(string xhtml)
    {
        var cues = EbookContentReader.ExtractParagraphs(xhtml);
        Assert.Single(cues);
        Assert.Null(cues[0].Speaker);   // 防誤判：非名字冒號不當說話人
    }

    // ---- 空章／null／空白 ----

    [Fact]
    public void ExtractParagraphs_EmptyChapter_ReturnsEmpty()
    {
        Assert.Empty(EbookContentReader.ExtractParagraphs("<html><body></body></html>"));
        Assert.Empty(EbookContentReader.ExtractParagraphs("<html><body><div></div><p>  </p></body></html>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractParagraphs_NullOrWhitespace_ReturnsEmpty(string? input)
    {
        Assert.Empty(EbookContentReader.ExtractParagraphs(input));
    }

    [Fact]
    public void ExtractParagraphs_StableAcrossCalls_SameOutput()
    {
        const string xhtml = "<body><p>Ryder: Go!</p><p>Narration here.</p></body>";
        var a = EbookContentReader.ExtractParagraphs(xhtml);
        var b = EbookContentReader.ExtractParagraphs(xhtml);
        Assert.Equal(a.Select(c => $"{c.Speaker}|{c.Text}"), b.Select(c => $"{c.Speaker}|{c.Text}"));  // 同書同輸出
    }

    // ---- 組合方法（smoke）：由真實 epub 取各章 XHTML → 逐章 ExtractParagraphs ----

    [Fact]
    public async Task ReadChaptersAsync_RealEpub_ExtractsPerChapterParagraphs()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3Bodies(dir, "urn:uuid:content-01", "Reader Book", "Author", "en",
                new[]
                {
                    ("Chapter 1", "<p>Ryder: Ready?</p><p>The pups gathered at the lookout.</p>"),
                    ("Chapter 2", "<div>Only a div lives here.</div>"),   // 無 <p> → 退回區塊層
                });

            var info = (await EbookReader.ParseAsync(path)).Info!;
            Assert.Equal(2, info.SpineHrefs.Count);

            var chapters = await EbookContentReader.ReadChaptersAsync(path, info);

            Assert.Equal(2, chapters.Count);                       // 依 spine 順序逐章
            Assert.Equal(2, chapters[0].Count);
            Assert.Equal("Ryder", chapters[0][0].Speaker);         // 說話人經真實管線抽出
            Assert.Equal("Ready?", chapters[0][0].Text);
            Assert.Null(chapters[0][0].StartSec);                  // 無時間軸
            Assert.Null(chapters[0][1].Speaker);
            Assert.Equal("The pups gathered at the lookout.", chapters[0][1].Text);
            Assert.Single(chapters[1]);                            // 第二章走 <div> 退回
            Assert.Equal("Only a div lives here.", chapters[1][0].Text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ReadChaptersAsync_MissingPath_ReturnsEmpty()
    {
        var chapters = await EbookContentReader.ReadChaptersAsync(
            Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.epub"), new EbookInfo());
        Assert.Empty(chapters);
    }

    [Fact]
    public async Task ReadChaptersAsync_NullInfo_ReturnsEmpty()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3(dir, "id-x", "T", "A", "en", new[] { "C1" }, cover: null);
            Assert.Empty(await EbookContentReader.ReadChaptersAsync(path, null));   // info 為 null → 空清單、不擲例外
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ReadChaptersAsync_BrokenFile_ReturnsEmpty_NoThrow()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteBrokenFile(dir);
            var chapters = await EbookContentReader.ReadChaptersAsync(path, new EbookInfo { SpineHrefs = { "OEBPS/c1.xhtml" } });
            Assert.Empty(chapters);   // 壞檔明確降級、不當機
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExtractChapters_EmptySpineHrefs_FallsBackToReadingOrder()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3Bodies(dir, "id-ro", "RO", "A", "en",
                new[] { ("C1", "<p>Alpha para.</p>"), ("C2", "<p>Beta para.</p>") });
            var book = await VersOne.Epub.EpubReader.ReadBookAsync(path);

            // spineHrefs 空 → 退回 book.ReadingOrder 之 FilePath 序（仍逐章解析）。
            var chapters = EbookContentReader.ExtractChapters(book, Array.Empty<string>());
            Assert.Equal(2, chapters.Count);
            Assert.Equal("Alpha para.", chapters[0][0].Text);
            Assert.Equal("Beta para.", chapters[1][0].Text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExtractChapters_UnknownHref_ChapterEmpty_NoThrow()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3Bodies(dir, "id-u", "U", "A", "en", new[] { ("C1", "<p>Only.</p>") });
            var book = await VersOne.Epub.EpubReader.ReadBookAsync(path);

            // 對不到之 href → 該章退空段清單、不當機（其餘章不受影響）。
            var chapters = EbookContentReader.ExtractChapters(book, new[] { "OEBPS/c1.xhtml", "OEBPS/does-not-exist.xhtml" });
            Assert.Equal(2, chapters.Count);
            Assert.Equal("Only.", chapters[0][0].Text);
            Assert.Empty(chapters[1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExtractChapters_NullBook_ReturnsEmpty()
    {
        Assert.Empty(EbookContentReader.ExtractChapters(null, new[] { "x" }));
    }
}
