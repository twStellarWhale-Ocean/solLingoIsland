using System;
using System.Linq;
using System.Reflection;
using UserControl = System.Windows.Controls.UserControl;
using LingoIsland.Present;
using Xunit;

namespace LingoIsland.Tests;

/// <summary>
/// spec#11 之契約 invariant 機檢（design.md ＜II.C.(A).4＞ 三條可執行斷言之 ①②）。
/// 斷言③（撥回不得反噬）需 STA 與 UI 實體，落 UIA 驗收腳本與 UIA 驗收腳本。
/// </summary>
public class UnsavedGuardContractTests
{
    /// <summary>主視窗承載之頁面型別（枚舉來源＝MainWindow 之頁面欄位，非掃 assembly 全部 UserControl）。</summary>
    private static Type[] HostedPageTypes() =>
        typeof(MainWindow)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.FieldType)
            .Where(t => typeof(UserControl).IsAssignableFrom(t))
            .Distinct()
            .ToArray();

    /// <summary>斷言①-a 標記完備：每個承載頁必須三態擇一標記，未標記即紅燈。</summary>
    [Fact]
    public void 每個承載頁皆已三態擇一標記()
    {
        var unmarked = HostedPageTypes()
            .Where(t => t.GetCustomAttribute<EditablePageAttribute>() is null
                     && t.GetCustomAttribute<NoUnsavedStateAttribute>() is null
                     && t.GetCustomAttribute<UnsavedStateOutOfScopeAttribute>() is null)
            .Select(t => t.Name)
            .ToArray();

        Assert.True(unmarked.Length == 0,
            "下列頁面未標記未存狀態三態之一（spec#11 斷言①）：" + string.Join("、", unmarked));
    }

    /// <summary>斷言①-b：標 <c>[UnsavedStateOutOfScope]</c> 者理由不得為空——免以空宣告矇混。</summary>
    [Fact]
    public void 範圍外標記之理由不得為空()
    {
        var blank = HostedPageTypes()
            .Select(t => (t.Name, Attr: t.GetCustomAttribute<UnsavedStateOutOfScopeAttribute>()))
            .Where(x => x.Attr is not null && string.IsNullOrWhiteSpace(x.Attr!.Reason))
            .Select(x => x.Name)
            .ToArray();

        Assert.True(blank.Length == 0, "下列頁面之範圍外標記缺理由：" + string.Join("、", blank));
    }

    /// <summary>斷言①-c：標 <c>[EditablePage]</c> 者必須實作四成員契約。</summary>
    [Fact]
    public void 標記為可編輯頁者皆實作四成員契約()
    {
        var missing = HostedPageTypes()
            .Where(t => t.GetCustomAttribute<EditablePageAttribute>() is not null
                     && !typeof(IUnsavedGuardPage).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToArray();

        Assert.True(missing.Length == 0,
            "下列頁面標了 [EditablePage] 卻未實作 IUnsavedGuardPage：" + string.Join("、", missing));
    }

    /// <summary>斷言②：守衛方法（以 <c>[LeaveGuard]</c> 自我指名）之簽章不得參照任何具名頁面型別。</summary>
    [Fact]
    public void 守衛方法不得參照任何具名頁面型別()
    {
        var guards = typeof(MainWindow)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<LeaveGuardAttribute>() is not null)
            .ToArray();

        Assert.True(guards.Length == 1, $"MainWindow 應恰有一個 [LeaveGuard] 守衛方法，實得 {guards.Length} 個");

        var pageTypes = HostedPageTypes().ToHashSet();
        var guard = guards[0];
        var referenced = guard.GetParameters().Select(p => p.ParameterType)
            .Append(guard.ReturnType)
            .Where(pageTypes.Contains)
            .Select(t => t.Name)
            .ToArray();

        Assert.True(referenced.Length == 0,
            "守衛方法簽章參照了具名頁面型別（spec#11 斷言②）：" + string.Join("、", referenced));
    }

    /// <summary>四成員契約之形狀固定，改動即為破壞性（design.md ＜II.C.(A).1＞ ⑥）。</summary>
    [Fact]
    public void 契約恰為四成員()
    {
        var members = typeof(IUnsavedGuardPage).GetMembers()
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            new[] { "get_IsDirty", "get_PageDisplayName", "IsDirty", "PageDisplayName", "RevertChanges", "TrySave" }.OrderBy(n => n),
            members);
    }
}
