using System;

namespace LingoIsland.Present;

/// <summary>
/// 可編輯頁面之未存變更契約（spec#11）。實作本介面之頁面即納入 [MainWindow] 之離開守衛保護——
/// 切分頁、同頁改選編輯對象、結束程式前皆先提示三選。
/// <para>
/// 守衛**不得**判斷任何具名頁面型別，一律透過本契約求值（design.md ＜II.C.(A).4＞ invariant）。
/// </para>
/// </summary>
public interface IUnsavedGuardPage
{
    /// <summary>是否有未儲存變更。比較前文字欄位一律 Trim、色值以大寫 #RRGGBB 正規化。</summary>
    bool IsDirty { get; }

    /// <summary>嘗試儲存。<c>true</c>＝已存可離開；<c>false</c>＝失敗，須留在原處（失敗訊息由實作以持續性通道呈現）。</summary>
    bool TrySave();

    /// <summary>捨棄未存變更、還原為上次儲存值。</summary>
    void RevertChanges();

    /// <summary>對話文案用之頁面顯示名（如「選項頁」「主題頁」），使既有頁之文案一字不變。</summary>
    string PageDisplayName { get; }
}

/// <summary>本頁有未存狀態且納入守衛保護，須實作 <see cref="IUnsavedGuardPage"/>。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class EditablePageAttribute : Attribute { }

/// <summary>本頁確無未存狀態，不需守衛。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoUnsavedStateAttribute : Attribute { }

/// <summary>本頁有未存狀態但本期不納入守衛；理由不得為空（design.md ＜II.C.(A).4＞ 斷言①）。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UnsavedStateOutOfScopeAttribute : Attribute
{
    public UnsavedStateOutOfScopeAttribute(string reason) => Reason = reason;

    public string Reason { get; }
}

/// <summary>標記 [MainWindow] 之離開守衛方法，供斷言② 定位受檢對象（靠方法名定位會因改名失效）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LeaveGuardAttribute : Attribute { }
