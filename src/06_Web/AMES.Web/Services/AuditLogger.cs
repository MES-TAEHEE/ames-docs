using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AMES.Data.Repositories;
using Microsoft.AspNetCore.Components.Authorization;

namespace AMES.Web.Services;

/// <summary>
/// 화면의 등록·수정·삭제를 SYS_AuditLog 에 남기는 공용 창구. 화면은 저장이 성공한 직후 한 줄로 부른다.
/// 감사 기록 실패가 이미 끝난 저장을 실패로 보이게 하면 안 되므로 예외는 경고 로그로만 남긴다.
/// </summary>
public sealed class AuditLogger
{
    readonly SysRepository _sys;
    readonly AuthenticationStateProvider _auth;
    readonly IHttpContextAccessor _http;
    readonly ILogger<AuditLogger> _log;
    readonly string _ipAtStart;

    public AuditLogger(SysRepository sys, AuthenticationStateProvider auth, IHttpContextAccessor http, ILogger<AuditLogger> log)
    {
        _sys = sys; _auth = auth; _http = http; _log = log;
        // 회로로 넘어간 뒤에는 HttpContext 가 없을 수 있어 만들어질 때 한 번 잡아 둔다
        _ipAtStart = ToIpv4(http.HttpContext?.Connection.RemoteIpAddress);
    }

    public void Created(string screenCode, string entity, object? id, object? after)
        => Log(screenCode, "CREATE", entity, id, null, after);

    public void Updated(string screenCode, string entity, object? id, object? before, object? after)
        => Log(screenCode, "UPDATE", entity, id, before, after);

    public void Deleted(string screenCode, string entity, object? id, object? before)
        => Log(screenCode, "DELETE", entity, id, before, null);

    /// <summary>CREATE·UPDATE·DELETE 밖의 동작(APPROVE·COPY·PIN_RESET·UNLOCK 등). action 은 15자 이내.</summary>
    public void Log(string screenCode, string action, string entity, object? id,
                    object? before, object? after, string? note = null, string? actor = null)
    {
        try
        {
            // actor 는 로그인 전 화면(자기가입·비밀번호 재설정)처럼 인증 상태가 없을 때만 호출자가 준다
            actor = Cut(string.IsNullOrWhiteSpace(actor) ? CurrentActor() : actor, 50)!;   // CreatedBy VARCHAR(50)
            var target = Cut(Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture), 40);
            // 화면 코드가 아닌 구역 이름(ACCOUNT 등)은 SYS 모듈로 묶어 SYS-007 모듈 필터에 걸리게 한다
            var module = screenCode.Contains('-') ? screenCode.Split('-')[0] : "SYS";
            note ??= $"{action} {entity} '{target}' by '{actor}'";
            _sys.InsertAuditLog(Cut(module, 10), Cut(screenCode, 20), Cut(action, 15)!, Cut(entity, 40), target,
                ToJson(before), ToJson(after), actor, CurrentIp(), note: Cut(note, 500));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Audit log skipped — {Screen} {Action} {Entity} {Id}", screenCode, action, entity, id);
        }
    }

    string CurrentActor()
    {
        // Blazor Server 의 인증 상태 Task 는 이미 끝나 있다 — 아니면 기다리지 않고 system 으로 남긴다
        var t = _auth.GetAuthenticationStateAsync();
        return t.IsCompletedSuccessfully ? t.Result.User.Identity?.Name ?? "system" : "system";
    }

    string? CurrentIp()
    {
        var now = ToIpv4(_http.HttpContext?.Connection.RemoteIpAddress);
        var ip  = now.Length > 0 ? now : _ipAtStart;
        return ip.Length > 0 ? ip : null;
    }

    static string ToIpv4(IPAddress? ip)
    {
        if (ip is null) return "";
        if (ip.IsIPv4MappedToIPv6) return ip.MapToIPv4().ToString();
        if (ip.Equals(IPAddress.IPv6Loopback)) return "127.0.0.1";
        return ip.ToString();
    }

    static string? Cut(string? s, int max) => s is null || s.Length <= max ? s : s[..max];

    // ── JSON 스냅샷 ──────────────────────────────────────────────────────
    // 화면의 폼 모델·행 DTO 를 그대로 받으므로, 비밀값은 이름으로 걸러 내고 바이트 배열은 크기만 남긴다.
    // PascalCase 단어 경계로 본다 — "Pin" 은 거르되 "Shipping"·"Pinion" 은 남긴다
    static readonly System.Text.RegularExpressions.Regex SecretName =
        new("(Password|Secret|Token|Hash|Pin)(?![a-z])", System.Text.RegularExpressions.RegexOptions.Compiled);

    static readonly JsonSerializerOptions JsonOpt = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        IncludeFields = false,
        Converters = { new ByteSizeConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { DropSecrets } },
    };

    static void DropSecrets(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        for (int i = info.Properties.Count - 1; i >= 0; i--)
        {
            if (SecretName.IsMatch(info.Properties[i].Name)) info.Properties.RemoveAt(i);
        }
    }

    /// <summary>키·그룹 이름이 비밀값을 담는 종류인지(설정 키, 공통코드 그룹 등 — 값이 든 속성 이름으로는 알 수 없을 때).</summary>
    public static bool IsSecretKey(string? key)
        => key is not null && System.Text.RegularExpressions.Regex.IsMatch(key, "PASSWORD|SECRET|TOKEN|APIKEY|AUTH|_KEY",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>스냅샷에서 지정한 속성 값을 *** 로 가린 JSON 을 돌려준다(중첩·배열 포함). 결과는 Created/Updated/Deleted 에 그대로 넘긴다.</summary>
    public static string? Redact(object? o, params string[] propertyNames)
    {
        var json = ToJson(o);
        if (json is null) return null;
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            Mask(node, propertyNames);
            return node?.ToJsonString(JsonOpt);
        }
        catch { return "{\"redacted\":true}"; }   // 가리지 못하면 원문을 남기지 않는다
    }

    static void Mask(System.Text.Json.Nodes.JsonNode? node, string[] names)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                if (names.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    if (obj[key] is not null && obj[key]!.ToJsonString() is not ("\"\"" or "null")) obj[key] = "***";
                }
                else Mask(obj[key], names);
            }
        }
        else if (node is System.Text.Json.Nodes.JsonArray arr)
            foreach (var child in arr) Mask(child, names);
    }

    static string? ToJson(object? o)
    {
        if (o is null) return null;
        if (o is string s) return s;
        try { return JsonSerializer.Serialize(o, o.GetType(), JsonOpt); }
        catch (Exception ex) { return JsonSerializer.Serialize(new { snapshotError = ex.Message }); }
    }

    sealed class ByteSizeConverter : JsonConverter<byte[]>
    {
        public override byte[]? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o) => null;
        public override void Write(Utf8JsonWriter w, byte[] v, JsonSerializerOptions o) => w.WriteStringValue($"({v.Length} bytes)");
    }
}
