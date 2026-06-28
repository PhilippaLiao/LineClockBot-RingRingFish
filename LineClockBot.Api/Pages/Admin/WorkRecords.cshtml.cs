using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineClockBot.Api.Data;
using LineClockBot.Api.Models;

namespace LineClockBot.Api.Pages.Admin;

public class WorkRecordsModel(AppDbContext db) : AdminPageBase
{
    public const int PageSize = 30;

    public List<WorkRecord> WorkRecords { get; private set; } = [];
    public Dictionary<string, string> Nicknames { get; private set; } = [];
    public List<UserSetting> AllUsers { get; private set; } = [];
    public string? Message { get; private set; }
    public string UserFilter { get; private set; } = "";
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;

    public async Task<IActionResult> OnGetAsync(string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        var record = await db.WorkRecords.FindAsync(id);
        if (record != null)
        {
            db.WorkRecords.Remove(record);
            await db.SaveChangesAsync();
            Message = $"已刪除打卡紀錄 ID {id}";
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteBatchAsync(int[] ids, string userFilter = "", int pageNum = 1)
    {
        if (CheckAuth() is { } redirect) return redirect;

        UserFilter = userFilter;
        CurrentPage = Math.Max(1, pageNum);

        if (ids.Length > 0)
        {
            var records = await db.WorkRecords.Where(r => ids.Contains(r.Id)).ToListAsync();
            db.WorkRecords.RemoveRange(records);
            await db.SaveChangesAsync();
            Message = $"已刪除 {records.Count} 筆打卡紀錄";
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

        var query = db.WorkRecords.AsQueryable();
        if (!string.IsNullOrEmpty(UserFilter))
            query = query.Where(r => r.LineUserId == UserFilter);

        query = query.OrderByDescending(r => r.ClockInTime);

        var total = await query.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        CurrentPage = Math.Min(CurrentPage, TotalPages);

        WorkRecords = await query
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
