using System;

namespace LingoIsland.Present;

/// <summary>
/// 主題變更之消費頁契約（spec#12）。實作本介面之頁面即納入 [MainWindow] 之主題變更派送——
/// 主題存放區寫回成功後，由主視窗統一通知各承載頁重算主題衍生呈現。
/// <para>
/// **消費頁不得自行訂閱 <c>ThemeStore.Changed</c>**：訂閱寫在各頁自己的建構式時，新頁面漏訂閱
/// 不會報錯、只會靜默顯示舊配色（與 spec#11 所修之守衛同形）。訂閱與派送只有主視窗一個落點。
/// </para>
/// </summary>
public interface IThemeConsumerPage
{
    /// <summary>
    /// 主題資料已變更：重算本頁**全部**主題衍生呈現——說話人字型色、主題篩選下拉、清單卡片之主題標籤。
    /// <para>
    /// 契約刻意只有一個成員、不拆為配色／篩選／清單三個：拆了就會出現「實作了兩個、漏了第三個」
    /// （#290 缺口①＝影片頁有訂閱卻漏了清單重整）。呼叫端保證於 UI 執行緒呼叫。
    /// </para>
    /// </summary>
    void OnThemesChanged();
}

/// <summary>本頁呈現主題衍生資訊且納入主題變更推送，須實作 <see cref="IThemeConsumerPage"/>。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ThemeConsumerAttribute : Attribute { }

/// <summary>本頁確不呈現任何主題衍生資訊，不需推送。**持有 ThemeStore 欄位者不得用本標記**（斷言②）。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoThemeConsumptionAttribute : Attribute { }

/// <summary>本頁呈現主題衍生資訊但不納入推送；理由不得為空（design.md ＜II.C.(A).4＞ 斷言①）。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ThemeConsumptionOutOfScopeAttribute : Attribute
{
    public ThemeConsumptionOutOfScopeAttribute(string reason) => Reason = reason;

    public string Reason { get; }
}

/// <summary>標記 [MainWindow] 之主題變更派送方法，供斷言③ 定位受檢對象（靠方法名定位會因改名失效）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ThemeChangeDispatchAttribute : Attribute { }
