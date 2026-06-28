using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LineClockBot.Functions;

public class OffWorkReminderFunction(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<OffWorkReminderFunction> logger)
{
    // 每分鐘執行一次
    [Function("OffWorkReminder")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo timer)
    {
        var apiBase = config["Api:BaseUrl"];
        var apiKey = config["Api:InternalKey"];

        if (string.IsNullOrEmpty(apiBase) || string.IsNullOrEmpty(apiKey))
        {
            logger.LogError("Api:BaseUrl 或 Api:InternalKey 未設定");
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/internal/process-all-reminders");
            request.Headers.Add("X-Internal-Key", apiKey);

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogInformation("處理提醒完成：{Body}", body);
            }
            else
            {
                logger.LogWarning("處理提醒回傳非成功狀態：{Status}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "呼叫 process-all-reminders 失敗");
        }
    }
}
