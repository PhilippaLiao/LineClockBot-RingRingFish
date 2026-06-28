# LineClockBot

LINE Bot 提醒小助理，支援打卡計算下班時間與自訂提醒推播。

## 專案結構

```
LineClockBot/
├── LineClockBot.Api/        # ASP.NET Core - Webhook + 內部 API + Admin 管理介面
├── LineClockBot.Functions/  # Azure Functions - Timer 排程推播
├── Scripts/                 # 工具腳本（批次匯入提醒）
└── Doc/                     # 文件（CHANGELOG、架構圖）
```

## 架構說明

```
使用者 LINE 打卡 / 傳送指令
    ↓
LineClockBot.Api（Azure App Service F1）
  - 接收 Webhook，驗證 LINE Signature
  - 解析指令（打卡、工時、提醒）
  - 存入 SQLite（D:\home\lineclockbot.db）
    ↓
LineClockBot.Functions（Azure Functions Consumption Plan）
  - Timer Trigger 每分鐘執行一次
  - 呼叫 Api 的 POST /internal/process-all-reminders
  - Api 查出到期的提醒與下班紀錄 → 推播 LINE
    ↓
使用者收到下班提醒 / 自訂提醒
```

## 費用

| 服務 | 方案 | 費用 |
|---|---|---|
| Azure App Service | F1（免費） | $0 |
| Azure Functions | Consumption Plan | $0（免費額度內） |

## Bot 使用方式

| 訊息 | 功能 |
|---|---|
| `工時 8` 或 `設定工時 8.5` | 設定每日工時（首次使用必須先設定） |
| `上班` 或 `打卡` | 打卡（使用當下時間） |
| `打卡 09:00` 或 `上班 09:30` | 打卡（指定時間） |
| `提醒 限動 12:00` | 設定當天指定時間的提醒 |
| `提醒 限動 6/30 16:00` | 設定指定日期的提醒 |
| `提醒清單` | 查詢今日待發送的提醒清單 |
| `提醒清單 6/30` | 查詢指定日期的待發提醒 |
| `取消提醒 6/28 1` | 取消指定日期提醒清單中的第 1 筆 |
| `呼叫鈴魚` | 顯示所有可用指令 |

> 今日已過期（超過 24 小時）的提醒將不會被推播。

**推播訊息格式：**

- 自訂提醒：`Ring Ring 提醒：{描述}`
- 下班提醒：`Ring Ring~鈴魚想回家啦！`

## Admin 管理介面

路徑：`https://你的app.azurewebsites.net/Admin`  
以 `Internal API Key` 登入（Session 有效 8 小時）。

| 頁面 | 路徑 | 功能 |
|------|------|------|
| 登入 | `/Admin/Login` | API Key 身份驗證 |
| 使用者設定 | `/Admin/UserSettings` | 查看使用者工時、設定暱稱、刪除使用者 |
| 打卡紀錄 | `/Admin/WorkRecords` | 查看、刪除打卡紀錄；支援依使用者篩選、分頁（每頁 30 筆）、批次刪除 |
| 提醒管理 | `/Admin/Reminders` | 查看（全部／待發／已發）、新增、刪除提醒；支援依使用者篩選、分頁（每頁 30 筆）、批次刪除 |

## API 端點

### 公開端點

#### `POST /webhook`

接收 LINE Webhook 事件。

- **Headers：** `X-Line-Signature`（LINE 簽章驗證）
- **Response：** `200 OK`

### 內部端點（需 `X-Internal-Key` Header）

#### `POST /internal/process-all-reminders`

處理並發送所有到期提醒與下班通知。由 Azure Functions 計時器每分鐘呼叫。

**Response：**
```json
{
  "remindersSent": 2,
  "offWorkNotified": 1,
  "now": "2026-06-20 17:30:00",
  "dueRemindersFound": 2,
  "reminderErrors": []
}
```

#### `POST /internal/reminders`

建立自訂提醒。

**Request Body：**
```json
{
  "lineUserId": "Uxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
  "scheduledAt": "2026-06-20T15:30:00",
  "message": "喝水時間到了！",
  "reminderType": "custom"
}
```

**Response：** `200 OK: { "created": true }` / `409 Conflict`：相同提醒已存在

#### `DELETE /internal/reminders/{id}`

刪除指定 ID 的提醒。**Response：** `{ "deleted": true, "id": 42 }`

#### `GET /internal/users`

取得所有 LINE 使用者 ID 清單。**Response：** `["Uxxxxxxxxx", "Uyyyyyyyyy"]`

## 資料模型

### UserSetting（使用者工時設定）

| 欄位 | 型別 | 說明 |
|------|------|------|
| Id | int | 主鍵 |
| LineUserId | string | LINE 使用者 ID |
| Nickname | string? | 暱稱（可在 Admin 介面設定）|
| WorkHours | double | 每日工作時數（例：8.0、8.5）|
| CreatedAt | DateTime | 建立時間（台灣時區）|
| UpdatedAt | DateTime | 更新時間（台灣時區）|

### WorkRecord（打卡紀錄）

| 欄位 | 型別 | 說明 |
|------|------|------|
| Id | int | 主鍵 |
| LineUserId | string | LINE 使用者 ID |
| ClockInTime | DateTime | 打卡時間 |
| OffWorkTime | DateTime | 計算出的下班時間（打卡 + 工時）|
| IsNotified | bool | 是否已發送下班提醒 |
| NotifiedAt | DateTime? | 提醒發送時間 |
| CreatedAt | DateTime | 建立時間（台灣時區）|

### Reminder（自訂提醒）

| 欄位 | 型別 | 說明 |
|------|------|------|
| Id | int | 主鍵 |
| LineUserId | string | LINE 使用者 ID |
| ScheduledAt | DateTime | 預定觸發時間 |
| Message | string | 提醒訊息內容 |
| SentAt | DateTime? | 實際發送時間（null 表示待發）|
| ReminderType | string | 提醒類型（`custom`、`ig`）|
| CreatedAt | DateTime | 建立時間（台灣時區）|

## 部署步驟

### 1. LINE Developer Console
- 建立 Messaging API Channel
- 取得 Channel Secret 與 Channel Access Token
- Webhook URL 設定為：`https://你的app.azurewebsites.net/webhook`

### 2. Azure App Service
- 建立 App Service（F1 免費方案，Runtime: .NET 10）
- 在「環境變數」設定：

| 變數名稱 | 說明 |
|----------|------|
| `Line__ChannelSecret` | LINE Channel Secret |
| `Line__ChannelAccessToken` | LINE Channel Access Token |
| `Internal__ApiKey` | 內部 API 金鑰（自訂隨機字串）|

### 3. Azure Functions
- 建立 Function App（Consumption Plan，Runtime: .NET 10 Isolated）
- 在「環境變數」設定：

| 變數名稱 | 說明 |
|----------|------|
| `Api__BaseUrl` | App Service 網址（例：`https://lineclockbot.azurewebsites.net`）|
| `Api__InternalKey` | 須與 App Service 的 `Internal__ApiKey` 相同 |

### 4. GitHub Actions（CI/CD）
- 從 Azure Portal 下載 publish profile
- 加入 GitHub Secrets：`AZURE_WEBAPP_PUBLISH_PROFILE`、`AZURE_FUNCTIONS_PUBLISH_PROFILE`
- Push 到 main branch 自動部署

| Workflow | 觸發條件 | 部署目標 |
|----------|----------|----------|
| `ci-develop.yml` | push 到 `develop` 或 PR 至 `main` | Build 驗證（不部署）|
| `deploy-api.yml` | `LineClockBot.Api/**` 有變更（`main`）| Azure App Service `LineClockBot` |
| `deploy-functions.yml` | `LineClockBot.Functions/**` 有變更（`main`）| Azure Function App `line-clock-bot-func` |

## 本地開發

建立測試用的第二個 LINE Bot Channel（LINE Developer Console），與正式 Bot 完全隔離：

1. 在 LINE Developer Console 建立第二個 Messaging API Channel 作為測試環境
2. 複製範本建立 `LineClockBot.Api/appsettings.Development.json`，填入**測試 Bot** 的 LINE 金鑰（此檔案已在 `.gitignore` 中，不會進 Git）
3. `dotnet run --project LineClockBot.Api`

資料庫路徑：

| 環境 | 路徑 |
|------|------|
| 本機開發 | `./lineclockbot.db`（專案根目錄）|
| Azure App Service | `D:\home\lineclockbot.db`（持久化路徑）|

## 工具腳本

`Scripts/ImportIgReminders.csx`：批次匯入整年度提醒，自動跳過週末與台灣國定假日。

**安裝相依工具：**

```bash
dotnet tool install -g dotnet-script
```

**使用方式：**

```bash
dotnet script Scripts/ImportIgReminders.csx -- <year> <lineUserId> [lineUserId2 ...]
```

- 預設時間：每天 11:00，提醒訊息：「查看IG的時間到了！」
- 執行前請透過環境變數傳入金鑰，避免將敏感資訊寫入腳本：
  ```bash
  $env:LINECLOCKBOT_API_URL="https://你的app.azurewebsites.net"
  $env:LINECLOCKBOT_API_KEY="你的 Internal API Key"
  ```
- 已內建 2026 年台灣國定假日排除清單
