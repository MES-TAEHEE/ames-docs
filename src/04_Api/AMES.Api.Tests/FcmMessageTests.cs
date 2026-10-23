using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AMES.Api.Workers.Notify;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Api.Tests;

// 10-24 PUSH 발송기 — FCM HTTP v1 메시지 모양(앱이 눌렀을 때 쓰는 data), 실패 응답 분류(토큰 무효·재시도), 서비스 계정 JWT 서명
public class FcmMessageTests
{
    static NotificationRepository.PushItem Item(string? body = "Equipment: INJ-01\nSeverity: MAJOR\nSymptom: noise\nReported: 2026-10-24 08:00")
        => new(42, "tok-123", "Equipment failure FAIL-2610-001", body, "FAILURE_OPEN", "FAILURE", "FAIL-2610-001", 0);

    [Fact]
    public void Message_carries_title_body_and_string_data_for_the_app()
    {
        var m = JsonNode.Parse(FcmMessage.Build(Item()))!["message"]!;
        Assert.Equal("tok-123", (string?)m["token"]);
        Assert.Equal("Equipment failure FAIL-2610-001", (string?)m["notification"]!["title"]);
        Assert.Equal("Equipment: INJ-01\nSeverity: MAJOR\nSymptom: noise", (string?)m["notification"]!["body"]);   // 앞 3줄
        var d = m["data"]!;
        Assert.Equal("42", (string?)d["historyId"]);
        Assert.Equal("FAILURE_OPEN", (string?)d["eventType"]);
        Assert.Equal("FAILURE", (string?)d["refType"]);
        Assert.Equal("FAIL-2610-001", (string?)d["refId"]);
        Assert.Equal("high", (string?)m["android"]!["priority"]);
        Assert.Equal(FcmMessage.AndroidChannelId, (string?)m["android"]!["notification"]!["channel_id"]);
    }

    [Fact]
    public void Message_without_body_still_builds()
        => Assert.Equal("", (string?)JsonNode.Parse(FcmMessage.Build(Item(null)))!["message"]!["notification"]!["body"]);

    [Theory]
    [InlineData(404, "", true, true)]
    [InlineData(400, "{\"error\":{\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}", true, true)]
    [InlineData(400, "The registration token is not a valid FCM registration token", true, true)]
    [InlineData(400, "Invalid value at 'message.data'", true, false)]
    [InlineData(401, "", false, false)]
    [InlineData(429, "", false, false)]
    [InlineData(503, "", false, false)]
    public void Failures_are_classified(int status, string body, bool permanent, bool invalidToken)
        => Assert.Equal((permanent, invalidToken), FcmMessage.Classify(status, body));

    [Fact]
    public void Jwt_is_signed_with_the_service_account_key()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var now = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var jwt = FcmMessage.SignJwt("svc@proj.iam.gserviceaccount.com", pem, "https://oauth2.googleapis.com/token",
            "https://www.googleapis.com/auth/firebase.messaging", now);

        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        static byte[] Dec(string s) { s = s.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=')); }
        var claims = JsonNode.Parse(Dec(parts[1]))!;
        Assert.Equal("svc@proj.iam.gserviceaccount.com", (string?)claims["iss"]);
        Assert.Equal("https://oauth2.googleapis.com/token", (string?)claims["aud"]);
        Assert.Equal(now.ToUnixTimeSeconds() + 3600, (long?)claims["exp"]);
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Dec(parts[2]),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
