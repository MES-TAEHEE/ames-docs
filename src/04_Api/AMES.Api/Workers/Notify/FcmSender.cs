using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AMES.Data.Repositories;

namespace AMES.Api.Workers.Notify;

public sealed record PushSendResult(bool Ok, bool Permanent, bool InvalidToken, string? Error);

public interface IPushSender
{
    /// <summary>키 파일이 없거나 읽을 수 없으면 null 이 아닌 사유 — 그때 발송기는 행을 잡지 않고 PENDING 으로 둔다.</summary>
    string? ConfigError { get; }
    Task<PushSendResult> SendAsync(NotificationRepository.PushItem item, CancellationToken ct);
}

/// <summary>
/// FCM HTTP v1 발송 — 서비스 계정 키(JSON 파일, 설정 <c>Notify:FcmServiceAccountPath</c>)로 OAuth 토큰을 받아 보낸다.
/// 키는 약 2KB 라 공통코드 설명 칸(500자)에 들어가지 않아 서버 파일로 둔다(Git·게시본에 넣지 않는다).
/// </summary>
public sealed class FcmSender(IHttpClientFactory http, IConfiguration cfg, ILogger<FcmSender> log) : IPushSender
{
    public const string ClientName = "fcm";
    const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    sealed record Account(string ProjectId, string ClientEmail, string PrivateKey, string TokenUri);

    readonly SemaphoreSlim _tokenGate = new(1, 1);
    Account? _account;
    string? _loadedPath;
    string? _loadError;
    string? _accessToken;
    DateTimeOffset _accessExpires;

    public string? ConfigError => LoadAccount() is null ? _loadError : null;

    Account? LoadAccount()
    {
        var path = cfg["Notify:FcmServiceAccountPath"]?.Trim();
        if (string.IsNullOrEmpty(path)) { _account = null; _loadError = "FCM 서비스 계정 키 경로(Notify:FcmServiceAccountPath)가 비어 있습니다 — 알림은 PENDING 으로 쌓입니다."; return null; }
        if (_account is not null && path == _loadedPath) return _account;
        try
        {
            var j = JsonNode.Parse(File.ReadAllText(path))!;
            _account = new Account(
                (string?)j["project_id"] ?? throw new InvalidDataException("project_id"),
                (string?)j["client_email"] ?? throw new InvalidDataException("client_email"),
                (string?)j["private_key"] ?? throw new InvalidDataException("private_key"),
                (string?)j["token_uri"] ?? "https://oauth2.googleapis.com/token");
            _loadedPath = path; _loadError = null; _accessToken = null;
            return _account;
        }
        catch (Exception ex)
        {
            _account = null;
            _loadError = $"FCM 서비스 계정 키를 읽지 못했습니다({path}): {ex.GetType().Name} {ex.Message}";
            return null;
        }
    }

    public async Task<PushSendResult> SendAsync(NotificationRepository.PushItem item, CancellationToken ct)
    {
        var acc = LoadAccount();
        if (acc is null) return new(false, false, false, _loadError);
        try
        {
            var token = await AccessTokenAsync(acc, ct);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{acc.ProjectId}/messages:send")
            {
                Content = new StringContent(FcmMessage.Build(item), Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await http.CreateClient(ClientName).SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return new(true, false, false, null);
            var body = await res.Content.ReadAsStringAsync(ct);
            if ((int)res.StatusCode == 401) _accessToken = null;   // 다음 시도에 토큰을 다시 받는다
            var (permanent, invalidToken) = FcmMessage.Classify((int)res.StatusCode, body);
            return new(false, permanent, invalidToken, $"FCM {(int)res.StatusCode}: {Trim(body, 400)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "FCM send failed for notification {Id}", item.Id);
            return new(false, false, false, $"{ex.GetType().Name}: {Trim(ex.Message, 400)}");
        }
    }

    async Task<string> AccessTokenAsync(Account acc, CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessExpires) return _accessToken;
            var now = DateTimeOffset.UtcNow;
            var jwt = FcmMessage.SignJwt(acc.ClientEmail, acc.PrivateKey, acc.TokenUri, Scope, now);
            using var res = await http.CreateClient(ClientName).PostAsync(acc.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"]  = jwt,
            }), ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new HttpRequestException($"OAuth {(int)res.StatusCode}: {Trim(text, 300)}");
            var j = JsonNode.Parse(text)!;
            _accessToken   = (string?)j["access_token"] ?? throw new InvalidDataException("access_token");
            _accessExpires = now.AddSeconds(((int?)j["expires_in"] ?? 3600) - 300);
            return _accessToken;
        }
        finally { _tokenGate.Release(); }
    }

    static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>FCM 메시지·응답 해석·서명 — 순수 함수(테스트 대상).</summary>
public static class FcmMessage
{
    /// <summary>안드로이드 알림 채널 ID — 앱이 같은 ID 로 채널을 만들어야 소리·중요도가 적용된다.</summary>
    public const string AndroidChannelId = "ames_alerts";

    public static string Build(NotificationRepository.PushItem item)
    {
        var msg = new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["token"] = item.Token,
                ["notification"] = new JsonObject
                {
                    ["title"] = item.Subject ?? item.EventTypeCode ?? "AMES",
                    ["body"]  = FirstLines(item.Body, 3),
                },
                // 앱이 눌렀을 때 상세(GET /api/notifications/{historyId})를 여는 데 쓴다. data 값은 문자열만 된다.
                ["data"] = new JsonObject
                {
                    ["historyId"] = item.Id.ToString(),
                    ["eventType"] = item.EventTypeCode ?? "",
                    ["refType"]   = item.SourceRefType ?? "",
                    ["refId"]     = item.SourceRefId ?? "",
                },
                ["android"] = new JsonObject
                {
                    ["priority"] = "high",
                    ["notification"] = new JsonObject { ["channel_id"] = AndroidChannelId },
                },
            },
        };
        return msg.ToJsonString();
    }

    /// <summary>
    /// 실패 응답 → (다시 보내도 안 되는가, 토큰이 무효인가). 404·UNREGISTERED 와 토큰 관련 INVALID_ARGUMENT 는 토큰 무효(기기 행을 끈다),
    /// 그 밖의 400 은 메시지 자체 문제라 재시도하지 않는다. 401·403·429·5xx 는 일시 오류로 보고 다시 보낸다.
    /// </summary>
    public static (bool Permanent, bool InvalidToken) Classify(int status, string? body)
    {
        var b = body ?? "";
        if (status == 404 || b.Contains("UNREGISTERED", StringComparison.Ordinal)) return (true, true);
        if (status == 400)
        {
            var tokenProblem = b.Contains("registration token", StringComparison.OrdinalIgnoreCase)
                            || b.Contains("message.token", StringComparison.OrdinalIgnoreCase);
            return (true, tokenProblem);
        }
        return (false, false);
    }

    /// <summary>서비스 계정 OAuth 용 RS256 JWT.</summary>
    public static string SignJwt(string clientEmail, string privateKeyPem, string audience, string scope, DateTimeOffset now)
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var iat = now.ToUnixTimeSeconds();
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new { iss = clientEmail, scope, aud = audience, iat, exp = iat + 3600 }));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var sig = rsa.SignData(Encoding.ASCII.GetBytes(header + "." + claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return header + "." + claims + "." + B64(sig);
    }

    static string FirstLines(string? body, int n)
        => body is null ? "" : string.Join("\n", body.Split('\n').Take(n));
}
