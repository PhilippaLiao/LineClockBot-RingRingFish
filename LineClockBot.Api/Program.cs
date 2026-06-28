using Microsoft.EntityFrameworkCore;
using LineClockBot.Api.Data;
using LineClockBot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// 資料庫：SQLite，檔案放在 D:\home（Azure App Service 持久化路徑）
var dbPath = builder.Environment.IsProduction()
    ? @"D:\home\lineclockbot.db"
    : Path.Combine(builder.Environment.ContentRootPath, "lineclockbot.db");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddHttpClient<LineService>();
builder.Services.AddScoped<TimeService>();
builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// 自動建立資料表（首次啟動時）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSession();
app.UseStaticFiles();
app.MapControllers();
app.MapRazorPages();
app.Run();
