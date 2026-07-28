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

    // ---- 增量3：內嵌圖依閱讀位置關聯段落（spec#11，純函式） ----

    [Fact]
    public void WithImages_SceneImageThenParagraphs_ParagraphsCarryImage_ImageOnlyPNotACue()
    {
        var xhtml =
            "<p class=\"scene-image\"><img src=\"../images/01.png\" alt=\"x\"/></p>" +
            "<p>First line.</p><p>Second line.</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.Equal(2, ps.Count);                                                   // 純圖片 <p> 只更新圖、不成段
        Assert.Equal(new[] { "First line.", "Second line." }, ps.Select(p => p.Cue.Text));
        Assert.All(ps, p => Assert.Equal("../images/01.png", p.ImageHref));          // 其後各段皆帶該場景圖
    }

    [Fact]
    public void WithImages_LeadingParagraphBeforeFirstImage_BackfilledWithSceneImage()
    {
        var xhtml = "<p>Intro.</p><p class=\"scene-image\"><img src=\"a.png\"/></p><p>After.</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        // 增量3：圖前之開頭段（Intro）回填該章首張場景圖，使整個場景自開頭即顯示圖；跨圖後仍為該圖。
        Assert.Equal(new[] { "a.png", "a.png" }, ps.Select(p => p.ImageHref));
        Assert.Equal(new[] { "Intro.", "After." }, ps.Select(p => p.Cue.Text));
    }

    [Fact]
    public void WithImages_LeadingNullsBackfilled_ButLaterImageStillSupersedes()
    {
        var xhtml =
            "<p>opening</p>" +                                    // 圖前開頭段 → 回填 one.png
            "<p><img src=\"one.png\"/></p><p>alpha</p>" +
            "<p><img src=\"two.png\"/></p><p>beta</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.Equal(new[] { "one.png", "one.png", "two.png" }, ps.Select(p => p.ImageHref)); // 開頭回填 one；後段仍換 two
        Assert.Equal(new[] { "opening", "alpha", "beta" }, ps.Select(p => p.Cue.Text));
    }

    [Fact]
    public void WithImages_ImageOutsideParagraph_InFigure_AssociatesWithFollowingParagraphs()
    {
        // 首頁封面圖常在 <figure>（非 <p>）內——須依文件順序全章掃 img，否則漏（simulation.epub title.xhtml 實例）。
        var xhtml =
            "<h1>Title</h1>" +
            "<figure class=\"cover-photo\"><img src=\"cover.png\" alt=\"c\"/></figure>" +
            "<p>VERSION 1.1</p><p>PRODUCER x</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.Equal(new[] { "Title", "VERSION 1.1", "PRODUCER x" }, ps.Select(p => p.Cue.Text)); // h1 亦捕捉為標題段
        Assert.True(ps[0].IsHeading);                                   // h1 標記為標題（渲染為章節標題、非對白）
        Assert.All(ps.Skip(1), p => Assert.False(p.IsHeading));         // <p> 段非標題
        Assert.All(ps, p => Assert.Equal("cover.png", p.ImageHref));   // figure 內之 img 關聯至其後段落＋回填至含標題之開頭
    }

    [Fact]
    public void WithImages_HeadingsCapturedAndMarked_InterleavedWithParagraphs()
    {
        var xhtml = "<h2>Scene Title</h2><p>Ryder: Go!</p><p>Narration.</p><h3>Sub</h3><p>More.</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.Equal(new[] { "Scene Title", "Go!", "Narration.", "Sub", "More." }, ps.Select(p => p.Cue.Text)); // 說話人前綴已剝
        Assert.Equal(new[] { true, false, false, true, false }, ps.Select(p => p.IsHeading));                   // h2/h3 為標題、<p> 非
        Assert.Equal("Ryder", ps[1].Cue.Speaker);                                                              // 對白說話人不受影響
    }

    [Fact]
    public void WithImages_MultipleImages_SwitchByReadingPosition_NotByChapter()
    {
        var xhtml =
            "<p><img src=\"one.png\"/></p><p>alpha</p><p>beta</p>" +
            "<p><img src=\"two.png\"/></p><p>gamma</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.Equal(new[] { "one.png", "one.png", "two.png" }, ps.Select(p => p.ImageHref)); // 讀到 two 之後才換（一章多圖）
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, ps.Select(p => p.Cue.Text));
    }

    [Fact]
    public void WithImages_NoImages_AllNull_CuesMatchExtractParagraphs()
    {
        var xhtml = "<p>one</p><p>two</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.All(ps, p => Assert.Null(p.ImageHref));
        Assert.Equal(                                                                 // 委派：cue 與舊 ExtractParagraphs 一致
            EbookContentReader.ExtractParagraphs(xhtml).Select(c => c.Text),
            ps.Select(p => p.Cue.Text));
    }

    [Fact]
    public void WithImages_SpeakerPrefixParagraph_StillDetectsSpeaker_AndImage()
    {
        var xhtml =
            "<p class=\"scene-image\"><img src=\"s.png\"/></p>" +
            "<p><strong class=\"speaker\">Staff:</strong> Good morning.</p>";
        var ps = EbookContentReader.ExtractParagraphsWithImages(xhtml);
        Assert.Single(ps);
        Assert.Equal("s.png", ps[0].ImageHref);
        Assert.Equal("Staff", ps[0].Cue.Speaker);                                     // 說話人沿用增量2 之行首 Name: 抽取
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

    // ---- 增量3 組合方法（smoke）：ReadContentAsync 由真實 epub 管線取段落＋依位置場景圖＋圖片位元組 ----

    [Fact]
    public async Task ReadContentAsync_RealEpub_ResolvesSceneImagesByPosition()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3WithImages(dir, "urn:uuid:img-01", "Image Book",
                new[]
                {
                    ("Scene 1", "<p class=\"scene-image\"><img src=\"images/01-first.png\"/></p><p>Alpha line.</p><p>Beta line.</p>"),
                    ("Scene 2", "<p>No image yet.</p><p class=\"scene-image\"><img src=\"images/02-second.png\"/></p><p>Gamma line.</p>"),
                },
                new[]
                {
                    ("images/01-first.png", EpubTestFixtures.SamplePng),
                    ("images/02-second.png", EpubTestFixtures.SamplePng),
                });

            var info = (await EbookReader.ParseAsync(path)).Info!;
            var content = await EbookContentReader.ReadContentAsync(path, info);

            // 兩張圖以檔名 key 收入、位元組非空。
            Assert.True(content.Images.ContainsKey("01-first.png"));
            Assert.True(content.Images.ContainsKey("02-second.png"));
            Assert.All(content.Images.Values, b => Assert.NotEmpty(b));

            Assert.Equal(2, content.Chapters.Count);
            // 章1：純圖片 <p> 不成段；其後兩段皆帶場景圖1（key＝檔名）。
            Assert.Equal(new[] { "Alpha line.", "Beta line." }, content.Chapters[0].Select(p => p.Cue.Text));
            Assert.All(content.Chapters[0], p => Assert.Equal("01-first.png", p.ImageHref));
            // 章2：圖前開頭段回填場景圖2（整個場景自開頭顯示圖）、圖後段亦帶場景圖2。
            Assert.Equal(new[] { "02-second.png", "02-second.png" }, content.Chapters[1].Select(p => p.ImageHref));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ReadContentAsync_NoImages_TextOnly_EmptyImageMap()
    {
        var dir = NewTempDir();
        try
        {
            var path = EpubTestFixtures.WriteEpub3Bodies(dir, "urn:uuid:noimg", "Plain", "A", "en",
                new[] { ("Ch1", "<p>Just text.</p>") });
            var info = (await EbookReader.ParseAsync(path)).Info!;
            var content = await EbookContentReader.ReadContentAsync(path, info);
            Assert.Empty(content.Images);                                                        // 無圖 → 空圖表
            Assert.All(content.Chapters.SelectMany(c => c), p => Assert.Null(p.ImageHref));      // 各段退純文字
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ReadContentAsync_MissingPath_ReturnsEmptyContent()
    {
        var content = await EbookContentReader.ReadContentAsync(
            Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.epub"), new EbookInfo());
        Assert.Empty(content.Chapters);
        Assert.Empty(content.Images);
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

    // ---- #262：段落與清單項混排（intTest#66–#69）。fixture 取自真實 EPUB 之結構樣態、去識別化。 ----

    /// <summary>真實教材書常見樣態：旁白／標籤用 &lt;p&gt;、對白用 &lt;ul class="exchange"&gt;&lt;li&gt;。</summary>
    private const string MixedSceneXhtml =
        "<body class=\"scene-document\"><article class=\"scene\">" +
        "<p class=\"day-label\">Day 1</p>" +
        "<h1 id=\"section-1\">08:30 Registration</h1>" +
        "<p class=\"scene-image\"><img src=\"../images/01.png\" alt=\"x\" /></p>" +
        "<ul class=\"exchange\">" +
        "<li class=\"prompt\"><strong class=\"speaker\">Staff:</strong> Good morning. May I have your name?</li>" +
        "<li class=\"response\"><strong class=\"speaker\">Me:</strong> Good morning. My name is Alex.</li>" +
        "</ul>" +
        "<p>(The staff hands you a badge.)</p>" +
        "</article></body>";

    [Fact]
    public void ExtractParagraphs_MixedPAndListItems_BothBecomeCues_InDocumentOrder()
    {
        // intTest#66：舊制「章內有 <p> 即只取 <p>」會靜默丟棄整章清單項——此處斷言對白零遺漏且保持原文順序。
        var cues = EbookContentReader.ExtractParagraphs(MixedSceneXhtml);
        Assert.Equal(new[]
        {
            "Day 1",
            "08:30 Registration",
            "Good morning. May I have your name?",   // <li> 對白（說話人前綴已抽離）
            "Good morning. My name is Alex.",
            "(The staff hands you a badge.)",
        }, cues.Select(c => c.Text));
    }

    [Fact]
    public void ExtractParagraphs_MixedChapter_HeadingFlaggedAndImageCarried()
    {
        var ps = EbookContentReader.ExtractParagraphsWithImages(MixedSceneXhtml);
        Assert.Equal(5, ps.Count);
        Assert.True(ps[1].IsHeading);                                  // <h1> 為標題段
        Assert.All(ps, p => Assert.Equal("../images/01.png", p.ImageHref)); // 首圖回填至其前之開頭段
    }

    [Fact]
    public void ExtractParagraphs_SemanticSpeakerMark_TakesPrecedence()
    {
        // intTest#67：class="speaker" 語意標記優先，不受行首正則之 ≤3 詞／≤24 字與「冒號後須空白」邊界所限。
        var cues = EbookContentReader.ExtractParagraphs(
            "<p>Intro.</p><ul class=\"exchange\">" +
            "<li><strong class=\"speaker\">Faculty:</strong> What is the aim of your study?</li>" +
            "<li><span class=\"speaker\">Visiting Research Fellow:</span>The aim is to develop an intervention.</li>" +
            "</ul>");
        Assert.Equal(new[] { null, "Faculty", "Visiting Research Fellow" }, cues.Select(c => c.Speaker));
        Assert.Equal("What is the aim of your study?", cues[1].Text);
        // 說話人逾 3 詞／逾 24 字、且冒號後無空白——行首正則抽不到，語意標記才抽得到。
        Assert.Equal("The aim is to develop an intervention.", cues[2].Text);
    }

    [Fact]
    public void ExtractParagraphs_NoSpeakerMark_StillFallsBackToInlineNamePrefix()
    {
        // 既有行首 Name: 抽取不迴歸（無語意標記之書照舊）。
        var cues = EbookContentReader.ExtractParagraphs("<p>Intro.</p><ul><li>Anna: Hello there.</li></ul>");
        Assert.Equal("Anna", cues[1].Speaker);
        Assert.Equal("Hello there.", cues[1].Text);
    }

    [Fact]
    public void ExtractParagraphs_NavigationOnlyListItems_NotCues()
    {
        // intTest#68：目錄／導覽章之 <li> 僅含單一 <a>＝導覽項，不成段、不朗讀；含連結外文字者仍成段。
        var cues = EbookContentReader.ExtractParagraphs(
            "<h1>Programme</h1><ol class=\"programme-list\">" +
            "<li><a href=\"day-1.xhtml#section-1\">Day 1</a></li>" +
            "<li><a href=\"day-2.xhtml#section-1\">Day 2</a></li>" +
            "<li>Read the <a href=\"notes.xhtml\">notes</a> before you start.</li>" +
            "</ol>");
        Assert.Equal(new[] { "Programme", "Read the notes before you start." }, cues.Select(c => c.Text));
    }

    [Fact]
    public void ExtractParagraphs_NestedBlocks_InnerOnly_NoDuplicate()
    {
        // intTest#69：<li> 內含 <p>、<ul> 巢狀 <ul> → 只取內層、同一段文字不重複計段。
        var cues = EbookContentReader.ExtractParagraphs(
            "<ul><li><p>Inner paragraph.</p></li>" +
            "<li>Outer item.<ul><li>Nested item.</li></ul></li></ul>");
        Assert.Equal(new[] { "Inner paragraph.", "Nested item." }, cues.Select(c => c.Text));
    }

    [Fact]
    public void ExtractParagraphs_DivWithTextAndEmptyP_StillFallsBackToDiv()
    {
        // 巢狀去重不得吃掉退回路徑之保障：空 <p> 不算內容區塊，故帶文字之外層 <div> 仍成段、不整章空白。
        var cues = EbookContentReader.ExtractParagraphs("<div>Text lives in a div.<p></p></div>");
        Assert.Single(cues);
        Assert.Equal("Text lives in a div.", cues[0].Text);
    }

    [Fact]
    public void ExtractParagraphs_DivWrappingList_NoP_TakesListItemsOnly()
    {
        // <div> 包 <ul>（章內無 <p>）→ div 被巢狀規則捨去、只取清單項，不重複計段。
        var cues = EbookContentReader.ExtractParagraphs(
            "<div class=\"chapter\"><ul><li>First item.</li><li>Second item.</li></ul></div>");
        Assert.Equal(new[] { "First item.", "Second item." }, cues.Select(c => c.Text));
    }
}
