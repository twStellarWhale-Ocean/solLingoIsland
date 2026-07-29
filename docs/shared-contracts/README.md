# docs/shared-contracts — 共享契約副本

本資料夾放 [`../design.md`](../design.md) 引用之**共享契約（通用／標準／常例）副本**，正本在中央契約庫（kdbUserSkills `2tech-incrFlow-0shared/範本庫`），由 sub-sync-contracts 對正本檢一致、**不得本地修改**。本案**自訂／專用設計不成檔**——寫入 design.md 文字（引用處 `[標記]＋就近說明`）。

## 分類 → 檔案格式對照

| 資料夾 | 契約類型 | 機器可驗格式（優先） |
|---|---|---|
| apiIntf/ | API 介面 | OpenAPI yaml、markdown 協定 |
| comIntf/ | 連線／事件 | markdown 協定說明 |
| sysTechType/ | 系統類型 Profile（sys 層） | Markdown Profile |
| modTechStack/ | 構件技術疊層 Profile（mod 層） | Markdown Profile（建置／測試／部署指令、產物型態） |
| cmpTechItem/ | 函式庫元件型態 Profile | Markdown Profile |

## 現有副本

- apiIntf：標準OPENAI的API協定
- comIntf：通用HTTPS連線
- sysTechType：桌面App
- modTechStack：WinApp
- cmpTechItem：語音合成、發音評分、桌面通知、字幕擷取、影片播放

> **2026-07-29 隨 design.md 遷 4.1 一併更名**：`techApp/` → `sysTechType/`、`techStack/` → `modTechStack/`、`techItem/` → `cmpTechItem/`；`techApp桌面查詢工具` 之正本已於中央庫更名並上收為基底型 `sysTechType桌面App`（原「常駐即查」能力降為其選配節），候選契約 `techStackDotnetWin` 由封閉枚舉之 `modTechStackWinApp` 取代。三份中央有正本者皆自中央庫重新複製、非本地改名。
>
> **已知待清（非本增量範圍）**：`cmpTechItem字幕擷取`／`cmpTechItem影片播放` 兩份於中央契約庫查無正本——或屬本案自訂（依規不成檔、應併回 design.md 文字），或屬應上收中央之共享契約。留待 sub-sync-contracts 之錯位檢查裁定。

## 自訂設計（不在此，見 design.md 文字）

本案自訂 etyCfg（sysLingoIsland 組態）、runWi（熱鍵喚起框選／辨識翻譯選區／查看聆聽結果等）、ackWi（各作業項之自檢督核）、setWi（安裝金鑰／啟動結束常駐／移除）、datIntf（查詢結果格式）、comIntf（本機桌面操作）、solTechStyle（淺粉童趣桌面風格）、solHmi／sysHmi／modHmi（桌面常駐應用形式、三個存取端點、十二個具名頁）皆為本案專屬、不成檔——內容在 design.md 就近文字（invariant／欄位格式／行為步驟）描述。
