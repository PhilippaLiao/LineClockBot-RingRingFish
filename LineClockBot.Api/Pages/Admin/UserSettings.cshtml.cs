using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LineClockBot.Api.Data;
using LineClockBot.Api.Models;

namespace LineClockBot.Api.Pages.Admin;

public class UserSettingsModel(AppDbContext db) : AdminPageBase
{
    public List<UserSetting> UserSettings { get; private set; } = [];
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (CheckAuth() is { } redirect) return redirect;

        UserSettings = await db.UserSettings
            .OrderBy(s => s.LineUserId)
            .ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveNicknameAsync(int id, string nickname)
    {
        if (CheckAuth() is { } redirect) return redirect;

        var setting = await db.UserSettings.FindAsync(id);
        if (setting != null)
        {
            setting.Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
            setting.UpdatedAt = LineClockBot.Api.Services.TimeService.TaiwanNow;
            await db.SaveChangesAsync();
            Message = $"已更新 {setting.LineUserId} 的暱稱";
        }

        UserSettings = await db.UserSettings.OrderBy(s => s.LineUserId).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (CheckAuth() is { } redirect) return redirect;

        var setting = await db.UserSettings.FindAsync(id);
        if (setting != null)
        {
            db.UserSettings.Remove(setting);
            await db.SaveChangesAsync();
            Message = $"已刪除使用者設定 ID {id}（{setting.LineUserId}）";
        }

        UserSettings = await db.UserSettings
            .OrderBy(s => s.LineUserId)
            .ToListAsync();
        return Page();
    }
}
