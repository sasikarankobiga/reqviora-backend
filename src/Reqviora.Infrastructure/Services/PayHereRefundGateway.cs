using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Reqviora.Infrastructure.Services;

public sealed class PayHereRefundGateway(HttpClient http, IOptions<BillingOptions> options) : IPayHereRefundGateway
{
    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record RefundResponse(int Status, string Msg, object? Data);

    public async Task<string> RefundAsync(string paymentId, string reason, long amountMinor, CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.RefundsEnabled || string.IsNullOrWhiteSpace(settings.AppId) ||
            string.IsNullOrWhiteSpace(settings.AppSecret))
            throw new InvalidOperationException("PayHere refunds are not configured.");

        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" })
        };
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AppId}:{settings.AppSecret}")));
        using var tokenResponse = await http.SendAsync(tokenRequest, ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("PayHere returned an invalid OAuth response.");

        using var refundRequest = new HttpRequestMessage(HttpMethod.Post, "payment/refund")
        {
            Content = JsonContent.Create(new
            {
                payment_id = paymentId,
                description = reason,
                amount = (amountMinor / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            })
        };
        refundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var refundResponse = await http.SendAsync(refundRequest, ct);
        refundResponse.EnsureSuccessStatusCode();
        var result = await refundResponse.Content.ReadFromJsonAsync<RefundResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("PayHere returned an invalid refund response.");
        if (result.Status != 1)
            throw new InvalidOperationException("PayHere rejected the refund request.");
        return result.Data?.ToString() ?? string.Empty;
    }
}
