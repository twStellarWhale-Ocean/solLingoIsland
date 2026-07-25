using System.Collections.Generic;
using LingoIsland.Ebook;
using LingoIsland.Video;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// 逐段導讀之段序游標推進（[EbookPage] 內容閱讀器契約，spec#7；增量2）：<see cref="ParagraphStepper"/> 為純函式——
/// 以段 index 前進／後退（cue＝段落、無時間軸），於指定說話人暫停時跳過非勾選說話人之段、停在符合者（沿用 <see cref="PauseDecider"/> 家族）。
/// </summary>
public class ParagraphStepperTests
{
    private static SubtitleCue Para(string text, string? speaker = null) => new(text, null, speaker);

    private static IReadOnlyList<SubtitleCue> Book() => new[]
    {
        Para("旁白一。"),                 // 0 無說話人
        Para("Hi.", "Ryder"),            // 1 Ryder
        Para("Woof.", "Marshall"),       // 2 Marshall
        Para("Let's go.", "Ryder"),      // 3 Ryder
        Para("旁白二。"),                 // 4 無說話人
    };

    // ---- NextStop：不指定名單＝逐段前進 ----

    [Fact]
    public void NextStop_NoTargets_AdvancesOneParagraph()
    {
        var b = Book();
        Assert.Equal(0, ParagraphStepper.NextStop(b, -1)); // 自 -1 起→首段
        Assert.Equal(1, ParagraphStepper.NextStop(b, 0));
        Assert.Equal(4, ParagraphStepper.NextStop(b, 3));
        Assert.Equal(-1, ParagraphStepper.NextStop(b, 4)); // 章末→ -1
    }

    // ---- NextStop：指定說話人＝跳過非勾選、停在符合 ----

    [Fact]
    public void NextStop_SpecificSpeaker_SkipsNonMatching()
    {
        var b = Book();
        var ryder = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Ryder" };
        Assert.Equal(1, ParagraphStepper.NextStop(b, -1, ryder));  // 首個 Ryder 段
        Assert.Equal(3, ParagraphStepper.NextStop(b, 1, ryder));   // 跳過 Marshall(2)、旁白，停 Ryder(3)
        Assert.Equal(-1, ParagraphStepper.NextStop(b, 3, ryder));  // 其後無 Ryder→ -1
    }

    [Fact]
    public void NextStop_NoSpeakerTargetOnly_StopsAtUnattributedParagraphs()
    {
        var b = Book();
        var none = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase); // 空集合＋noSpeaker=true
        Assert.Equal(0, ParagraphStepper.NextStop(b, -1, none, noSpeaker: true)); // 旁白段 0
        Assert.Equal(4, ParagraphStepper.NextStop(b, 0, none, noSpeaker: true));  // 下一旁白段 4
        Assert.Equal(-1, ParagraphStepper.NextStop(b, 4, none, noSpeaker: true));
    }

    [Fact]
    public void NextStop_EmptyTargetsNoNoSpeaker_NeverStops()
    {
        var b = Book();
        var empty = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase); // 指定但無人＝不停
        Assert.Equal(-1, ParagraphStepper.NextStop(b, -1, empty, noSpeaker: false));
    }

    [Fact]
    public void NextStop_CombinedSpeakerParagraph_MatchesAtom()
    {
        var b = new[] { Para("旁白。"), Para("Go go!", "Ryder and Marshall"), Para("End.") };
        var ryder = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Ryder" };
        Assert.Equal(1, ParagraphStepper.NextStop(b, -1, ryder)); // 合唸句含 Ryder→停在該段
    }

    // ---- PrevStop ----

    [Fact]
    public void PrevStop_NoTargets_StepsBackOne()
    {
        var b = Book();
        Assert.Equal(3, ParagraphStepper.PrevStop(b, 4));
        Assert.Equal(0, ParagraphStepper.PrevStop(b, 1));
        Assert.Equal(-1, ParagraphStepper.PrevStop(b, 0)); // 章首
    }

    [Fact]
    public void PrevStop_SpecificSpeaker_SkipsBackToMatching()
    {
        var b = Book();
        var ryder = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Ryder" };
        Assert.Equal(3, ParagraphStepper.PrevStop(b, 4, ryder)); // 自旁白(4)往回→Ryder(3)
        Assert.Equal(1, ParagraphStepper.PrevStop(b, 3, ryder)); // 跳過 Marshall(2)→Ryder(1)
        Assert.Equal(-1, ParagraphStepper.PrevStop(b, 1, ryder));
    }

    // ---- ClampCursor：還原進度防越界 ----

    [Fact]
    public void ClampCursor_BoundsAndEmpty()
    {
        Assert.Equal(-1, ParagraphStepper.ClampCursor(0, 0));  // 空章
        Assert.Equal(0, ParagraphStepper.ClampCursor(-3, 5));
        Assert.Equal(4, ParagraphStepper.ClampCursor(99, 5)); // 逾界夾回末段
        Assert.Equal(2, ParagraphStepper.ClampCursor(2, 5));
    }

    [Fact]
    public void NextStop_NullOrEmpty_ReturnsMinusOne()
    {
        Assert.Equal(-1, ParagraphStepper.NextStop(new List<SubtitleCue>(), -1));
        Assert.Equal(-1, ParagraphStepper.PrevStop(new List<SubtitleCue>(), 3));
    }

    // ---- 連續朗讀模型（NextReadable／PauseAfterReading，#234/#251）----
    // 應有：[播放/繼續] 念全部對話、於勾選者暫停；BUG（舊）：只念勾選者、其餘全跳過。dialogueOnly 控朗讀範圍（#251）。

    [Fact]
    public void ReadingModel_ContinuousReadsAllDialogue_PausesOnlyAfterCheckedSpeaker_Issue234()
    {
        var b = Book(); // 0 旁白, 1 Ryder, 2 Marshall, 3 Ryder, 4 旁白
        var ryder = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Ryder" };

        // 念全部對話（含未勾選之 Marshall）、不因勾選跳過：對話序 1→2→3（旁白 0/4 無 Name: 跳過）
        Assert.Equal(1, ParagraphStepper.NextReadable(b, null, -1, dialogueOnly: true));
        Assert.Equal(2, ParagraphStepper.NextReadable(b, null, 1, dialogueOnly: true)); // Marshall 未勾選仍念（修 #234：舊實作會跳過）
        Assert.Equal(3, ParagraphStepper.NextReadable(b, null, 2, dialogueOnly: true));
        Assert.Equal(-1, ParagraphStepper.NextReadable(b, null, 3, dialogueOnly: true)); // 章末

        // 只於勾選者（Ryder）暫停：唸完 1、3 停；唸完 2（Marshall）不停、續念
        Assert.True(ParagraphStepper.PauseAfterReading(b, 1, ryder, false));
        Assert.False(ParagraphStepper.PauseAfterReading(b, 2, ryder, false));
        Assert.True(ParagraphStepper.PauseAfterReading(b, 3, ryder, false));
    }

    [Fact]
    public void PauseAfterReading_NoTargets_NeverPauses_ContinuousToChapterEnd()
    {
        var b = Book();
        var empty = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        // 空集合／null＝無暫停點＝連續念（勿把 null 當「每段皆停」）
        Assert.False(ParagraphStepper.PauseAfterReading(b, 1, empty, false));
        Assert.False(ParagraphStepper.PauseAfterReading(b, 1, null, false));
        Assert.False(ParagraphStepper.PauseAfterReading(b, 0, null, false));
    }

    [Fact]
    public void PauseAfterReading_NoSpeakerTarget_PausesAtUnattributed()
    {
        var b = Book();
        var empty = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        Assert.True(ParagraphStepper.PauseAfterReading(b, 0, empty, pauseNoSpeaker: true));  // 旁白段
        Assert.False(ParagraphStepper.PauseAfterReading(b, 1, empty, pauseNoSpeaker: true)); // Ryder 段（有說話人）
    }

    [Fact]
    public void PauseAfterReading_CombinedSpeakerParagraph_MatchesAtom()
    {
        var b = new[] { Para("旁白。"), Para("Go go!", "Ryder and Marshall"), Para("End.") };
        var ryder = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "Ryder" };
        Assert.True(ParagraphStepper.PauseAfterReading(b, 1, ryder, false)); // 合唸句含 Ryder→停
    }

    [Fact]
    public void NextReadable_DialogueOnly_SkipsHeadingsAndNarration()
    {
        var b = Book();
        var headings = new[] { false, true, false, false, false }; // index 1（Ryder）標記為標題
        Assert.Equal(2, ParagraphStepper.NextReadable(b, headings, -1, dialogueOnly: true)); // 跳旁白(0)、標題(1)、停 Marshall(2)
        Assert.Equal(2, ParagraphStepper.NextReadable(b, headings, 0, dialogueOnly: true));
    }

    [Fact]
    public void NextReadable_ReadAll_IncludesNarrationSkipsHeadings()
    {
        var b = Book(); // 0 旁白, 1 Ryder, 2 Marshall, 3 Ryder, 4 旁白
        var headings = new[] { false, true, false, false, false }; // index 1 標記為標題
        // 讀全部（dialogueOnly:false）：連旁白也念（0/4），仍跳標題(1)
        Assert.Equal(0, ParagraphStepper.NextReadable(b, null, -1, dialogueOnly: false));    // 旁白 0 也念
        Assert.Equal(2, ParagraphStepper.NextReadable(b, headings, 0, dialogueOnly: false)); // 跳標題(1)、停 2
        Assert.Equal(4, ParagraphStepper.NextReadable(b, null, 3, dialogueOnly: false));     // 旁白 4 也念
    }

    [Fact]
    public void NextReadable_NullOrEmpty_ReturnsMinusOne()
    {
        Assert.Equal(-1, ParagraphStepper.NextReadable(new List<SubtitleCue>(), null, -1, dialogueOnly: true));
    }
}
