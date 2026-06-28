using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LineClockBot.Api.Data;
using LineClockBot.Api.Models;
using LineClockBot.Api.Services;

namespace LineClockBot.Api.Controllers;

[ApiController]
[Route("webhook")]
public class LineWebhookController(
    AppDbContext db,
    TimeService timeService,
    LineService lineService,
    IConfiguration config) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post()
    {
        // 驗證 Line Signature
        var body = await ReadBodyAsync();
        if (!ValidateSignature(body))
            return Unauthorized();

        var payload = JsonDocument.Parse(body);
        var events = payload.RootElement.GetProperty("events");

        foreach (var ev in events.EnumerateArray())
        {
            if (ev.GetProperty("type").GetString() != "message") continue;
            if (ev.GetProperty("message").GetProperty("type").GetString() != "text") continue;

            var replyToken = ev.GetProperty("replyToken").GetString()!;
            var lineUserId = ev.GetProperty("source").GetProperty("userId").GetString()!;
            var text = ev.GetProperty("message").GetProperty("text").GetString()!;

            await HandleMessageAsync(lineUserId, text, replyToken);
        }

        return Ok();
    }

    private async Task HandleMessageAsync(string lineUserId, string text, string replyToken)
    {
        // 處理工時設定
        if (timeService.TryParseWorkHours(text, out var workHours))
        {
            var setting = await db.UserSettings.FirstOrDefaultAsync(s => s.LineUserId == lineUserId);
            if (setting == null)
            {
                setting = new UserSetting { LineUserId = lineUserId };
                db.UserSettings.Add(setting);
            }
            setting.WorkHours = workHours;
            setting.UpdatedAt = TimeService.TaiwanNow;
            await db.SaveChangesAsync();

            await lineService.ReplyAsync(replyToken, 
                $"✅ 已設定每日工時為 {workHours} 小時\n" +
                $"要重新打卡才能套用新的工時設定喔！");
            return;
        }

        // 處理打卡
        if (timeService.TryParseClockIn(text, out var clockInTime))
        {
            var setting = await db.UserSettings.FirstOrDefaultAsync(s => s.LineUserId == lineUserId);

            // 首次使用：尚未設定工時
            if (setting == null)
            {
                await lineService.ReplyAsync(replyToken,
                    "歡迎使用 鈴魚×RingRingFish！\n\n請先設定你的每日工時，例如：\n「工時 8」或「工時 8.5」");
                return;
            }

            var offWorkTime = timeService.CalculateOffWorkTime(clockInTime, setting.WorkHours);

            // 若今天已有打卡紀錄，更新它
            var today = TimeService.TaiwanToday;
            var existing = await db.WorkRecords
                .FirstOrDefaultAsync(r => r.LineUserId == lineUserId
                    && r.ClockInTime >= today
                    && r.ClockInTime < today.AddDays(1));

            if (existing != null)
            {
                existing.ClockInTime = clockInTime;
                existing.OffWorkTime = offWorkTime;
                existing.IsNotified = false;
                existing.NotifiedAt = null;
            }
            else
            {
                db.WorkRecords.Add(new WorkRecord
                {
                    LineUserId = lineUserId,
                    ClockInTime = clockInTime,
                    OffWorkTime = offWorkTime
                });
            }

            await db.SaveChangesAsync();

            await lineService.ReplyAsync(replyToken,
                $"✅ 打卡成功！\n" +
                $"上班時間：{clockInTime:HH:mm}\n" +
                $"下班時間：{offWorkTime:HH:mm}");
            return;
        }

        // 處理提醒設定
        if (timeService.TryParseReminder(text, out var reminderText, out var scheduledAt))
        {
            if (scheduledAt <= TimeService.TaiwanNow)
            {
                await lineService.ReplyAsync(replyToken, "鈴魚不能回到過去提醒啦><");
                return;
            }

            db.Reminders.Add(new Reminder
            {
                LineUserId = lineUserId,
                ScheduledAt = scheduledAt,
                Message = reminderText,
                ReminderType = "custom"
            });
            await db.SaveChangesAsync();

            var isToday = scheduledAt.Date == TimeService.TaiwanToday;
            var timeDisplay = isToday ? scheduledAt.ToString("HH:mm") : scheduledAt.ToString("M/d HH:mm");
            await lineService.ReplyAsync(replyToken,
                $"✅ 已設定在 {timeDisplay} 提醒「{reminderText}」");
            return;
        }

        // 查詢提醒列表（預設今天，支援「提醒清單 6/30」查指定日期）
        if (text == "提醒清單" || text.StartsWith("提醒清單 "))
        {
            var queryDate = TimeService.TaiwanToday;
            if (text.Length > 4)
            {
                var datePart = text[4..].Trim();
                if (!timeService.TryParseDateMD(datePart, out queryDate))
                {
                    await lineService.ReplyAsync(replyToken, "日期格式不對，請用「提醒清單 6/30」");
                    return;
                }
                if (queryDate < TimeService.TaiwanToday)
                {
                    await lineService.ReplyAsync(replyToken, "鈴魚曰：人要往前看😉");
                    return;
                }
            }

            var dayStart = queryDate;
            var reminders = await db.Reminders
                .Where(r => r.LineUserId == lineUserId && r.SentAt == null
                    && r.ScheduledAt >= dayStart && r.ScheduledAt < dayStart.AddDays(1))
                .OrderBy(r => r.ScheduledAt)
                .ToListAsync();

            var dateLabel = queryDate == TimeService.TaiwanToday ? "今天" : queryDate.ToString("M/d");

            if (reminders.Count == 0)
            {
                await lineService.ReplyAsync(replyToken, $"只有泡泡～{dateLabel}沒有待發的提醒");
                return;
            }

            var list = string.Join("\n", reminders.Select((r, i) =>
                $"{i + 1}. {r.ScheduledAt:HH:mm} - {r.Message}"));

            var cancelDateLabel = queryDate.ToString("M/d");
            await lineService.ReplyAsync(replyToken,
                $"{dateLabel}提醒清單\n\n{list}\n\n" +
                $"傳「取消提醒 {cancelDateLabel} [編號]」可以取消");
            return;
        }

        // 取消提醒（格式：「取消提醒 6/28 1」，必須指定日期）
        if (text.StartsWith("取消提醒"))
        {
            var remaining = text["取消提醒".Length..].Trim();
            var tokens = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 2
                && timeService.TryParseDateMD(tokens[0], out var cancelDate)
                && int.TryParse(tokens[1], out var index))
            {
                var dayStart = cancelDate;
                var reminders = await db.Reminders
                    .Where(r => r.LineUserId == lineUserId && r.SentAt == null
                        && r.ScheduledAt >= dayStart && r.ScheduledAt < dayStart.AddDays(1))
                    .OrderBy(r => r.ScheduledAt)
                    .ToListAsync();

                if (index > 0 && index <= reminders.Count)
                {
                    var reminder = reminders[index - 1];
                    db.Reminders.Remove(reminder);
                    await db.SaveChangesAsync();

                    await lineService.ReplyAsync(replyToken,
                        $"✅ 已取消提醒「{reminder.Message}」");
                    return;
                }

                await lineService.ReplyAsync(replyToken, "取消失敗，請確認日期和編號正確");
                return;
            }

            await lineService.ReplyAsync(replyToken, "格式請用「取消提醒 1/1 1」");
            return;
        }

        // 指令清單
        if (text == "呼叫鈴魚")
        {
            await lineService.ReplyAsync(replyToken,
                "鈴魚能幫你做的事：\n" +
                "📌 打卡：傳「上班」或「打卡 09:00」\n" +
                "📌 設定工時：傳「工時 8」\n" +
                "📌 設定提醒：傳「提醒 限動 12:00」或「提醒 限動 12/1 16:00」\n" +
                "📌 查詢提醒：傳「提醒清單」或「提醒清單 12/1」\n" +
                "📌 取消提醒：傳「取消提醒 12/1 1」");
            return;
        }

        // 預設回覆
        await lineService.ReplyAsync(replyToken, "(鈴魚吐泡泡)");
    }

    private async Task<string> ReadBodyAsync()
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;
        return body;
    }

    private bool ValidateSignature(string body)
    {
        var secret = config["Line:ChannelSecret"] ?? "";
        var headerSignature = Request.Headers["X-Line-Signature"].FirstOrDefault() ?? "";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        var signature = Convert.ToBase64String(hash);
        return signature == headerSignature;
    }
}
