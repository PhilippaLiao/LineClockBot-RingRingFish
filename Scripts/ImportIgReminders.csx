#!/usr/bin/env dotnet-script
// 用途：批次匯入整年度的 IG 提醒（週一到週五，跳過台灣國定假日）
// 執行方式：dotnet script Scripts/ImportIgReminders.csx -- <year> <lineUserId>
// 範例：dotnet script Scripts/ImportIgReminders.csx -- 2026 Uxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
// 需要先安裝 dotnet-script：dotnet tool install -g dotnet-script

#r "nuget: System.Net.Http, 4.3.4"

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

// ── 設定區 ────────────────────────────────────────────────
// 優先讀環境變數，否則 fallback 至下方預設值
string ApiBaseUrl = Environment.GetEnvironmentVariable("LINECLOCKBOT_API_URL")
    ?? "https://<your-app>.azurewebsites.net";
string InternalApiKey = Environment.GetEnvironmentVariable("LINECLOCKBOT_API_KEY")
    ?? "<your-internal-api-key>";
const int ReminderHour = 11;   // 提醒時間（台灣時間，24小時制）
const int ReminderMinute = 0;
// ─────────────────────────────────────────────────────────

// 台灣國定假日（格式：MM-dd），每年請自行更新
// 參考：https://www.dgpa.gov.tw/informationlist?uid=49
var holidays = new HashSet<string>
{
    "01-01", // 元旦
    "02-16", // 春節
    "02-17", // 春節
    "02-18", // 春節
    "02-19", // 春節
    "02-20", // 春節
    "02-27", // 和平紀念日補假
    "04-03", // 兒童節補假
    "04-04", // 兒童節/清明節
    "04-06", // 清明節補假
    "05-01", // 勞動節
    "06-19", // 端午節
    "09-25", // 中秋節
    "09-28", // 教師節
    "10-09", // 國慶日補假
    "10-26", // 光復節補假
    "12-25", // 行憲紀念日
};

if (Args.Count < 2)
{
    Console.Error.WriteLine("用法：dotnet script Scripts/ImportIgReminders.csx -- <year> <lineUserId>");
    Console.Error.WriteLine("範例：dotnet script Scripts/ImportIgReminders.csx -- 2026 Uxxxxxxxxx");
    return 1;
}

if (!int.TryParse(Args[0], out var year))
{
    Console.Error.WriteLine($"無效的年份：{Args[0]}");
    return 1;
}

var userIds = Args.Skip(1).ToList();
var startFrom = DateOnly.FromDateTime(DateTime.Today); // 只新增今天（含）以後的日期
Console.WriteLine($"匯入年份：{year}");
Console.WriteLine($"起始日期：{startFrom:yyyy-MM-dd}（今天）");
Console.WriteLine($"使用者：{string.Join(", ", userIds)}");
Console.WriteLine($"提醒時間：{ReminderHour:D2}:{ReminderMinute:D2}");
Console.WriteLine();

// 計算所有工作日（週一到週五，排除國定假日）
var workdays = new List<DateOnly>();
var current = new DateOnly(year, 1, 1) > startFrom ? new DateOnly(year, 1, 1) : startFrom;
var end = new DateOnly(year, 12, 31);

while (current <= end)
{
    var isWeekday = current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday;
    var isHoliday = holidays.Contains(current.ToString("MM-dd"));

    if (isWeekday && !isHoliday)
        workdays.Add(current);

    current = current.AddDays(1);
}

Console.WriteLine($"共 {workdays.Count} 個工作日，每位使用者將建立 {workdays.Count} 筆提醒");
Console.Write("確認繼續？(y/N) ");
var confirm = Console.ReadLine()?.Trim().ToLower();
if (confirm != "y")
{
    Console.WriteLine("已取消");
    return 0;
}

Console.WriteLine();

var successCount = 0;
var skipCount = 0;
var errorCount = 0;

using (var httpClient = new HttpClient())
{
    httpClient.DefaultRequestHeaders.Add("X-Internal-Key", InternalApiKey);
    foreach (var userId in userIds)
    {
        Console.WriteLine($"── 處理使用者 {userId} ──");

        foreach (var date in workdays)
        {
            var scheduledAt = new DateTime(date.Year, date.Month, date.Day, ReminderHour, ReminderMinute, 0);

            var payload = new
            {
                lineUserId = userId,
                scheduledAt = scheduledAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                message = "查看IG的時間到了！",
                reminderType = "custom"
            };

            try
            {
                var response = await httpClient.PostAsJsonAsync($"{ApiBaseUrl}/internal/reminders", payload);

                if (response.IsSuccessStatusCode)
                {
                    successCount++;
                    Console.WriteLine($"  ✓ {date:yyyy-MM-dd} ({date.DayOfWeek})");
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    skipCount++;
                    Console.WriteLine($"  - {date:yyyy-MM-dd} 已存在，略過");
                }
                else
                {
                    errorCount++;
                    Console.WriteLine($"  ✗ {date:yyyy-MM-dd} 失敗：{response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                Console.WriteLine($"  ✗ {date:yyyy-MM-dd} 例外：{ex.Message}");
            }
        }
    }
}
Console.WriteLine();
Console.WriteLine($"完成！成功：{successCount}，略過：{skipCount}，失敗：{errorCount}");
return errorCount > 0 ? 1 : 0;
