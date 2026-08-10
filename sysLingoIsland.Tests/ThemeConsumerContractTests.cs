using System;
using System.Linq;
using System.Reflection;
using UserControl = System.Windows.Controls.UserControl;
using LingoIsland.Present;
using LingoIsland.Query;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// spec#12 之契約 invariant 機檢（design.md ＜II.C.(A).4＞ 四條可執行斷言）。
/// 缺口來源＝#290：影片頁重算不完整、螢幕擷取頁從未訂閱、無任何測試抓得到。
/// </summary>
public class ThemeConsumerContractTests
{
    /// <summary>主視窗承載之頁面型別（枚舉來源同 spec#11 斷言①＝MainWindow 之頁面欄位）。</summary>
    private static Type[] HostedPageTypes() =>
        typeof(MainWindow)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.FieldType)
            .Where(t => typeof(UserControl).IsAssignableFrom(t))
            .Distinct()
            .ToArray();

    /// <summary>斷言①-a 標記完備：每個承載頁必須三態擇一標記，未標記即紅燈。</summary>
    [Fact]
    public void 每個承載頁皆已三態擇一標記主題消費()
    {
        var unmarked = HostedPageTypes()
            .Where(t => t.GetCustomAttribute<ThemeConsumerAttribute>() is null
                     && t.GetCustomAttribute<NoThemeConsumptionAttribute>() is null
                     && t.GetCustomAttribute<ThemeConsumptionOutOfScopeAttribute>() is null)
            .Select(t => t.Name)
            .ToArray();

        Assert.True(unmarked.Length == 0,
            "下列頁面未標記主題消費三態之一（spec#12 斷言①）：" + string.Join("、", unmarked));
    }

    /// <summary>斷言①-b：標 <c>[ThemeConsumptionOutOfScope]</c> 者理由不得為空。</summary>
    [Fact]
    public void 主題消費範圍外標記之理由不得為空()
    {
        var blank = HostedPageTypes()
            .Select(t => (t.Name, Attr: t.GetCustomAttribute<ThemeConsumptionOutOfScopeAttribute>()))
            .Where(x => x.Attr is not null && string.IsNullOrWhiteSpace(x.Attr!.Reason))
            .Select(x => x.Name)
            .ToArray();

        Assert.True(blank.Length == 0, "下列頁面之主題消費範圍外標記缺理由：" + string.Join("、", blank));
    }

    /// <summary>斷言①-c：標 <c>[ThemeConsumer]</c> 者必須實作一成員契約。</summary>
    [Fact]
    public void 標記為主題消費頁者皆實作契約()
    {
        var missing = HostedPageTypes()
            .Where(t => t.GetCustomAttribute<ThemeConsumerAttribute>() is not null
                     && !typeof(IThemeConsumerPage).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToArray();

        Assert.True(missing.Length == 0,
            "下列頁面標了 [ThemeConsumer] 卻未實作 IThemeConsumerPage：" + string.Join("、", missing));
    }

    /// <summary>
    /// 斷言②：宣告不得與實情相反——持有 <see cref="ThemeStore"/> 實例欄位者即讀主題資料，
    /// 不得標 <c>[NoThemeConsumption]</c>（#290 缺口② `ScreenCapturePage` 正是此型）。
    /// </summary>
    [Fact]
    public void 持有主題存放區者不得宣告不消費主題()
    {
        var lying = HostedPageTypes()
            .Where(t => t.GetCustomAttribute<NoThemeConsumptionAttribute>() is not null)
            .Where(t => t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                         .Any(f => f.FieldType == typeof(ThemeStore)))
            .Select(t => t.Name)
            .ToArray();

        Assert.True(lying.Length == 0,
            "下列頁面持有 ThemeStore 卻標 [NoThemeConsumption]（spec#12 斷言②）：" + string.Join("、", lying));
    }

    /// <summary>斷言③：派送方法（以 <c>[ThemeChangeDispatch]</c> 自我指名）恰一個，且簽章不參照任何具名頁面型別。</summary>
    [Fact]
    public void 主題變更派送方法不得參照任何具名頁面型別()
    {
        var dispatchers = typeof(MainWindow)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ThemeChangeDispatchAttribute>() is not null)
            .ToArray();

        Assert.True(dispatchers.Length == 1,
            $"MainWindow 應恰有一個 [ThemeChangeDispatch] 派送方法，實得 {dispatchers.Length} 個");

        var pageTypes = HostedPageTypes().ToHashSet();
        var dispatch = dispatchers[0];
        var referenced = dispatch.GetParameters().Select(p => p.ParameterType)
            .Append(dispatch.ReturnType)
            .Where(pageTypes.Contains)
            .Select(t => t.Name)
            .ToArray();

        Assert.True(referenced.Length == 0,
            "派送方法簽章參照了具名頁面型別（spec#12 斷言③）：" + string.Join("、", referenced));
    }

    /// <summary>斷言④：契約形狀固定為一成員，改動即為破壞性。</summary>
    [Fact]
    public void 主題消費契約恰為一成員()
    {
        var members = typeof(IThemeConsumerPage).GetMembers().Select(m => m.Name).ToArray();

        Assert.Equal(new[] { "OnThemesChanged" }, members);
    }

    /// <summary>存放區之變更通知須於寫回成功後發出（推送模型之上游前提；失敗不發，避免半成品外溢）。</summary>
    [Fact]
    public void 寫回成功才發出變更通知()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "li-theme-consumer-" + Guid.NewGuid().ToString("N"));
        var store = new ThemeStore(System.IO.Path.Combine(dir, "themes.json"), System.IO.Path.Combine(dir, "img"));
        var fired = 0;
        store.Changed += () => fired++;

        Assert.True(store.TrySave(new ThemesData(), out _));
        Assert.Equal(1, fired);

        System.IO.Directory.Delete(dir, recursive: true);
    }
}
