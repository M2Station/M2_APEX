# M2_APEX

[English](README.md) · **繁體中文**

> 一個為 Windows 打造、受 [Listary](https://www.listary.com/) 啟發的極速檔案搜尋與應用程式啟動器。

M2_APEX 常駐於系統匣,透過全域快捷鍵隨處叫出搜尋列,毫秒級模糊搜尋整台電腦的檔案、
應用程式、系統指令與網頁;並提供 Listary 招牌的 **Quick Switch** —— 在檔案總管中直接
打字即可即時篩選、高亮並跳到對應檔案。

技術:**.NET 9 · WPF · WinForms(系統匣)**,純受管理程式碼 + Win32/COM/UI Automation 互通,無第三方相依套件。
品牌:系統匣圖示、視窗圖示與搜尋列皆使用 **M2** 標誌(向量繪製,見 `Assets/M2Logo.cs`)。

---

## 主要功能

| 分類 | 說明 |
| --- | --- |
| **全域啟動** | 雙擊 `Ctrl`,或按 `Alt+Space`,隨處叫出搜尋列 |
| **檔案 / 資料夾搜尋** | 背景索引所有固定磁碟,毫秒級模糊搜尋,結果快取到磁碟開機即用 |
| **應用程式啟動** | 掃描開始選單捷徑(`.lnk` / `.url` / `.appref-ms`) |
| **網頁搜尋** | 找不到或需要時,一鍵用預設搜尋引擎搜尋;可自訂 URL |
| **系統指令** | 鎖定、睡眠、關機、重新啟動、登出、資源回收筒、清空回收筒、設定、控制台、工作管理員 |
| **模糊比對** | 支援字首、字界、駝峰、縮寫(如 `vsc` → Visual Studio Code),並將命中字元**高亮** |
| **習慣排序** | 依使用頻率與最近使用時間,把常用結果排到前面 |
| **結果動作** | 開啟、開啟所在資料夾、以系統管理員執行、複製路徑 |
| **Quick Switch** | 在檔案總管檔案清單打字 → 彈出高亮清單 → 跳到 / 選取對應檔案 |
| **系統匣常駐** | 背景執行;右鍵選單可開啟搜尋、重建索引、設定、結束 |
| **設定視窗** | 快捷鍵、Quick Switch、結果數量、搜尋引擎、索引磁碟、排除資料夾、開機自動啟動等 |

---

## 快捷鍵與操作

### 搜尋列(全域)

| 按鍵 | 動作 |
| --- | --- |
| `Ctrl` `Ctrl`(雙擊) / `Alt+Space` | 開啟搜尋列 |
| 直接輸入 | 即時搜尋 |
| `↑` / `↓`、`PageUp` / `PageDown` | 移動選取 |
| `Enter` | 開啟選取項目 |
| `Ctrl+Enter` | 開啟所在資料夾 |
| `Shift+Enter` | 以系統管理員執行 |
| `Ctrl+C` | 複製路徑 |
| `Esc` | 關閉 |

### Quick Switch(檔案總管內)

| 按鍵 | 動作 |
| --- | --- |
| 於檔案清單直接輸入 | 彈出高亮篩選清單 |
| `↑` / `↓` | 在命中項目間切換(檔案總管同步選取) |
| `Enter` | 原地開啟 / 進入 |
| `Backspace` | 修改關鍵字 |
| 滑鼠點選項目 | 開啟該項目 |
| `Esc` / 點到其他視窗 | 關閉 |

> Quick Switch 只在檔案清單有焦點時作用;在網址列、搜尋框或重新命名(F2)等文字輸入框中打字完全不受影響。
### M2_Commander（雙欄檔案管理器）

在任一搜尋介面按 `Ctrl` + `` ` ``（反引號）開啟，全程以鍵盤操作：

| 按鍵 | 動作 |
| --- | --- |
| `Tab` | 切換使用中的窗格 |
| `Alt+←` / `Alt+→` | 切換到左側／右側窗格 |
| `Enter` | 開啟檔案 / 進入資料夾 |
| `Backspace` / `Alt+↑` | 回到上一層資料夾 |
| `Alt+[` | 上一頁（瀏覽紀錄） |
| `Alt+]` | 下一頁（瀏覽紀錄） |
| 直接輸入 `A`–`Z` | 篩選清單 |
| `Ctrl+C` / `Ctrl+V` | 標記複製 / 貼到使用中的窗格 |
| `F2` | 重新命名 |
| `Del` | 永久刪除（不進資源回收筒） |
| `Shift+Del` | 快速刪除資料夾 |
| `Ctrl+U` | 交換左右兩個窗格 |
| `Ctrl+R` | 重新整理兩個窗格 |
| `F1` | 動作選單（複製／移動／刪除／新增…） |
| `F11` | 自訂指令設定 |
| `F12` | 所有鍵盤快速鍵 |
| `F10` / `Esc` | 結束 |
---

## 系統需求

- Windows 10 / 11(x64;ARM64 見「建置」一節)
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)(若使用自封裝發行則不需另外安裝)

---

## 建置與執行

```powershell
# 建置
dotnet build -c Release

# 直接執行
dotnet run -c Release
# 或執行輸出的可執行檔
.\bin\Release\net9.0-windows\M2_APEX.exe
```

### 發行成單一自封裝檔(免安裝 .NET)

```powershell
# x64
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# ARM64(Windows on ARM)
dotnet publish -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

> 專案為純受管理程式碼,所有原生呼叫皆針對 Windows 系統 DLL(`user32` / `shell32` / `kernel32`)與
> COM / UI Automation,**架構中立**,可直接交叉編譯出 ARM64 版本。

---

## 專案結構

```
M2_APEX/
├─ App.xaml(.cs)              # 進入點、系統匣、單一實例、崩潰記錄、服務組裝
├─ app.manifest               # DPI 感知 / asInvoker / Win10-11 相容性
├─ Assets/
│  ├─ M2Logo.cs               # M2 標誌(WPF 幾何/影像/點陣圖來源)
│  └─ m2-logo.svg             # 原始 SVG 標誌
├─ Models/
│  ├─ AppSettings.cs          # 使用者設定(JSON 持久化)
│  └─ SearchResult.cs         # 搜尋結果模型
├─ Services/
│  ├─ FuzzyMatcher.cs         # 模糊比對評分 + 命中索引(高亮用)
│  ├─ FileIndexService.cs     # 全碟 BFS 索引 + 磁碟快取
│  ├─ AppIndexService.cs      # 開始選單應用程式掃描
│  ├─ SearchEngine.cs         # 合併 App/檔案/指令/網頁並排序
│  ├─ CommandProvider.cs      # 內建系統指令
│  ├─ UsageTracker.cs         # 習慣(頻率 + 最近)排序
│  ├─ LaunchService.cs        # 開啟 / 開資料夾 / 管理員 / 複製路徑
│  ├─ HotkeyService.cs        # 低階鍵盤鉤子(雙擊 Ctrl / Alt+Space / 按鍵攔截)
│  ├─ QuickSwitchService.cs   # 檔案總管型即打即跳邏輯
│  ├─ ExplorerAccess.cs       # Shell.Application COM + UI Automation
│  ├─ IconGlyph.cs            # 依副檔名對應 Segoe MDL2 圖示
│  ├─ AppIcon.cs             # 由 M2 標誌算繪系統匣圖示
│  └─ StartupService.cs       # 開機自動啟動(登錄機碼)
├─ ViewModels/
│  └─ SearchViewModel.cs      # 搜尋列 MVVM(去抖動、選取)
├─ Views/
│  ├─ SearchWindow.xaml(.cs)      # 主搜尋列
│  ├─ QuickSwitchBar.xaml(.cs)    # Quick Switch 高亮清單浮窗
│  └─ SettingsWindow.xaml(.cs)    # 設定視窗
├─ Behaviors/
│  ├─ Highlight.cs                # TextBlock 命中字元高亮附加屬性
│  └─ KindToLabelConverter.cs
└─ Native/
   └─ NativeMethods.cs        # Win32 P/Invoke 宣告
```

---

## 設定與資料位置

所有資料存於 `%APPDATA%\M2_APEX`:

| 檔案 | 內容 |
| --- | --- |
| `settings.json` | 使用者設定 |
| `usage.json` | 使用習慣(頻率 / 最近使用) |
| `index.cache` | 檔案索引快取 |
| `crash.log` | 未處理例外記錄(如有) |

設定視窗可調整:雙擊 Ctrl / Alt+Space、Quick Switch、最大結果數、網頁搜尋 URL、
索引磁碟(留空 = 所有固定磁碟)、排除資料夾、是否索引隱藏檔、開機自動啟動,並可手動「重建索引」。

---

## 運作原理(概要)

- **全域快捷鍵 / 按鍵攔截**:`WH_KEYBOARD_LL` 低階鍵盤鉤子偵測雙擊 Ctrl、Alt+Space,
  並在檔案總管檔案清單有焦點時攔截輸入以驅動 Quick Switch(其餘情況一律放行,不影響一般打字)。
- **檔案索引**:以佇列 BFS 列舉固定磁碟,套用排除清單與隱藏檔屬性;結果存為 `index.cache`,
  下次啟動直接載入,並可手動重建。
- **模糊比對**:貪婪子序列 + 字界 / 駝峰 / 縮寫加權評分,顯示時才計算命中索引供高亮;搜尋以平行 top-K 挑選。
- **Quick Switch**:用 `Shell.Application` COM 讀取當前資料夾內容(比對 / 原地開啟),
  用 UI Automation 選取並捲動到對應項目(失敗時仍可用 Enter 開啟,優雅降級)。

---

## 已知限制

- Quick Switch 目前比對「當前資料夾」內容(非跨資料夾全域搜尋)。
- Quick Switch 的按鍵字元對應目前涵蓋 A–Z、0–9、空白、`.` `-` `_`(尚未支援完整符號 / 多國鍵盤配置)。
- 檔案索引為啟動載入 + 手動重建(無即時檔案系統監看)。
- 首次索引整台電腦需數秒(視檔案數量而定)。

---

## 授權

僅供學習與個人使用之範例專案;「Listary」為其原作者之商標,本專案與其無任何關聯。
M2 標誌取自同作者的 M2Station/M2_GIT_DIFF 專案。
