using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineClockBot.Api.Data;
using LineClockBot.Api.Models;
using LineClockBot.Api.Services;

namespace LineClockBot.Api.Controllers;

/// <summary>
/// 內部 API，以 API Key 保護，不對外公開。
/// 供 Azure Functions Timer 與管理腳本呼叫。
/// </summary>
[ApiController]
[Route("internal")]
public class InternalController(
    AppDbContext db,
    LineService lineService,
    IConfiguration config,
    ILogger<InternalController> logger) : ControllerBase
{
    [HttpPost("reminders")]
    public async Task<IActionResult> CreateReminder([FromBody] CreateReminderRequest req)
    {
        var apiKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        var expectedKey = config["Internal:ApiKey"];
        if (apiKey != expectedKey)
            return Unauthorized();

        var existing = await db.Reminders.FirstOrDefaultAsync(r =>
            r.LineUserId == req.LineUserId &&
            r.ScheduledAt == req.ScheduledAt &&
            r.Message == req.Message);

        if (existing != null)
            return Conflict(new { message = "相同提醒已存在" });

        db.Reminders.Add(new Reminder
        {
            LineUserId = req.LineUserId,
            ScheduledAt = req.ScheduledAt,
            Message = req.Message,
            ReminderType = req.ReminderType ?? "custom"
        });
        await db.SaveChangesAsync();
        return Ok(new { created = true });
    }

    [HttpDelete("reminders/{id}")]
    public async Task<IActionResult> DeleteReminder(int id)
    {
        var apiKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        var expectedKey = config["Internal:ApiKey"];
        if (apiKey != expectedKey)
            return Unauthorized();

        var reminder = await db.Reminders.FindAsync(id);
        if (reminder == null)
            return NotFound();

        db.Reminders.Remove(reminder);
        await db.SaveChangesAsync();
        return Ok(new { deleted = true, id });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var apiKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        var expectedKey = config["Internal:ApiKey"];
        if (apiKey != expectedKey)
            return Unauthorized();

        var userIds = await db.UserSettings.Select(s => s.LineUserId).ToListAsync();
        return Ok(userIds);
    }

    [HttpPost("process-all-reminders")]
    public async Task<IActionResult> ProcessAllReminders()
    {
        // 驗證內部 API Key
        var apiKey = Request.Headers["X-Internal-Key"].FirstOrDefault();
        var expectedKey = config["Internal:ApiKey"];
        if (apiKey != expectedKey)
            return Unauthorized();

        var now = TimeService.TaiwanNow;

        // 原子標記：先 UPDATE SentAt，只處理本次成功標記到的提醒，防止並行重複推播
        var cutoff = now.AddHours(-24);
        var dueReminderIds = await db.Reminders
            .Where(r => r.SentAt == null && r.ScheduledAt <= now && r.ScheduledAt >= cutoff)
            .Select(r => r.Id)
            .ToListAsync();

        var remindersSent = 0;
        var reminderErrors = new List<string>();
        foreach (var id in dueReminderIds)
        {
            var marked = await db.Reminders
                .Where(r => r.Id == id && r.SentAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, now));

            if (marked == 0) continue; // 已被其他並行執行標記，跳過

            var reminder = await db.Reminders.FindAsync(id);
            if (reminder == null) continue;

            try
            {
                await lineService.PushAsync(reminder.LineUserId,
                    $"Ring Ring 提醒：{reminder.Message}");
                remindersSent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Push reminder failed for {UserId}", reminder.LineUserId);
                reminderErrors.Add($"{reminder.LineUserId}: {ex.Message}");
                // 推播失敗時重置標記，下次可重試
                await db.Reminders.Where(r => r.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.SentAt, (DateTime?)null));
            }
        }

        // 原子標記：下班提醒同樣先標記再推播
        var offWorkRecordIds = await db.WorkRecords
            .Where(r => !r.IsNotified && r.OffWorkTime <= now)
            .Select(r => r.Id)
            .ToListAsync();

        var offWorkNotified = 0;
        foreach (var id in offWorkRecordIds)
        {
            var marked = await db.WorkRecords
                .Where(r => r.Id == id && !r.IsNotified)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.IsNotified, true)
                    .SetProperty(r => r.NotifiedAt, now));

            if (marked == 0) continue;

            var record = await db.WorkRecords.FindAsync(id);
            if (record == null) continue;

            try
            {
                // 下班提醒
                await lineService.PushAsync(record.LineUserId,
                    $"Ring Ring~鈴魚想回家啦！");
                offWorkNotified++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Push failed for {UserId}", record.LineUserId);
                // 推播失敗時重置標記，下次可重試
                await db.WorkRecords.Where(r => r.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.IsNotified, false)
                        .SetProperty(r => r.NotifiedAt, (DateTime?)null));
            }
        }

        return Ok(new { remindersSent, offWorkNotified, now = now.ToString("yyyy-MM-dd HH:mm:ss"), dueRemindersFound = dueReminderIds.Count, reminderErrors });
    }

}

