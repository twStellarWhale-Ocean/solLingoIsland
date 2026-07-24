using LingoIsland.Present;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// Windows 語音服務（[techItem語音合成]，Issue #9）：語音列舉與缺失語音之容錯。
/// 不實際發聲（介面攔截精神）；僅驗建構與列舉不丟例外。
/// </summary>
public class SpeechServiceTests
{
    [Fact]
    public void InstalledVoiceNames_DoesNotThrow_ReturnsNonNull()
    {
        var voices = SpeechService.InstalledVoiceNames();
        Assert.NotNull(voices); // 空清單亦可（無安裝語音），但不得為 null 或丟例外
    }

    [Fact]
    public void Ctor_WithMissingVoiceName_FallsBack_DoesNotThrow()
    {
        // 指定不存在的語音 → 應吞例外退回系統預設、不當機（契約：語音缺失不當機）
        using var svc = new SpeechService("NoSuchVoice-xyz-9999");
        Assert.NotNull(svc);
    }

    [Fact]
    public void Ctor_WithNullVoice_UsesDefault_DoesNotThrow()
    {
        using var svc = new SpeechService(null);
        Assert.NotNull(svc);
    }

    // ---- SpeakCompleted 完成回呼（增量2 spec#8）：以假實作驗證觸發與 Cancelled 語意 ----

    /// <summary>測試用假語音服務：可主動觸發 <see cref="ISpeechService.SpeakCompleted"/>（不實際發聲、供切片2 逐段前進邏輯注入）。</summary>
    private sealed class FakeSpeech : ISpeechService
    {
        public string? LastText;
        public event EventHandler<SpeakDoneEventArgs>? SpeakCompleted;
        public void Speak(string text, string culture, bool stopPrevious = true) => LastText = text;
        public void RaiseCompleted(bool cancelled) => SpeakCompleted?.Invoke(this, new SpeakDoneEventArgs(cancelled));
    }

    [Fact]
    public void SpeakCompleted_FakeImpl_FiresOnNaturalDone_SkipsWhenCancelled()
    {
        var fake = new FakeSpeech();
        var advances = 0;                 // 模擬切片2「唸完自動前進」計數
        bool? lastCancelled = null;
        ((ISpeechService)fake).SpeakCompleted += (_, e) => { lastCancelled = e.Cancelled; if (!e.Cancelled) { advances++; } };

        fake.Speak("The first paragraph.", "en-US");
        fake.RaiseCompleted(cancelled: false);   // 自然唸完 → 應自動前進
        Assert.Equal("The first paragraph.", fake.LastText);
        Assert.False(lastCancelled);
        Assert.Equal(1, advances);

        fake.RaiseCompleted(cancelled: true);    // 被中止（切書／跳讀／暫停）→ 不前進
        Assert.True(lastCancelled);
        Assert.Equal(1, advances);               // 未再前進（避免誤讀）
    }

    [Fact]
    public void SpeakDoneEventArgs_CarriesCancelledFlag()
    {
        Assert.True(new SpeakDoneEventArgs(true).Cancelled);
        Assert.False(new SpeakDoneEventArgs(false).Cancelled);
    }

    [Fact]
    public void SpeakCompleted_RealService_SubscribeUnsubscribeDispose_NoThrow()
    {
        // 真實服務曝露事件、可訂閱/取消訂閱且 Dispose 不當機（不實際發聲）。
        var svc = new SpeechService(null);
        void Handler(object? s, SpeakDoneEventArgs e) { }
        var ex = Record.Exception(() =>
        {
            ((ISpeechService)svc).SpeakCompleted += Handler;
            ((ISpeechService)svc).SpeakCompleted -= Handler;
            svc.Dispose();
        });
        Assert.Null(ex);
    }
}
