using LingoIsland.Present;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// 朗讀語言切段器之全分支驗證（#252）。
/// 重點在兩條 invariant：**零回歸**（純單一 script 之文本恆切成一段、與改版前等價）與
/// **不碎片化**（ASCII 標點與空白跟隨前一段，否則每片換聲會導致頓挫）。
/// </summary>
public class SpeechSegmenterTests
{
    private const string En = "en-US";
    private const string Zh = "zh-TW";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void 空白或null_回空清單(string? text)
    {
        Assert.Empty(SpeechSegmenter.Split(text, En));
    }

    [Fact]
    public void 純英文_恆切成一段且為預設語言_零回歸()
    {
        var segs = SpeechSegmenter.Split("Good morning, Ben. How are you today?", En);

        var seg = Assert.Single(segs);
        Assert.Equal("Good morning, Ben. How are you today?", seg.Text);
        Assert.Equal(En, seg.Culture);
    }

    [Fact]
    public void 純中文_恆切成一段且為中文語言()
    {
        var segs = SpeechSegmenter.Split("早安，今天天氣很好。", En);

        var seg = Assert.Single(segs);
        Assert.Equal("早安，今天天氣很好。", seg.Text);
        Assert.Equal(Zh, seg.Culture);
    }

    [Fact]
    public void 中英混排_依script切段且逐段換聲()
    {
        var segs = SpeechSegmenter.Split("Hello 世界 world", En);

        Assert.Collection(segs,
            s => { Assert.Equal(En, s.Culture); Assert.Equal("Hello ", s.Text); },
            s => { Assert.Equal(Zh, s.Culture); Assert.Equal("世界 ", s.Text); },
            s => { Assert.Equal(En, s.Culture); Assert.Equal("world", s.Text); });
    }

    [Fact]
    public void 說話人前綴之對白_不因冒號與逗號而碎片化()
    {
        // 實際電子書段落之典型形狀；若標點各自成段，會切出七八片、每片換聲導致頓挫
        var segs = SpeechSegmenter.Split("Anna: 你好, Ben.", En);

        Assert.Collection(segs,
            s => { Assert.Equal(En, s.Culture); Assert.Equal("Anna: ", s.Text); },
            s => { Assert.Equal(Zh, s.Culture); Assert.Equal("你好, ", s.Text); },
            s => { Assert.Equal(En, s.Culture); Assert.Equal("Ben.", s.Text); });
        Assert.Equal(3, segs.Count);
    }

    [Fact]
    public void 全形標點與CJK標點_歸中文段()
    {
        var segs = SpeechSegmenter.Split("他說：「這是測試」。", En);

        var seg = Assert.Single(segs);
        Assert.Equal(Zh, seg.Culture);
    }

    [Fact]
    public void 數字與ASCII標點_跟隨前一段_不自成一段()
    {
        var segs = SpeechSegmenter.Split("第 3 章 Chapter 3", En);

        Assert.Collection(segs,
            s => { Assert.Equal(Zh, s.Culture); Assert.Equal("第 3 章 ", s.Text); },
            s => { Assert.Equal(En, s.Culture); Assert.Equal("Chapter 3", s.Text); });
    }

    [Fact]
    public void 以標點開頭_中性字元併入首個有script之段()
    {
        var segs = SpeechSegmenter.Split("— Hello", En);

        var seg = Assert.Single(segs);
        Assert.Equal(En, seg.Culture);
        Assert.Equal("— Hello", seg.Text);
    }

    [Fact]
    public void 僅數字或標點_無script可判_歸預設語言()
    {
        var segs = SpeechSegmenter.Split("123 456.", En);

        var seg = Assert.Single(segs);
        Assert.Equal(En, seg.Culture);
    }

    [Fact]
    public void 連續多次切換_每次換script皆切段()
    {
        var segs = SpeechSegmenter.Split("A中B文C", En);

        Assert.Equal(5, segs.Count);
        Assert.Equal(new[] { En, Zh, En, Zh, En }, segs.Select(s => s.Culture));
    }

    [Fact]
    public void 切段結果串接後_與原文逐字相同_不漏字不重複()
    {
        const string text = "Anna: 你好，Ben！Let's read 第 1 章 together.";

        var joined = string.Concat(SpeechSegmenter.Split(text, En).Select(s => s.Text));

        Assert.Equal(text, joined);
    }

    [Fact]
    public void 可指定其他CJK語言_不寫死zh_TW()
    {
        var segs = SpeechSegmenter.Split("測試", En, "zh-CN");

        Assert.Equal("zh-CN", Assert.Single(segs).Culture);
    }

    [Fact]
    public void 假名不納入CJK_歸預設語言_本階段不支援日語語音()
    {
        // 明確取捨：納入假名會把日文段落誤指給中文語音，比用預設語音念更糟
        var segs = SpeechSegmenter.Split("ひらがな", En);

        Assert.Equal(En, Assert.Single(segs).Culture);
    }
}
