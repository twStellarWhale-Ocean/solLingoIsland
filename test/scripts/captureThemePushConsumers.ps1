#requires -Version 7
<#
  Issue #290（主題變更推送之消費端缺口）之實機走查＋證據擷取管線（spec#12）。

  驗四件訴求在實機成立（範圍＝主題推送之消費端，非全系統）：
    (1) 影片頁：主題更名並儲存後，**不切頁不重開**即反映——影片清單卡片之主題標籤（VideoCardTheme）
        與主題篩選下拉（VideoThemeFilter）皆為新名。**本頁無 IsVisibleChanged 重填**（原始碼實查：
        VideoCapturePage 之 IsVisibleChanged 只做輪詢/焦點，不重填清單），故「切入即見新名」＝推送生效之實證。
    (2) 螢幕擷取頁：ShotThemeFilter 反映新名。**注意**：本頁另有 IsVisibleChanged 重填，
        故本斷言為「不回歸」證據，**無法區分推送與切頁重填**——本腳本如實標示。
    (3) 電子書頁：書櫃卡片之主題標籤（BookCardTheme）與 BookThemeFilter 皆為新名（#297；本頁亦有
        IsVisibleChanged 重填，故此處證的是「標籤依 ThemeId 即時解析、不再繪加入當下之名稱快照」，
        不區分推送與切頁重填——快照式寫法連切頁重填也修不好，兩者皆會顯示舊名）。
    (4) 單一派送點之例外隔離：派送後 App 仍存活、且各頁皆可操作（單頁擲例外不得拖垮其餘頁）。

  桌面 UIA e2e 工法依 [modTechStackWinApp] ＜III＞；取窗／最大化／截圖沿用 uiaCommon.ps1，不重造。
  %APPDATA% 起手備份、finally 還原（本腳本會改主題資料）。
#>

param(
  [string]$ExePath = "",
  [string]$OutDir  = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

#region I.主旨目的 ================================
Write-Host "# I.主旨目的 ================================" -ForegroundColor Blue
Write-Host "* 驗證主題配色變更即時套用至各消費頁（Issue #290／spec#12）於實機成立。"
#endregion

#region II.參考準備 ================================
Write-Host "# II.參考準備 ================================" -ForegroundColor Blue

  #region A.參數準備 --------------------------------
  Write-Host "## A.參數準備 --------------------------------" -ForegroundColor Cyan
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  if (-not $ExePath) { $ExePath = Join-Path $repoRoot "sysLingoIsland\bin\Release\net9.0-windows10.0.19041.0\LingoIsland.exe" }
  if (-not $OutDir)  { $OutDir  = Join-Path $env:TEMP "LingoIsland-themepush-evidence" }
  $appData   = Join-Path $env:APPDATA "LingoIsland"
  $backupDir = Join-Path $env:TEMP ("LingoIsland-backup-themepush-" + (Get-Date -Format "yyyyMMddHHmmss"))
  $probe     = "PUSH-PROBE-" + (Get-Random -Maximum 99999)
  Write-Host "* ExePath = $ExePath"
  Write-Host "* OutDir  = $OutDir"
  Write-Host "* 探針主題名 = $probe"
  if (-not (Test-Path $ExePath)) { Write-Host "* [錯誤] 找不到建置產物：$ExePath（請先 dotnet build -c Release）" -ForegroundColor Red; exit 1 }
  if (-not (Test-Path $OutDir))  { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
  #endregion

  #region B.型別準備（共用定義） --------------------------------
  Write-Host "## B.型別準備（共用定義） --------------------------------" -ForegroundColor Cyan
  . "$PSScriptRoot\uiaCommon.ps1"
  Write-Host "* DPI 一致化（PER_MONITOR_AWARE_V2）＝$([Win32Ui]::MakeDpiAware())"
  $AE = [System.Windows.Automation.AutomationElement]
  $TS = [System.Windows.Automation.TreeScope]

  function Click-Element {
    param([System.Windows.Automation.AutomationElement]$El)
    $r = $El.Current.BoundingRectangle
    $x = [int]($r.X + $r.Width / 2); $y = [int]($r.Y + $r.Height / 2)
    [Win32Ui]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32Ui]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [Win32Ui]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # LEFTUP
    Start-Sleep -Milliseconds 400
  }

  # ComboBox 之項目於未展開時不在 UIA 樹——以 ExpandCollapsePattern 展開後列名再收合。
  function Get-ComboItems {
    param([System.Windows.Automation.AutomationElement]$Combo)
    $ec = $Combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand(); Start-Sleep -Milliseconds 500
    # ComboBoxItem 之 Name 為型別字串（無 DisplayMemberPath），故取其下 Text 元素之 Name
    $items = @($Combo.FindAll($TS::Descendants,
      (New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) |
      ForEach-Object { $_.Current.Name })
    $ec.Collapse(); Start-Sleep -Milliseconds 300
    return $items
  }

  function Find-AllByAutomationId {
    param([System.Windows.Automation.AutomationElement]$Root, [string]$Id)
    @($Root.FindAll($TS::Descendants,
      (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $Id))) |
      ForEach-Object { $_.Current.Name })
  }

  function Switch-Tab {
    param([System.Windows.Automation.AutomationElement]$Root, [string]$TabId, [IntPtr]$Hwnd)
    for ($i = 0; $i -lt 5; $i++) {
      Set-WindowForeground -Hwnd $Hwnd | Out-Null
      $tab = Find-ByAutomationId -Root $Root -Id $TabId
      if ($null -eq $tab) { Start-Sleep -Milliseconds 500; continue }
      Click-Element -El $tab
      Start-Sleep -Milliseconds 900
      $sel = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
      if ($sel) { return $true }
    }
    return $false
  }
  #endregion
#endregion

#region III.內容程序 ================================
Write-Host "# III.內容程序 ================================" -ForegroundColor Blue

$fails = @()
$notes = @()
try {

  #region A.APPDATA 備份 --------------------------------
  Write-Host "## A.APPDATA 備份 --------------------------------" -ForegroundColor Cyan
  Get-Process -Name "LingoIsland" -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) }
  Start-Sleep -Milliseconds 500
  Copy-Item -Path $appData -Destination $backupDir -Recurse -Force
  Write-Host "* 已備份 $appData → $backupDir"
  #endregion

  #region B.前置：先看影片頁現況（取得改名前之基準） --------------------------------
  Write-Host "## B.啟動並取影片頁基準 --------------------------------" -ForegroundColor Cyan
  $win  = Start-AppAndGetWindow -ExePath $ExePath
  $hwnd = $win.Hwnd
  Set-WindowMaximized -Hwnd $hwnd | Out-Null
  Set-WindowForeground -Hwnd $hwnd | Out-Null
  $root = $AE::FromHandle($hwnd)

  if (-not (Switch-Tab -Root $root -TabId "TabVideo" -Hwnd $hwnd)) { throw "切不到影片分頁（TabVideo）" }
  Start-Sleep -Milliseconds 800
  $beforeCards = Find-AllByAutomationId -Root $root -Id "VideoCardTheme"
  Write-Host "* 影片清單卡片主題標籤（改名前，$($beforeCards.Count) 張）：$($beforeCards -join '｜')"

  # #297：書櫃卡片標籤為同型寫法（同樣繪加入當下之名稱快照），一併取基準
  if (-not (Switch-Tab -Root $root -TabId "TabEbook" -Hwnd $hwnd)) { throw "切不到電子書分頁（TabEbook）" }
  Start-Sleep -Milliseconds 800
  $beforeBooks = Find-AllByAutomationId -Root $root -Id "BookCardTheme"
  Write-Host "* 書櫃卡片主題標籤（改名前，$($beforeBooks.Count) 張）：$($beforeBooks -join '｜')"
  #endregion

  #region C.主題頁改名並儲存 --------------------------------
  Write-Host "## C.主題頁改名並儲存 --------------------------------" -ForegroundColor Cyan
  if (-not (Switch-Tab -Root $root -TabId "TabThemes" -Hwnd $hwnd)) { throw "切不到主題分頁（TabThemes）" }

  # 一次改名＝選取該主題 → 改 NameBox → 存 → 斷言清單已反映（未落地即前置狀態不成立，後續全是假結果）。
  # #297 起兩櫃各需一次改名（影片與書櫃未必有共同主題），故抽為函式、不複製貼上。
  function Invoke-ThemeRename {
    param([string]$Target, [string]$NewName)
    if ($null -ne $Target) {
      $listBox = Find-ByAutomationId -Root $root -Id "List"
      $hit = @($listBox.FindAll($TS::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $Target)))) |
               Select-Object -First 1
      if ($null -ne $hit) {
        Click-Element -El $hit
        Start-Sleep -Milliseconds 800
        Write-Host "* 已選取受測主題「$Target」"
      } else {
        Write-Host "* [註] 主題清單找不到「$Target」，改以目前選取之主題受測" -ForegroundColor Yellow
      }
    }
    $nameBox = $null
    for ($i = 0; $i -lt 10 -and $null -eq $nameBox; $i++) { Start-Sleep -Milliseconds 400; $nameBox = Find-ByAutomationId -Root $root -Id "NameBox" }
    if ($null -eq $nameBox) { throw "找不到主題名稱欄（NameBox）——主題頁可能無任何主題可編輯" }
    $orig = $nameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ([string]::IsNullOrWhiteSpace($orig)) { throw "主題名稱欄為空——測試前置狀態不成立（空輸入之比對不算證據）" }
    Write-Host "* 受測主題原名＝「$orig」（長度 $($orig.Length)）"

    $nameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($NewName)
    Start-Sleep -Milliseconds 400
    $saveBtn = Find-ByAutomationId -Root $root -Id "SaveBtn"
    if ($null -eq $saveBtn) { throw "找不到儲存鈕（SaveBtn）" }
    # 儲存成功不彈 modal，故以 InvokePattern 直呼（滑鼠座標易因視窗被 ui-state.json 還原尺寸而落空）
    $saveBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1500
    # ListBoxItem 之 Name 為型別字串（無 DisplayMemberPath），故取其下 Text 元素之 Name
    $listNames = @((Find-ByAutomationId -Root $root -Id "List").FindAll($TS::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                      $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) |
                    ForEach-Object { $_.Current.Name })
    Write-Host "* 主題清單項：$($listNames -join '｜')"
    if (-not ($listNames -join '｜').Contains($NewName)) {
      throw "儲存未落地：主題清單仍未見「$NewName」（實得：$($listNames -join '｜')）——測試前置狀態不成立，本輪不得判定產品"
    }
    Write-Host "* 已改名為「$NewName」並儲存（清單已反映）"
    return $orig
  }

  # 受測主題刻意選「清單卡片實際所屬」者，否則改名後卡片標籤根本不該變、缺口驗不到。
  # #297：兩櫃皆須驗——優先選影片與書櫃**共有**之主題（一次改名兩櫃同驗）；無交集時各改各的，
  # 否則書櫃那半永遠落在「驗不到」，等於本件第二處修法無證據（上一輪即敗於此類漏驗）。
  $bookThemes = @($beforeBooks | Where-Object { $_ -ne "未分類" })   # 書卡無主題時顯「未分類」，非真主題名
  $target = $null
  foreach ($n in $beforeCards) { if ($bookThemes -contains $n) { $target = $n; break } }
  if ($null -eq $target -and $beforeCards.Count -gt 0) { $target = $beforeCards[0] }
  if ($null -eq $target -and $bookThemes.Count -gt 0)  { $target = $bookThemes[0] }
  Write-Host "* 受測主題選定＝「$target」（影片卡 $($beforeCards.Count) 張／書卡具名 $($bookThemes.Count) 張）"
  $origName = Invoke-ThemeRename -Target $target -NewName $probe

  # 書櫃無卡片屬 $target 時，另取一個書櫃實際所屬之主題再改一次（書櫃斷言改用這一組）
  $bookProbe = $probe
  $bookOrig  = $origName
  $bookTarget = @($bookThemes | Where-Object { $_ -ne $target }) | Select-Object -First 1
  if (-not ($bookThemes -contains $target) -and $null -ne $bookTarget) {
    $bookProbe = "PUSH-PROBE-BOOK-" + (Get-Random -Maximum 99999)
    Write-Host "* 書櫃無卡片屬「$target」，另改「$bookTarget」→「$bookProbe」以驗書櫃卡片標籤（#297 缺口②）"
    $bookOrig = Invoke-ThemeRename -Target $bookTarget -NewName $bookProbe
  }
  Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "01-theme-renamed-saved.png")
  #endregion

  #region D.訴求1：影片頁即時反映（本頁無 IsVisibleChanged 重填→為推送之實證） --------------------------------
  Write-Host "## D.訴求1 影片頁 --------------------------------" -ForegroundColor Cyan
  if (-not (Switch-Tab -Root $root -TabId "TabVideo" -Hwnd $hwnd)) { throw "切不到影片分頁" }
  Start-Sleep -Milliseconds 900
  $afterCards = Find-AllByAutomationId -Root $root -Id "VideoCardTheme"
  Write-Host "* 影片清單卡片主題標籤（改名後，$($afterCards.Count) 張）：$($afterCards -join '｜')"
  if ($afterCards.Count -eq 0) {
    $notes += "訴求1a：影片清單為空（0 張卡片），卡片主題標籤**驗不到**——空輸入之比對不算證據（GATE ＜4節＞）"
  } elseif ($beforeCards -contains $origName -and -not ($afterCards -contains $probe)) {
    $fails += "訴求1a：影片清單卡片主題標籤未刷新（仍見舊名「$origName」，未見「$probe」）＝#290 缺口① 未修復"
  } elseif ($afterCards -contains $probe) {
    Write-Host "* [OK] 影片清單卡片主題標籤已刷新為新名（RefreshVideoList 生效）" -ForegroundColor Green
  } else {
    $notes += "訴求1a：影片清單有卡片但無一屬受測主題（改名前標籤：$($beforeCards -join '｜')）——**驗不到**"
  }

  $vf = Find-ByAutomationId -Root $root -Id "VideoThemeFilter"
  if ($null -eq $vf) { $fails += "訴求1b：找不到影片頁主題篩選下拉（VideoThemeFilter）" }
  else {
    $items = Get-ComboItems -Combo $vf
    Write-Host "* VideoThemeFilter 項目：$($items -join '｜')"
    if ($items.Count -eq 0) { $fails += "訴求1b：VideoThemeFilter 展開後 0 項（空輸入，不計通過）" }
    elseif ($items -notcontains $probe) { $fails += "訴求1b：VideoThemeFilter 未見新主題名「$probe」（實得：$($items -join '｜')）" }
    else { Write-Host "* [OK] VideoThemeFilter 已反映新主題名" -ForegroundColor Green }
  }
  Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "02-video-page-after.png")
  #endregion

  #region E.訴求2：螢幕擷取頁 --------------------------------
  Write-Host "## E.訴求2 螢幕擷取頁 --------------------------------" -ForegroundColor Cyan
  if (-not (Switch-Tab -Root $root -TabId "TabCapture" -Hwnd $hwnd)) { throw "切不到擷取分頁（TabCapture）" }
  Start-Sleep -Milliseconds 900
  $sf = Find-ByAutomationId -Root $root -Id "ShotThemeFilter"
  if ($null -eq $sf) { $fails += "訴求2：找不到螢幕擷取頁主題篩選下拉（ShotThemeFilter）" }
  else {
    $items = Get-ComboItems -Combo $sf
    Write-Host "* ShotThemeFilter 項目：$($items -join '｜')"
    if ($items.Count -eq 0) { $fails += "訴求2：ShotThemeFilter 展開後 0 項（空輸入，不計通過）" }
    elseif ($items -notcontains $probe) { $fails += "訴求2：ShotThemeFilter 未見新主題名「$probe」（實得：$($items -join '｜')）" }
    else { Write-Host "* [OK] ShotThemeFilter 已反映新主題名" -ForegroundColor Green }
  }
  $notes += "訴求2：本頁另有 IsVisibleChanged 重填（ScreenCapturePage.xaml.cs:88），本斷言**無法區分**推送與切頁重填——只證不回歸"
  Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "03-screencapture-page-after.png")
  #endregion

  #region F.訴求3：電子書頁 --------------------------------
  Write-Host "## F.訴求3 電子書頁 --------------------------------" -ForegroundColor Cyan
  if (-not (Switch-Tab -Root $root -TabId "TabEbook" -Hwnd $hwnd)) { throw "切不到電子書分頁（TabEbook）" }
  Start-Sleep -Milliseconds 900
  # #297：書櫃卡片主題標籤（同型缺陷——繪加入當下之名稱快照，更名後不跟著變）
  $afterBooks = Find-AllByAutomationId -Root $root -Id "BookCardTheme"
  Write-Host "* 書櫃卡片主題標籤（改名後，$($afterBooks.Count) 張）：$($afterBooks -join '｜')"
  if ($afterBooks.Count -eq 0) {
    $notes += "訴求3a：書櫃為空（0 張卡片），卡片主題標籤**驗不到**——空輸入之比對不算證據（GATE ＜4節＞）"
  } elseif ($beforeBooks -contains $bookOrig -and -not ($afterBooks -contains $bookProbe)) {
    $fails += "訴求3a：書櫃卡片主題標籤未刷新（仍見舊名「$bookOrig」，未見「$bookProbe」）＝#297 缺口② 未修復"
  } elseif ($afterBooks -contains $bookProbe) {
    Write-Host "* [OK] 書櫃卡片主題標籤已刷新為新名（依 ThemeId 即時解析生效）" -ForegroundColor Green
  } else {
    $notes += "訴求3a：書櫃有卡片但無一屬受測主題（改名前標籤：$($beforeBooks -join '｜')）——**驗不到**"
  }

  $bf = Find-ByAutomationId -Root $root -Id "BookThemeFilter"
  if ($null -eq $bf) { $fails += "訴求3：找不到電子書頁主題篩選下拉（BookThemeFilter）" }
  else {
    $items = Get-ComboItems -Combo $bf
    Write-Host "* BookThemeFilter 項目：$($items -join '｜')"
    if ($items.Count -eq 0) { $fails += "訴求3：BookThemeFilter 展開後 0 項（空輸入，不計通過）" }
    elseif ($items -notcontains $probe) { $fails += "訴求3：BookThemeFilter 未見新主題名「$probe」（實得：$($items -join '｜')）" }
    else { Write-Host "* [OK] BookThemeFilter 已反映新主題名" -ForegroundColor Green }
  }
  $notes += "訴求3：本頁另有 IsVisibleChanged 重填（EbookPage.xaml.cs:146），同訴求2 之界線"
  Save-WindowShot -Hwnd $hwnd -Path (Join-Path $OutDir "04-ebook-page-after.png")
  #endregion

  #region G.訴求4：派送後 App 存活且各頁可操作（例外隔離之實地佐證） --------------------------------
  Write-Host "## G.訴求4 派送後存活 --------------------------------" -ForegroundColor Cyan
  $alive = Get-Process -Id $win.ProcessId -ErrorAction SilentlyContinue
  if ($null -eq $alive) { $fails += "訴求4：派送後 App 行程已不存在（派送擲例外拖垮全頁）" }
  else { Write-Host "* [OK] 派送後 App 存活（pid=$($win.ProcessId)）" -ForegroundColor Green }
  if (-not (Switch-Tab -Root $root -TabId "TabNotes" -Hwnd $hwnd)) { $fails += "訴求4：派送後切不回筆記分頁（UI 已失能）" }
  else { Write-Host "* [OK] 派送後各分頁仍可操作" -ForegroundColor Green }
  #endregion

}
finally {
  #region H.收尾：關程式並還原 APPDATA --------------------------------
  Write-Host "## H.收尾 --------------------------------" -ForegroundColor Cyan
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
$notes | ForEach-Object { Write-Host "  [註] $_" -ForegroundColor Yellow }
if ($fails.Count -gt 0) {
  Write-Host "* 結果：FAIL（$($fails.Count) 項）" -ForegroundColor Red
  $fails | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  exit 1
}
Write-Host "* 結果：PASS（訴求 1–4 於可驗範圍內全數成立；驗不到者見上方註記）" -ForegroundColor Green
exit 0
#endregion
