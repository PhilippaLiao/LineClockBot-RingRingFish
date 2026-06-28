using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineClockBot.Api.Data;
using LineClockBot.Api.Models;
using LineClockBot.Api.Services;

namespace LineClockBot.Api.Pages.Admin;

public class RemindersModel(AppDbContext db) : AdminPageBase
{
    public const int PageSize = 30;

    public List<Reminder> Reminders { get; private set; } = [];
    public Dictionary<string, string> Nicknames { get; private set; } = [];
    public List<UserSetting> AllUsers { get; private set; } = [];
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public string Filter { get; private set; } = "all";
    public string UserFilter { get; private set; } = "";
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;

    public async Task<IActionResult> OnGetAsync(string filter = "all", string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        Filter = filter;
        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(string lineUserId, DateOnly scheduledDate, string scheduledTime, string message,
        string filter = "all", string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        Filter = filter;
        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        if (!TimeOnly.TryParse(scheduledTime, out var time))
        {
            Message = "時間格式錯誤，請輸入 HH:mm，例如 11:00";
            IsError = true;
            await LoadAsync();
            return Page();
        }

        var scheduledAt = scheduledDate.ToDateTime(time);

        db.Reminders.Add(new Reminder
        {
            LineUserId = lineUserId,
            ScheduledAt = scheduledAt,
            Message = message,
            ReminderType = "custom"
        });
        await db.SaveChangesAsync();

        Message = $"已新增提醒：{message}（{scheduledAt:yyyy-MM-dd HH:mm}）";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string filter = "all", string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        Filter = filter;
        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        var reminder = await db.Reminders.FindAsync(id);
        if (reminder != null)
        {
            db.Reminders.Remove(reminder);
            await db.SaveChangesAsync();
            Message = $"已刪除提醒 ID {id}";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteBatchAsync(int[] ids, string filter = "all", string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        Filter = filter;
        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        if (ids.Length > 0)
        {
            var reminders = await db.Reminders.Where(r => ids.Contains(r.Id)).ToListAsync();
            db.Reminders.RemoveRange(reminders);
            await db.SaveChangesAsync();
            Message = $"已刪除 {reminders.Count} 筆提醒";
        }

        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        AllUsers = await db.UserSettings.OrderBy(s => s.LineUserId).ToListAsync();
        Nicknames = AllUsers
            .Where(s => s.Nickname != null)
            .ToDictionary(s => s.LineUserId, s => s.Nickname!);

        var query = db.Reminders.AsQueryable();
        if (Filter == "pending") query = query.Where(r => r.SentAt == null);
        else if (Filter == "sent") query = query.Where(r => r.SentAt != null);

        if (!string.IsNullOrEmpty(UserFilter))
            query = query.Where(r => r.LineUserId == UserFilter);

        query = query.OrderByDescending(r => r.ScheduledAt);

        var total = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        CurrentPage = Math.Min(CurrentPage, TotalPages);

        Reminders = await query
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
