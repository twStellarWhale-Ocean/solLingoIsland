---
name: cmpTechItem影片播放
date: 2026-07-13
description: 技術選型·元件層(cmpTechItem) Profile —— 「應用內影片播放與導引控制（embedded video playback & guided control）」功能型態，統一標準產品 WebView2 內嵌 YouTube IFrame Player API（WPF WebView2 控制項）。於 design.md ＜II.C.(B).1 技術選型＞落地版本/用法；與 modTechStack 多對多。專責「於桌面應用內嵌入並程式化控制線上影片播放（載入/播放/暫停/跳轉/取當前時間）」，與 [cmpTechItem字幕擷取]（取字幕文字）互為姊妹、責任區隔。
---

# I. 主旨目的

定義建置單元需要「於桌面應用內嵌入線上影片、並程式化控制其播放（載入指定影片、播放/暫停、跳轉、輪詢當前播放時間以到點暫停）」此一功能型態時的統一選型。**型態即契約身份、標準產品寫於內容**；日後換嵌入方案只改本契約一處，引用之 design 不動。於 design.md ＜III 系統設計.C.(A) 技術選型＞引用並落地版本/用法。此型態專責**影片播放與導引控制**，與 [cmpTechItem字幕擷取] 之**字幕文字取得**責任區隔。

# II. 參考準備

* **統一標準產品**：**WebView2（`Microsoft.Web.WebView2`）內嵌 YouTube IFrame Player API**——以 WPF `WebView2` 控制項載入一頁承載 YouTube IFrame Player API 之 HTML，經 `ExecuteScriptAsync` 下 `loadVideoById`／`playVideo`／`pauseVideo`／`seekTo`、以 `getCurrentTime` 取當前播放秒數；宿主端以計時器（`DispatcherTimer`）輪詢當前時間、與字幕 cue 比對而到句暫停。WebView2 Runtime 於 Windows 11 內建（Win10 需 Evergreen Runtime），為本型態唯一新平台依賴。
* **替代界線**：本地影片檔可改用 WPF `MediaElement`（免 WebView2）；線上串流（YouTube 等）因無直接檔案 URL、且須遵守平台播放條款，一律走官方 IFrame Player API 內嵌（**不下載影片內容、不繞過官方播放器**）。欲改用其他嵌入方案（如各平台官方 SDK）時於本契約換標準產品一處、design 引用不動（維持宿主端「載入/播放/暫停/跳轉/取時」之控制介面邊界）。
* **能力／成本前置**：WebView2 Runtime 須存在（缺失時明確提示引導安裝、不當機）；IFrame Player API 之播放受平台可用性/地區/嵌入權限影響（不可嵌入之影片須明確降級）；到點暫停精度受輪詢間隔限制（約 ±0.2s，非畫格級）、屬體驗參數非資料正確性。播放為前景持續互動、資源占用高於背景閒置——適用 [sysTechType桌面查詢工具] 契約時，「閒置輕量」門檻限背景態、影片頁前景使用時不套用（該 sysTechType 契約適用邊界，plan 載明）。

# III. 內容程序

* **控制慣例**：播放控制經 `ExecuteScriptAsync` 非同步下達、不阻塞 UI；當前時間輪詢以計時器（≈100ms）於 UI 執行緒進行；到句暫停判定抽為純函式（當前時間＋cue 清單→應否暫停/當前句 index）以便單元測試（不起真瀏覽器）。
* **輸入／輸出**：輸入＝影片識別（YouTube video id/URL）＋字幕 cue 時間軸（供到點暫停）；輸出＝播放器狀態（播放/暫停/當前時間）與到句暫停事件，供宿主 UI 顯示該句字幕。
* **參數**：輪詢間隔、暫停策略走組態/常數；不硬編影片來源。
* **可驗證性**：到句暫停判定（`PauseDecider` 之類純函式）以假時間/cue 注入單元測試；WebView2 播放本身屬 UI 行為、以 e2e 視覺驗（不入單元測試）。
* **隱私/合規**：僅嵌入官方播放器播放、**不下載影片內容、不儲存影片**；遵守平台嵌入播放條款，來源由使用者自負。

# IV. 備註紀錄

* 2026-07-13：建立。cmpTechItem「應用內影片播放與導引控制」型態首份；統一 WebView2 內嵌 YouTube IFrame Player API。因 solLingoIsland 增量 #139「影片擷取學習查詢（Mode A）」立案而補建；與 [cmpTechItem字幕擷取] 責任區隔。**候選契約，待 USR 於 Draft PR 裁決入庫（範本庫正本尚待同步）。**
