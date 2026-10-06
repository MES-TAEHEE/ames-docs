using System.Net.Http.Headers;
using System.Text;

namespace AMES.Api.Workers;

/// <summary>
/// SRM 연동 Worker(PoSync·DemandPlanSync) 공용 인증 적용기 — <c>Query:{이름}</c> / <c>Bearer</c> / <c>Basic</c> / <c>Header:{이름}</c> /
/// 그 외는 예외. 원래 <c>HttpPoSource.BuildRequest</c> 의 인증 분기를 옮긴 것으로 동작을 바꾸지 않는다.
/// </summary>
public static class SrmAuth
{
    public static (AuthenticationHeaderValue? Header, KeyValuePair<string, string>? ExtraHeader) Apply(
        List<KeyValuePair<string, string>> query, string? scheme, string? value)
    {
        AuthenticationHeaderValue? header = null;
        KeyValuePair<string, string>? extraHeader = null;
        if (!string.IsNullOrEmpty(scheme) && !string.IsNullOrEmpty(value))
        {
            if (scheme.StartsWith("Query:", StringComparison.OrdinalIgnoreCase))
            {
                var name = scheme["Query:".Length..].Trim();
                if (name.Length == 0) throw new InvalidOperationException("인증 방식 Query: 에 매개변수 이름이 없습니다");
                query.Add(new(name, value));
            }
            else if (scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
                header = new AuthenticationHeaderValue("Bearer", value);
            else if (scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase))
                header = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
            else if (scheme.StartsWith("Header:", StringComparison.OrdinalIgnoreCase))
            {
                var name = scheme["Header:".Length..].Trim();
                if (name.Length == 0) throw new InvalidOperationException("인증 방식 Header: 에 헤더 이름이 없습니다");
                extraHeader = new(name, value);
            }
            else
                throw new InvalidOperationException($"지원하지 않는 인증 방식: {scheme}");
        }
        return (header, extraHeader);
    }
}
