using System.Collections.Generic;
using System.Linq;
using LingoIsland.Video;

namespace LingoIsland.Ebook;

/// <summary>
/// 閱讀器內段落編輯之 <b>override side-car</b>（#239）：以「章序×段序」為 key 覆蓋段落<b>完整文字</b>（含段首 <c>Name:</c> 前綴），
/// 存於書本資料夾 <c>edits.json</c>——<b>原始 <c>.epub</c> 唯讀不改</b>，閱讀／朗讀／上色／說話人一律以覆蓋後為準、可還原。
/// 覆蓋文字為「重組之完整段落」（若有說話人＝<c>Name: 正文</c>），套用時對受影響章重抽段首 <c>Name:</c> 說話人，
/// 故編輯即可補／改說話人前綴、或修正錯字。段序以同一 <c>.epub</c> 同一解析器之確定性輸出為錨（原檔不變＝段序穩定）。
/// </summary>
public sealed class EbookEdits
{
    /// <summary>逐段覆蓋清單（章序、段序、覆蓋後完整文字）。</summary>
    public List<EbookEdit> Edits { get; set; } = new();

    public bool HasAny => Edits.Count > 0;

    /// <summary>某段之覆蓋文字（無覆蓋回 null＝用原文）。</summary>
    public string? TextFor(int chapter, int paragraph)
        => Edits.FirstOrDefault(e => e.Chapter == chapter && e.Paragraph == paragraph)?.Text;

    /// <summary>設某段覆蓋文字（同 key 換置；<paramref name="text"/> 空白＝移除覆蓋、還原原文）。純資料操作、可測。</summary>
    public void Set(int chapter, int paragraph, string? text)
    {
        Edits.RemoveAll(e => e.Chapter == chapter && e.Paragraph == paragraph);
        if (!string.IsNullOrWhiteSpace(text)) { Edits.Add(new EbookEdit(chapter, paragraph, text!.Trim())); }
    }

    /// <summary>
    /// 套用 override 至整本章節段落：覆蓋受影響章各段之 cue 文字（<c>Text</c>＝覆蓋全文、<c>Speaker</c> 先清空），
    /// 再對<b>整章</b>重抽段首 <c>Name:</c> 說話人（<see cref="SubtitleParser.ExtractInlineSpeakers"/>）——未覆蓋段無前綴走 else 保留原說話人（幂等），
    /// 覆蓋段依新全文重定說話人（補前綴＝標人、去前綴＝清為旁白）。回新章節段落；<b>原 chapters 不變</b>。
    /// 純函式（同輸入同輸出、僅依確定性抽取）、可單元測試。無覆蓋即原樣回傳、零開銷。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<EbookParagraph>> Apply(IReadOnlyList<IReadOnlyList<EbookParagraph>> chapters)
    {
        if (!HasAny) { return chapters; }
        var result = new List<IReadOnlyList<EbookParagraph>>(chapters.Count);
        for (int ch = 0; ch < chapters.Count; ch++)
        {
            var src = chapters[ch];
            if (!Edits.Any(e => e.Chapter == ch)) { result.Add(src); continue; }

            // 1) 覆蓋受影響段之文字（清 Speaker，交下一步重抽）；標題段（h1–h6）不參與說話人、僅覆蓋文字。
            var overridden = new List<EbookParagraph>(src.Count);
            for (int p = 0; p < src.Count; p++)
            {
                var text = TextFor(ch, p);
                overridden.Add(text is null
                    ? src[p]
                    : src[p] with { Cue = src[p].Cue with { Text = text, Speaker = null } });
            }

            // 2) 整章重抽段首 Name: 說話人（未覆蓋段幂等保留、覆蓋段依新全文重定）。
            var recooked = SubtitleParser.ExtractInlineSpeakers(overridden.Select(x => x.Cue).ToList());
            var final = new List<EbookParagraph>(src.Count);
            for (int p = 0; p < overridden.Count; p++) { final.Add(overridden[p] with { Cue = recooked[p] }); }
            result.Add(final);
        }
        return result;
    }
}

/// <summary>一段之覆蓋紀錄（章序、章內段序、覆蓋後完整文字）。</summary>
public sealed record EbookEdit(int Chapter, int Paragraph, string Text);
