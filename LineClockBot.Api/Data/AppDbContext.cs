using Microsoft.EntityFrameworkCore;
using LineClockBot.Api.Models;

namespace LineClockBot.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<WorkRecord> WorkRecords => Set<WorkRecord>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
}
