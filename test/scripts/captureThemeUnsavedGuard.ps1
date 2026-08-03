#requires -Version 7
<#
  Issue #285（主題頁未儲存離開時提醒）之機判＋證據擷取管線。

  驗四件 spec#11 訴求在實機成立（範圍＝本增量觸及之主題頁與離開守衛，非全系統）：
    (1) 主題頁改了欄位沒存就切分頁 —— 「未儲存的變更」對話框確實跳出，且以 PageDisplayName
        指名該頁（標題含「主題頁」＝第四成員契約真的生效，非退化為「目前的頁面」）。
    (2) 三選按鈕為 Yes／No／Cancel（MessageBoxButton.YesNoCancel，標籤隨 OS 語言；中文三選字面在內文）。
    (3) 按「取消」後留在主題頁 —— 分頁選取回主題頁，且**編輯內容原封不動**（撥回不得反噬：
        TabThemes.Checked 之 Reload(preferActive:true) 不得把剛保住的編輯覆蓋掉）。
    (4) 未改動時切分頁不誤觸 —— 不跳提示。

  同時產出 docs\manual-assets\theme-unsaved-guard.png 供 README 產品手冊佐證。

  桌面 UIA e2e 工法依 [modTechStackWinApp] ＜III＞；取窗／最大化／截圖沿用 uiaCommon.ps1，不重造。
#>

param(
  [string]$ExePath = "",
  [string]$OutDir  = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 驗證主題頁未儲存離開守衛（Issue #285／spec#11）於實機成立。"
Write-Host "* 產出「未儲存的變更」對話框實機截圖，供 README 產品手冊佐證。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  if (-not $ExePath) { $ExePath = Join-Path $repoRoot "sysLingoIsland\bin\Release\net9.0-windows10.0.19041.0\LingoIsland.exe" }
  if (-not $OutDir)  { $OutDir  = Join-Path $repoRoot "docs\manual-assets" }
  $appData   = Join-Path $env:APPDATA "LingoIsland"
  $backupDir = Join-Path $env:TEMP ("LingoIsland-backup-guard-" + (Get-Date -Format "yyyyMMddHHmmss"))
  $probe     = "GUARD-PROBE-" + (Get-Random -Maximum 99999)   # 打進主題描述之探針，供「內容原封不動」逐字比對
  Write-Host "* ExePath = $ExePath"
  Write-Host "* OutDir  = $OutDir"
  Write-Host "* 探針字串 = $probe"
  if (-not (Test-Path $ExePath)) { Write-Host "* [錯誤] 找不到建置產物：$ExePath（請先 dotnet build -c Release）" -ForegroundColor Red; exit 1 }
  if (-not (Test-Path $OutDir))  { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
  #endregion

  #region B.型別準備（共用定義） --------------------------------
  Write-Host "## B.型別準備（共用定義） --------------------------------" -ForegroundColor Cyan
  . "$PSScriptRoot\uiaCommon.ps1"
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$([Win32Ui]::MakeDpiAware())"
  $AE = [System.Windows.Automation.AutomationElement]
  $TS = [System.Windows.Automation.TreeScope]

  # modal 對話框會佔住受測 App 的 UI 執行緒，UIA 之 Select()／Invoke() 為同步呼叫必逾時。
  # 凡「可能觸發 modal」之點擊一律改走滑鼠事件（只送輸入、不等回應）。
  function Click-Element {
    param([System.Windows.Automation.AutomationElement]$El)
    $r = $El.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    [Win32Ui]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32Ui]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [Win32Ui]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # LEFTUP
  }
  #endregion
#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

$fails = @()
try {

  #region A.APPDATA 備份（本腳本會改主題資料，測後還原） --------------------------------
  Write-Host "## A.APPDATA 備份 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  Copy-Item -Path $appData -Destination $backupDir -Recurse -Force
  Write-Host "* 已備份 $appData → $backupDir"
  #endregion

  #region B.啟動並切至主題分頁 --------------------------------
  Write-Host "## B.啟動並切至主題分頁 --------------------------------" -ForegroundColor Cyan
  $win  = Start-AppAndGetWindow -ExePath $ExePath
  $hwnd = $win.Hwnd
  Set-WindowMaximized -Hwnd $hwnd | Out-Null
  Set-WindowForeground -Hwnd $hwnd | Out-Null
  $root = $AE::FromHandle($hwnd)

  $tabThemes = Find-ByAutomationId -Root $root -Id "TabThemes"
  if ($null -eq $tabThemes) { throw "找不到主題分頁鈕（TabThemes）" }
  # 視窗剛最大化／取得前景時，第一次點擊常被吞掉（用於啟用視窗），故重試取座標再點。
  $desc = $null
  for ($try = 0; $try -lt 5 -and $null -eq $desc; $try++) {
    Start-Sleep -Milliseconds 600
    $tabThemes = Find-ByAutomationId -Root $root -Id "TabThemes"
    Click-Element -El $tabThemes
    for ($i = 0; $i -lt 8; $i++) { Start-Sleep -Milliseconds 300; $desc = Find-ByAutomationId -Root $root -Id "DescBox"; if ($null -ne $desc) { break } }
  }
  if ($null -eq $desc) { throw "切至主題分頁後找不到主題描述欄（DescBox）——主題頁可能無任何主題可編輯" }
  $tabNotes = Find-ByAutomationId -Root $root -Id "TabNotes"
  $origDesc = $desc.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
  Write-Host "* 已切至主題分頁（原描述長度＝$($origDesc.Length)）"
  #endregion

  #region D.改一個欄位後切分頁（訴求 1、2） --------------------------------
  Write-Host "## D.改欄位後切分頁 --------------------------------" -ForegroundColor Cyan
  $desc.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($probe)
  Start-Sleep -Milliseconds 400
  Write-Host "* 已於主題描述打入探針：$probe"

  # 只點一次：modal 一旦出現，重複點擊會把它按掉。WPF MessageBox 是 Win32 對話（ClassName #32770），
  # 其 UIA Name 未必等於 caption，故以 ClassName 尋找較穩。
  $dlgCond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, "#32770")
  $dlg = $null
  for ($try = 0; $try -lt 4 -and $null -eq $dlg; $try++) {
    # 每輪起手確認仍在主題頁且探針還在（modal 未起時 DescBox 可見）；否則重打探針。
    $d = Find-ByAutomationId -Root $root -Id "DescBox"
    if ($null -ne $d) {
      $vp = $d.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
      if ($vp.Current.Value -ne $probe) { $vp.SetValue($probe) }
    }
    Start-Sleep -Milliseconds 800
    Set-WindowForeground -Hwnd $hwnd | Out-Null
    Start-Sleep -Milliseconds 500
    $tabNotes = Find-ByAutomationId -Root $root -Id "TabNotes"
    if ($null -eq $tabNotes) { continue }
    Click-Element -El $tabNotes
    for ($i = 0; $i -lt 25; $i++) {
      Start-Sleep -Milliseconds 300
      $dlg = $root.FindFirst($TS::Descendants, $dlgCond)
      if ($null -ne $dlg) { break }
    }
  }
  if ($null -eq $dlg) { throw "訴求1：改了欄位切分頁，未儲存的變更對話框未出現" }
  Write-Host "* [OK] 對話框已出現" -ForegroundColor Green

  # UIA 將 WPF MessageBox 之元件暴露為 ControlType.Pane：按鈕名為**英文** Yes／No／Cancel
  # （畫面上仍渲染為「是／否／取消」，受 MessageBoxButton.YesNoCancel 限制，design ＜IV＞ 已裁定），
  # 內文則為一個 Pane，其 Name 即完整訊息。故一律取子節點 Name 比對，不以 ControlType 篩選。
  $kids = @($dlg.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name })

  # 訴求 1：文案須以 PageDisplayName 指名該頁（第四成員契約生效之證據）
  $body = ($kids | Where-Object { $_ -match "未儲存的變更" } | Select-Object -First 1)
  if ([string]::IsNullOrEmpty($body) -or $body -notmatch "主題頁") {
    $fails += "訴求1：對話文案未以 PageDisplayName 指名『主題頁』（實得：$body）"
  } else {
    Write-Host "* [OK] 文案指名主題頁（第四成員契約生效）" -ForegroundColor Green
  }
  # 訴求 1b：三選語意名須在內文逐行列出
  foreach ($line in @("是 — 儲存並離開", "否 — 捨棄變更並離開", "取消 — 留在主題頁")) {
    if ($body -notmatch [regex]::Escape($line)) { $fails += "訴求1：內文缺三選說明「$line」" }
  }

  # 訴求 2：三選按鈕齊備
  foreach ($want in @("Yes", "No", "Cancel")) {
    if ($kids -notcontains $want) { $fails += "訴求2：三選按鈕缺「$want」（實得：$($kids -join '｜')）" }
  }
  if ($fails.Count -eq 0) { Write-Host "* [OK] 三選按鈕齊備（Yes／No／Cancel，畫面渲染為 是／否／取消）" -ForegroundColor Green }

  # 證據截圖（對話框；README 產品手冊用）
  $shot = Join-Path $OutDir "theme-unsaved-guard.png"
  $dlgHwnd = [IntPtr]$dlg.Current.NativeWindowHandle
  Save-WindowShot -Hwnd $dlgHwnd -Path $shot
  Write-Host "* 已截圖：$shot"
  #endregion

  #region E.按取消後留在原處且內容原封不動（訴求 3；撥回不得反噬） --------------------------------
  Write-Host "## E.取消後不反噬 --------------------------------" -ForegroundColor Cyan
  $cancel = $dlg.FindFirst($TS::Children,
              (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Cancel")))
  if ($null -eq $cancel) { throw "對話框找不到「取消」鈕" }
  Click-Element -El $cancel
  Start-Sleep -Milliseconds 1200

  # (a) 分頁選取回主題頁
  $themesSel = $tabThemes.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
  if (-not $themesSel) { $fails += "訴求3：按取消後分頁選取未回到主題頁" }
  else { Write-Host "* [OK] 分頁選取回到主題頁" -ForegroundColor Green }

  # (b) 編輯內容原封不動 —— 反噬的病徵就是這裡被 Reload 覆蓋掉
  $desc2 = Find-ByAutomationId -Root $root -Id "DescBox"
  $now   = if ($null -ne $desc2) { $desc2.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } else { "<找不到 DescBox>" }
  if ($now -ne $probe) { $fails += "訴求3：按取消後編輯內容被覆蓋（期望「$probe」，實得「$now」）＝撥回反噬" }
  else { Write-Host "* [OK] 編輯內容原封不動（撥回未反噬）" -ForegroundColor Green }
  #endregion

  #region F.未改動時切分頁不誤觸（訴求 4；置於最後，免主測試前來回切分頁擾動頁狀態） --------------------------------
  Write-Host "## F.未改動不誤觸 --------------------------------" -ForegroundColor Cyan
  $desc3 = Find-ByAutomationId -Root $root -Id "DescBox"
  $desc3.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($origDesc)  # 還原為載入值＝回到 clean
  Start-Sleep -Milliseconds 400
  Click-Element -El $tabNotes
  Start-Sleep -Milliseconds 1200
  $stray = $root.FindFirst($TS::Descendants,
             (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, "#32770")))
  if ($null -ne $stray) { $fails += "訴求4：未改動即切分頁卻跳出提示（誤觸）" }
  else { Write-Host "* [OK] 未改動時切分頁不跳提示" -ForegroundColor Green }
  #endregion

}
finally {
  #region F.收尾：關程式並還原 APPDATA --------------------------------
  Write-Host "## F.收尾 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  if (Test-Path $backupDir) {
    Remove-Item -Path $appData -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path $backupDir -Destination $appData -Recurse -Force
    Remove-Item -Path $backupDir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "* 已還原 $appData"
  }
  #endregion
}
#endregion

#region IV.完成條件 ================================
Write-Host "# IV.完成條件 ================================" -ForegroundColor Blue
if ($fails.Count -gt 0) {
  Write-Host "* 結果：FAIL（$($fails.Count) 項）" -ForegroundColor Red
  $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  exit 1
}
Write-Host "* 結果：PASS（訴求 1–4 全數成立）" -ForegroundColor Green
exit 0
#endregion
