#requires -Version 7
<#
  桌面 UIA e2e／證據擷取之共用定義（[modTechStackWinApp] ＜III＞）。
  本檔只定義型別與函數、無副作用，供各 intTest 腳本 dot-source：. "$PSScriptRoot\uiaCommon.ps1"
#>

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms

if (-not ("Win32Ui" -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class Win32Ui
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);
    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] public static extern uint GetWindowPid(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, KEYUP = 0x0002;

    // DPI 一致化（必須早於任何座標取用）：本行程若為 DPI-unaware，GetWindowRect／SetCursorPos 會取得
    // 虛擬化（縮放後）座標，而 UIA BoundingRectangle 恆為實體像素——兩者對不上會使點擊落空、截圖失真。
    public static bool MakeDpiAware() { return SetProcessDpiAwarenessContext(new IntPtr(-4)); } // PER_MONITOR_AWARE_V2

    // 前景鎖繞道：背景行程呼叫 SetForegroundWindow 常被 Windows 前景鎖拒絕
    // （實撞：前景由 GameInputSvc 之隱形 GameInputServiceWindow 持有）。
    public static void ForceForeground(IntPtr hWnd)
    {
        ShowWindow(hWnd, 9); // SW_RESTORE
        keybd_event(0x12, 0, 0, IntPtr.Zero);        // ALT down
        keybd_event(0x12, 0, KEYUP, IntPtr.Zero);    // ALT up
        uint fgTid = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        uint myTid = GetCurrentThreadId();
        bool attached = (fgTid != myTid) && AttachThreadInput(myTid, fgTid, true);
        BringWindowToTop(hWnd);
        SwitchToThisWindow(hWnd, true);
        SetForegroundWindow(hWnd);
        if (attached) { AttachThreadInput(myTid, fgTid, false); }
    }

    public static string WindowTitle(IntPtr hWnd)
    {
        var sb = new System.Text.StringBuilder(260);
        GetWindowTextW(hWnd, sb, 260);
        return sb.ToString();
    }

    public static uint PidOf(IntPtr hWnd) { uint pid; GetWindowPid(hWnd, out pid); return pid; }

    // 命中斷言（取代「必須是前景視窗」）：直接問 OS「螢幕上這個點是誰的視窗」——
    // 點若不屬受測行程，代表被他窗（Topmost 覆蓋卡等）擋住，點下去即假通過。
    public static uint PidAtPoint(int x, int y)
    {
        POINT p; p.X = x; p.Y = y;
        return PidOf(WindowFromPoint(p));
    }

    public static void DragVertical(int x, int yFrom, int yTo)
    {
        SetCursorPos(x, yFrom);
        System.Threading.Thread.Sleep(120);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(120);
        int step = yTo > yFrom ? 8 : -8;
        for (int y = yFrom; (step > 0 ? y < yTo : y > yTo); y += step)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(12);
        }
        SetCursorPos(x, yTo);
        System.Threading.Thread.Sleep(150);
        mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(250);
    }

    public static void DoubleClick(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero); mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero); mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(400);
    }

    /// <summary>送一次按鍵（虛擬鍵碼）。gapMs＝按下與放開之間隔。</summary>
    public static void KeyTap(byte vk, int gapMs)
    {
        keybd_event(vk, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(gapMs);
        keybd_event(vk, 0, KEYUP, IntPtr.Zero);
    }
}
"@
}

# 截圖：PrintWindow 直取視窗表面（不受 Z 序影響），GDI+ 操作留在 PowerShell 端免 C# 參照相依
function Save-WindowShot {
  param([IntPtr]$Hwnd, [string]$Path)
  $r = New-Object Win32Ui+RECT
  [Win32Ui]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  $full = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($full)
  $hdc = $g.GetHdc()
  [Win32Ui]::PrintWindow($Hwnd, $hdc, 0x2) | Out-Null   # PW_RENDERFULLCONTENT
  $g.ReleaseHdc($hdc); $g.Dispose()
  # DWM 隱形邊框內縮（左右各 7px、上 1px、下 7px）：畫布嚴格＝視窗裁切區、不留邊緣 padding
  $crop = New-Object System.Drawing.Rectangle(7, 1, [Math]::Max(1, $w - 14), [Math]::Max(1, $h - 8))
  $out = $full.Clone($crop, $full.PixelFormat)
  $out.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
  $out.Dispose(); $full.Dispose()
}

# 依 AutomationId 於指定根元素下尋一個元素（Collapsed 元素不在 UIA 樹，找不到即代表不可見）
function Find-ByAutomationId {
  param([System.Windows.Automation.AutomationElement]$Root, [string]$Id)
  $cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
  $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# 啟動 App 並取得受測主視窗（Issue #270）。
# 不得以 Start-Process -PassThru 回傳之行程為錨：Velopack 打包成品之 LingoIsland.exe 是外殼，
# 解析安裝路徑後另起真正的 app 行程、自身即退出（HasExited=True、MainWindowHandle 恆 0），
# 以其為錨恆逾時失敗。改輪詢「行程名相符且已有主視窗」者，再自該視窗反查 pid 供命中斷言；
# dev build 亦相容（該情境輪詢到的就是自己啟的那顆）。
# 前置：呼叫端須先殺光同名既有行程（見各腳本 %APPDATA% 備份段），否則可能取到不相干的實例。
function Start-AppAndGetWindow {
  param(
    [Parameter(Mandatory)][string]$ExePath,
    [string]$ProcessName = "LingoIsland",
    [int]$TimeoutSec = 30
  )
  Start-Process -FilePath $ExePath | Out-Null
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
           Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
           Select-Object -First 1
    if ($null -ne $p) {
      $hwnd = $p.MainWindowHandle
      return [pscustomobject]@{ Hwnd = $hwnd; ProcessId = [Win32Ui]::PidOf($hwnd) }
    }
  }
  throw "主視窗未出現（逾時 $TimeoutSec 秒）：找不到行程名「$ProcessName」且具主視窗者。ExePath=$ExePath"
}

# 最大化視窗並回傳其 rect（Issue #270）。
# App 會於載入／切換版面時套用 ui-state.json 保存之視窗尺寸，可能在測試「進行中」把已最大化的視窗
# 還原成小尺寸——實撞：還原成 600x460 後，閱讀器中欄之控制列由 2 列換行成 5 列、吃掉閱讀區高度，
# 於是量出「場景圖塊縮小、閱讀區卻同步縮小」這種不可能的結果（假 FAIL，病徵與 #265 之真缺陷同形）。
# 故量測基準與拖曳等關鍵步驟前都應呼叫本函數確認，尺寸有變即重取基準。
function Set-WindowMaximized {
  param([IntPtr]$Hwnd, [int]$MinHeight = 900, [int]$Tries = 6)
  $r = New-Object Win32Ui+RECT
  for ($i = 0; $i -lt $Tries; $i++) {
    [Win32Ui]::ShowWindow($Hwnd, 3) | Out-Null   # SW_MAXIMIZE
    Start-Sleep -Milliseconds 900
    [Win32Ui]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
    if (($r.Bottom - $r.Top) -gt $MinHeight) { break }
  }
  return $r
}

# 帶到前景：SetForegroundWindow 被前景鎖拒絕時以「最小化→還原」繞道；仍失敗不擲例外，
# 由呼叫端改以 PidAtPoint 命中斷言把關（見 Win32Ui.PidAtPoint）。
function Set-WindowForeground {
  param([IntPtr]$Hwnd)
  for ($i = 0; $i -lt 12; $i++) {
    [Win32Ui]::ForceForeground($Hwnd)
    Start-Sleep -Milliseconds 400
    if ([Win32Ui]::GetForegroundWindow() -eq $Hwnd) { return $true }
    if ($i -ge 2) {
      [Win32Ui]::ShowWindow($Hwnd, 6) | Out-Null   # SW_MINIMIZE
      Start-Sleep -Milliseconds 300
      [Win32Ui]::ShowWindow($Hwnd, 9) | Out-Null   # SW_RESTORE
      Start-Sleep -Milliseconds 500
      if ([Win32Ui]::GetForegroundWindow() -eq $Hwnd) { return $true }
    }
  }
  return $false
}
