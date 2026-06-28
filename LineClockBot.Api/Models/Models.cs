using LineClockBot.Api.Services;

namespace LineClockBot.Api.Models;

public class UserSetting
{
    public int Id { get; set; }
    public string LineUserId { get; set; } = "";
    public string? Nickname { get; set; }
    public double WorkHours { get; set; } = 8.0;
    public DateTime CreatedAt { get; set; } = TimeService.TaiwanNow;
    public DateTime UpdatedAt { get; set; } = TimeService.TaiwanNow;
}

public class WorkRecord
{
    public int Id { get; set; }
    public string LineUserId { get; set; } = "";
    public DateTime ClockInTime { get; set; }
    public DateTime OffWorkTime { get; set; }
    public bool IsNotified { get; set; } = false;
    public DateTime? NotifiedAt { get; set; }
    public DateTime CreatedAt { get; set; } = TimeService.TaiwanNow;
}

public class Reminder
{
    public int Id { get; set; }
    public string LineUserId { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public string Message { get; set; } = "";
    public DateTime? SentAt { get; set; }
    public string ReminderType { get; set; } = "";
    public DateTime CreatedAt { get; set; } = TimeService.TaiwanNow;
}

public class CreateReminderRequest
{
    public string LineUserId { get; set; } = "";
    public DateTime ScheduledAt { get; set; }
    public string Message { get; set; } = "";
    public string? ReminderType { get; set; }
}
