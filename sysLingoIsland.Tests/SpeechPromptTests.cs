using System.Xml.Linq;
using LingoIsland.Present;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// 朗讀合成之語音切換驗證（#252）——朗讀屬聲音、機判照不到「聽起來對不對」，
/// 故改驗 <see cref="SpeechService.BuildPrompt"/> 產出之 SSML：**換聲是否確實發生、各段文字是否正確**。
/// 實機聽感另由 USR 於 PR 確認。
/// <para>
/// 一律傳入固定之 <c>availableCultures</c>，使結果不受執行機器已裝語音影響（可重播）。
/// </para>
/// </summary>
public class SpeechPromptTests
{
    private const string En = "en-US";
    private const string Zh = "zh-TW";
    private static readonly string[] BothVoices = [En, Zh];
    private static readonly string[] EnglishOnly = [En];

    /// <summary>取 SSML 中各 <c>&lt;voice&gt;</c> 之 (xml:lang, 文字)；未包 voice 之裸文字以 lang=null 呈現。</summary>
    private static List<(string? Lang, string Text)> Voices(string ssml)
    {
        var doc = XDocument.Parse(ssml);
        var ns = doc.Root!.Name.Namespace;
        var xml = XNamespace.Xml;
        var result = new List<(string?, string)>();
        foreach (var node in doc.Root.Nodes())
        {
            switch (node)
            {
                case XElement e when e.Name == ns + "voice":
                    result.Add((e.Attribute(xml + "lang")?.Value, e.Value));
                    break;
                case XText t when !string.IsNullOrWhiteSpace(t.Value):
                    result.Add((null, t.Value));
                    break;
            }
        }
        return result;
    }

    [Fact]
    public void 中英混排_SSML逐段換聲且文字正確()
    {
        var ssml = SpeechService.BuildPrompt("Anna: 你好, Ben.", En, BothVoices).ToXml();

        var voices = Voices(ssml);
        Assert.Collection(voices,
            v => { Assert.Equal(En, v.Lang); Assert.Equal("Anna: ", v.Text); },
            v => { Assert.Equal(Zh, v.Lang); Assert.Equal("你好, ", v.Text); },
            v => { Assert.Equal(En, v.Lang); Assert.Equal("Ben.", v.Text); });
    }

    [Fact]
    public void 純英文_只產生一個voice_零回歸()
    {
        var ssml = SpeechService.BuildPrompt("Good morning, Ben.", En, BothVoices).ToXml();

        var v = Assert.Single(Voices(ssml));
        Assert.Equal(En, v.Lang);
        Assert.Equal("Good morning, Ben.", v.Text);
    }

    [Fact]
    public void 純中文_只產生一個中文voice()
    {
        var ssml = SpeechService.BuildPrompt("早安，今天天氣很好。", En, BothVoices).ToXml();

        var v = Assert.Single(Voices(ssml));
        Assert.Equal(Zh, v.Lang);
    }

    [Fact]
    public void 未裝中文語音_中文段不指定voice_以預設語音念而非略過()
    {
        var ssml = SpeechService.BuildPrompt("Hello 世界", En, EnglishOnly).ToXml();

        var voices = Voices(ssml);
        Assert.Collection(voices,
            v => { Assert.Equal(En, v.Lang); Assert.Equal("Hello ", v.Text); },
            v => { Assert.Null(v.Lang); Assert.Equal("世界", v.Text); });   // 未包 voice＝走預設語音
    }

    [Fact]
    public void 未裝中文語音_中文字仍在SSML內_不得被吞掉()
    {
        var ssml = SpeechService.BuildPrompt("Hello 世界", En, EnglishOnly).ToXml();

        Assert.Contains("世界", ssml, StringComparison.Ordinal);
    }

    [Fact]
    public void 未裝中文語音_觸發缺語音通知並帶出culture()
    {
        var seen = new List<string>();
        void Handler(object? _, string culture) => seen.Add(culture);
        SpeechService.MissingVoiceCulture += Handler;
        try
        {
            SpeechService.BuildPrompt("Hello 世界", En, EnglishOnly).ToXml();
        }
        finally
        {
            SpeechService.MissingVoiceCulture -= Handler;
        }

        Assert.Equal([Zh], seen);
    }

    [Fact]
    public void 可用語音集合為空_視為不設限_一律指定voice()
    {
        // InstalledCultures() 取不到時回空集合，此時不應把所有段都降級
        var ssml = SpeechService.BuildPrompt("Hello 世界", En, []).ToXml();

        var voices = Voices(ssml);
        Assert.Equal(2, voices.Count);
        Assert.All(voices, v => Assert.NotNull(v.Lang));
    }

    [Fact]
    public void 全段文字串接後與原文相同_不漏字()
    {
        const string text = "Anna: 你好，Ben！Let's read 第 1 章 together.";

        var ssml = SpeechService.BuildPrompt(text, En, BothVoices).ToXml();

        Assert.Equal(text, string.Concat(Voices(ssml).Select(v => v.Text)));
    }
}
