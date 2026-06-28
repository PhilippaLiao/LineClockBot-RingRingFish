namespace LineClockBot.Api.Services;

public class TimeService
{
    private static readonly TimeZoneInfo TaiwanTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public static DateTime TaiwanNow => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TaiwanTz);
    public static DateTime TaiwanToday => TaiwanNow.Date;

    private static readonly string[] ClockInKeywords = ["上班", "打卡", "上班打卡"];
    private static readonly string[] ReminderKeywords = ["提醒"];

    /// <summary>
    /// 嘗試從訊息中解析打卡時間。
    /// 支援格式：「上班」「打卡 09:00」「上班打卡 9:30」「上班 09:30」
    /// 若未帶時間則使用當下時間。
    /// </summary>
    public bool TryParseClockIn(string message, out DateTime clockInTime)
    {
        clockInTime = default;
        message = message.Trim();

        var isClockIn = ClockInKeywords.Any(k => message.StartsWith(k));
        if (!isClockIn) return false;

        // 移除關鍵字，取剩餘部分嘗試解析時間
        var remaining = message;
        foreach (var keyword in ClockInKeywords.OrderByDescending(k => k.Length))
            remaining = remaining.Replace(keyword, "").Trim();

        if (string.IsNullOrEmpty(remaining))
        {
            // 沒有帶時間 → 使用現在時間
            clockInTime = TaiwanNow;
            return true;
        }

        // 將全形冒號、空格正規化為半形，方便解析
        remaining = remaining.Replace('：', ':').Replace('　', ' ');

        // 嘗試解析 HH:mm 或 H:mm
        if (TimeOnly.TryParse(remaining, out var time))
        {
            clockInTime = TaiwanToday.Add(time.ToTimeSpan());
            return true;
        }

        return false;
    }

    /// <summary>
    /// 嘗試從訊息中解析提醒內容與時間。
    /// 支援格式：
    ///   「提醒 限動 12:00」→ 今天 12:00
    ///   「提醒 限動 6/30 16:00」→ 6/30 16:00
    /// </summary>
    public bool TryParseReminder(string message, out string reminderText, out DateTime scheduledAt)
    {
        reminderText = "";
        scheduledAt = default;
        message = message.Trim();

        if (!message.StartsWith("提醒"))
            return false;

        var remaining = message[2..].Trim();
        if (string.IsNullOrEmpty(remaining))
            return false;

        // 正規化全形冒號、空格
        remaining = remaining.Replace('：', ':').Replace('　', ' ');

        var parts = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        var potentialTime = parts[^1];
        if (!TimeOnly.TryParse(potentialTime, out var time))
            return false;

        // 判斷倒數第二個 token 是否為日期（M/d 格式）
        DateTime dateBase;
        int textEndIndex;

        if (parts.Length >= 3 && TryParseDateMD(parts[^2], out var parsedDate))
        {
            dateBase = parsedDate;
            textEndIndex = parts.Length - 2;
        }
        else
        {
            dateBase = TaiwanToday;
            textEndIndex = parts.Length - 1;
        }

        if (textEndIndex == 0)
            return false;

        reminderText = string.Join(" ", parts[..textEndIndex]);
        scheduledAt = dateBase.Date.Add(time.ToTimeSpan());
        return true;
    }

    /// <summary>解析 M/d 格式（如 6/30），補上當年年份</summary>
    public bool TryParseDateMD(string s, out DateTime result)
    {
        result = default;
        var slash = s.IndexOf('/');
        if (slash <= 0 || slash >= s.Length - 1) return false;
        if (!int.TryParse(s[..slash], out var month)) return false;
        if (!int.TryParse(s[(slash + 1)..], out var day)) return false;
        try
        {
            result = new DateTime(TaiwanNow.Year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 嘗試從訊息中解析工時設定。
    /// 支援格式：「工時 8」「工時 8.5」「設定工時 9」
    /// </summary>
    public bool TryParseWorkHours(string message, out double workHours)
    {
        workHours = 0;
        message = message.Trim();

        var keywords = new[] { "設定工時", "工時" };
        string? remaining = null;

        foreach (var keyword in keywords.OrderByDescending(k => k.Length))
        {
            if (message.StartsWith(keyword))
            {
                remaining = message[keyword.Length..].Trim();
                break;
            }
        }

        if (remaining == null) return false;

        if (double.TryParse(remaining, out workHours) && workHours is > 0 and <= 24)
            return true;

        return false;
    }

    public DateTime CalculateOffWorkTime(DateTime clockIn, double workHours)
        => clockIn.AddHours(workHours);
}

