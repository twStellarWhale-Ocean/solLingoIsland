namespace LingoIsland.Ebook;

/// <summary>
/// 閱讀區顯示模式之段落可見性判定（#235 移除右逐段清單後，「只顯示勾選者」作用於中閱讀區）；純函式、不依賴 UI、可單元測試。
/// </summary>
public static class ReaderViewFilter
{
    /// <summary>
    /// 某段於顯示模式下是否可見：非「只顯示勾選者」模式＝恆顯；該模式下章節標題（結構脈絡）與當前段（游標所在）恆顯，其餘依該段說話人是否被勾選。
    /// </summary>
    public static bool ParaVisible(bool showSelectedMode, bool speakerChecked, bool isCurrent, bool isHeading)
        => !showSelectedMode || isHeading || isCurrent || speakerChecked;
}
