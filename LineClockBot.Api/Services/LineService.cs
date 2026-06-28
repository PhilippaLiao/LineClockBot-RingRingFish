namespace LineClockBot.Api.Services;

public class LineService(HttpClient httpClient, IConfiguration config)
{
    private readonly string _channelAccessToken = config["Line:ChannelAccessToken"]
        ?? throw new InvalidOperationException("Line:ChannelAccessToken is not configured.");

    /// <summary>回覆訊息（Webhook 收到後立即回覆）</summary>
    public async Task ReplyAsync(string replyToken, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/reply");
        request.Headers.Add("Authorization", $"Bearer {_channelAccessToken}");
        request.Content = JsonContent.Create(new
        {
            replyToken,
            messages = new[] { new { type = "text", text = message } }
        });

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>主動推播訊息（下班提醒用）</summary>
    public async Task PushAsync(string lineUserId, string message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
        request.Headers.Add("Authorization", $"Bearer {_channelAccessToken}");
        request.Content = JsonContent.Create(new
        {
            to = lineUserId,
            messages = new[] { new { type = "text", text = message } }
        });

        var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
