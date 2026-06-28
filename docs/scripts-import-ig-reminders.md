# 工具腳本：ImportIgReminders

**路徑：** `Scripts/ImportIgReminders.csx`

批次為指定使用者匯入整年度的 IG 提醒，自動跳過週末與國定假日，只在工作日發送。

---

## 前置需求

安裝 [dotnet-script](https://github.com/dotnet-script/dotnet-script)（全域安裝一次即可）：

```bash
dotnet tool install -g dotnet-script
```

---

## 環境變數

執行前須設定以下兩個環境變數（或直接修改腳本內的預設值）：

| 環境變數 | 說明 | 範例 |
|----------|------|------|
| `LINECLOCKBOT_API_URL` | App Service 的根網址 | `https://your-app.azurewebsites.net` |
| `LINECLOCKBOT_API_KEY` | Internal API Key（與 `Internal__ApiKey` 相同） | `your-secret-key` |

```bash
# Windows PowerShell
$env:LINECLOCKBOT_API_URL = "https://your-app.azurewebsites.net"
$env:LINECLOCKBOT_API_KEY = "your-secret-key"

# macOS / Linux
export LINECLOCKBOT_API_URL="https://your-app.azurewebsites.net"
export LINECLOCKBOT_API_KEY="your-secret-key"
```

---

## 執行方式

```bash
dotnet script Scripts/ImportIgReminders.csx -- <year> <lineUserId> [lineUserId2 ...]
```

- `<year>`：要匯入的年份（例如 `2026`）
- `<lineUserId>`：LINE 使用者 ID，可傳入多個（空格分隔）

### 範例

```bash
# 單一使用者
dotnet script Scripts/ImportIgReminders.csx -- 2026 Uxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

# 多位使用者
dotnet script Scripts/ImportIgReminders.csx -- 2026 Uaaa111 Ubbb222
```

---

## 執行流程

1. 解析年份與使用者 ID 參數
2. 計算該年度所有工作日（週一至週五，排除國定假日）
3. **只處理今天（含）之後的日期**，不回補過去的提醒
4. 顯示預計建立筆數，等待確認（輸入 `y` 繼續，其他取消）
5. 逐日對每位使用者呼叫 `POST /internal/reminders`
6. 若該筆提醒已存在（API 回傳 `409 Conflict`），自動略過
7. 完成後輸出成功 / 略過 / 失敗筆數統計

---

## 提醒內容

腳本預設的提醒設定（可直接修改腳本開頭的設定區）：

| 項目 | 預設值 | 說明 |
|------|--------|------|
| 提醒時間 | `11:00` | 台灣時間（24小時制） |
| 訊息內容 | `查看IG的時間到了！` | 發送給使用者的提醒文字 |
| 提醒類型 | `custom` | 在 Admin Reminders 頁面以此 Type 顯示 |

---

## 國定假日維護

腳本內硬編碼了國定假日清單（`MM-dd` 格式），**每年需手動更新**：

```csharp
var holidays = new HashSet<string>
{
    "01-01", // 元旦
    "02-16", // 春節
    // ...
};
```

---

## 輸出範例

```
匯入年份：2026
起始日期：2026-06-28（今天）
使用者：Uxxxxxxxxx
提醒時間：11:00

共 134 個工作日，每位使用者將建立 134 筆提醒
確認繼續？(y/N) y

── 處理使用者 Uxxxxxxxxx ──
  ✓ 2026-06-29 (Monday)
  ✓ 2026-06-30 (Tuesday)
  - 2026-07-01 已存在，略過
  ✗ 2026-07-02 失敗：InternalServerError

完成！成功：132，略過：1，失敗：1
```

> 失敗時腳本結束碼為 `1`，可在 CI/CD 或 shell script 中判斷是否需要重試。

---

## 注意事項

- 腳本只會新增**今天以後**的提醒，不會回補歷史日期
- 重複執行是安全的，已存在的提醒會被略過（不會重複建立）
- 多位使用者可一次傳入，腳本會依序逐一處理
