using System.Collections.Generic;
using LingoIsland.Ebook;
using LingoIsland.Video;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>#239：閱讀器內段落編輯 override side-car（<see cref="EbookEdits"/>）——以「章序×段序」覆蓋段落完整文字、
/// 套用時重抽段首 <c>Name:</c> 說話人、可還原；純函式（同輸入同輸出）。</summary>
public class EbookEditsTests
{
    private static EbookParagraph P(string text, string? speaker = null, string? imageHref = null, bool heading = false)
        => new(new SubtitleCue(text, null, speaker), imageHref, heading);

    private static IReadOnlyList<IReadOnlyList<EbookParagraph>> OneChapter(params EbookParagraph[] paras)
        => new[] { (IReadOnlyList<EbookParagraph>)paras };

    [Fact]
    public void Set_ThenTextFor_ReturnsOverride()
    {
        var e = new EbookEdits();
        e.Set(1, 2, "new text");
        Assert.Equal("new text", e.TextFor(1, 2));
        Assert.Null(e.TextFor(1, 3));
        Assert.True(e.HasAny);
    }

    [Fact]
    public void Set_BlankText_RemovesOverride()   // 空白＝移除覆蓋（還原原文）
    {
        var e = new EbookEdits();
        e.Set(0, 0, "x");
        e.Set(0, 0, "   ");
        Assert.Null(e.TextFor(0, 0));
        Assert.False(e.HasAny);
    }

    [Fact]
    public void Set_SameKey_Replaces()
    {
        var e = new EbookEdits();
        e.Set(0, 0, "first");
        e.Set(0, 0, "second");
        Assert.Equal("second", e.TextFor(0, 0));
        Assert.Single(e.Edits);
    }

    [Fact]
    public void Apply_NoEdits_ReturnsSameInstance()   // 零開銷、原樣回
    {
        var e = new EbookEdits();
        var chapters = OneChapter(P("Hello"));
        Assert.Same(chapters, e.Apply(chapters));
    }

    [Fact]
    public void Apply_OverridesParagraphText()
    {
        var e = new EbookEdits();
        e.Set(0, 0, "Edited line");
        var result = e.Apply(OneChapter(P("Original")));
        Assert.Equal("Edited line", result[0][0].Cue.Text);
    }

    [Fact]
    public void Apply_AddingNamePrefix_SetsSpeaker()   // 編輯補 Name: 前綴→重抽出說話人
    {
        var e = new EbookEdits();
        e.Set(0, 0, "Alice: Hi there");
        var result = e.Apply(OneChapter(P("Hi there")));   // 原本無說話人
        Assert.Equal("Alice", result[0][0].Cue.Speaker);
        Assert.Equal("Hi there", result[0][0].Cue.Text);
    }

    [Fact]
    public void Apply_RemovingNamePrefix_ClearsSpeaker()   // 編輯去前綴→說話人清為旁白
    {
        var e = new EbookEdits();
        e.Set(0, 0, "Just narration now");
        var result = e.Apply(OneChapter(P("Hi", speaker: "Alice")));   // 原本有說話人
        Assert.Null(result[0][0].Cue.Speaker);
        Assert.Equal("Just narration now", result[0][0].Cue.Text);
    }

    [Fact]
    public void Apply_UneditedParagraph_Unchanged()   // 未編輯段幂等保留（含原說話人）
    {
        var e = new EbookEdits();
        e.Set(0, 1, "Edited");
        var result = e.Apply(OneChapter(P("First", speaker: "Bob"), P("Second")));
        Assert.Equal("First", result[0][0].Cue.Text);
        Assert.Equal("Bob", result[0][0].Cue.Speaker);
        Assert.Equal("Edited", result[0][1].Cue.Text);
    }

    [Fact]
    public void Apply_PreservesHeadingAndImage()   // 編輯只換文字，IsHeading/ImageHref 保留
    {
        var e = new EbookEdits();
        e.Set(0, 0, "New heading text");
        var result = e.Apply(OneChapter(P("Old heading", imageHref: "cover.jpg", heading: true)));
        Assert.True(result[0][0].IsHeading);
        Assert.Equal("cover.jpg", result[0][0].ImageHref);
        Assert.Equal("New heading text", result[0][0].Cue.Text);
    }

    [Fact]
    public void Apply_OnlyAffectedChapterRecomputed()   // 覆蓋只影響指定章、他章原樣
    {
        var e = new EbookEdits();
        e.Set(1, 0, "Chapter two edited");
        var chapters = new[]
        {
            (IReadOnlyList<EbookParagraph>)new[] { P("Ch1 para", speaker: "Bob") },
            (IReadOnlyList<EbookParagraph>)new[] { P("Ch2 para") },
        };
        var result = e.Apply(chapters);
        Assert.Same(chapters[0], result[0]);   // 未涉及章＝同一 instance
        Assert.Equal("Chapter two edited", result[1][0].Cue.Text);
    }
}
