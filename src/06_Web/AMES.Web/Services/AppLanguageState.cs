using AMES.Data.Repositories;
using Microsoft.AspNetCore.Localization;

namespace AMES.Web.Services;

/// <summary>
/// SYS_Config <c>LANGUAGE_DEFAULT</c> 캐시 — 컬처 프로바이더/언어 스위처 판정용.
/// IsActive=false → 앱을 en-US 로 강제하고 언어 선택 UI 숨김. IsActive=true → 기존(쿠키/기본) 유지.
/// DB를 매 요청 읽지 않도록 캐시하며, Config 저장 시 <see cref="Invalidate"/> 로 갱신.
/// </summary>
public sealed class AppLanguageState
{
    const string Key = "LANGUAGE_DEFAULT";
    readonly SysRepository _sys;
    readonly object _lock = new();
    (bool IsActive, string Value)? _cache;

    public AppLanguageState(SysRepository sys) => _sys = sys;

    public (bool IsActive, string Value) Get()
    {
        lock (_lock)
        {
            if (_cache is { } c) return c;
            (bool IsActive, string Value) result;
            try
            {
                var row = _sys.GetConfigFlag(Key);
                result = row is { } r
                    ? (r.IsActive, string.IsNullOrWhiteSpace(r.Value) ? "en-US" : r.Value!)
                    : (true, "ko-KR");   // 설정 행 없으면 기존 기본(한국어) 유지
            }
            catch { result = (true, "ko-KR"); }  // DB 미가용 시 안전 폴백
            _cache = result;
            return result;
        }
    }

    /// <summary>언어 선택 UI 노출 여부 = LANGUAGE_DEFAULT 활성.</summary>
    public bool SwitcherVisible => Get().IsActive;

    /// <summary>비활성 시 강제 컬처(en-US), 활성 시 null(쿠키/기본으로 폴백).</summary>
    public string? ForcedCulture => Get().IsActive ? null : "en-US";

    public void Invalidate() { lock (_lock) _cache = null; }
}

/// <summary>
/// LANGUAGE_DEFAULT 비활성 시 en-US 를 강제(쿠키보다 우선). 활성 시 null 반환 → 기존 프로바이더로 폴백.
/// RequestLocalizationOptions.RequestCultureProviders 맨 앞에 등록.
/// </summary>
public sealed class LanguageDefaultCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var forced = httpContext.RequestServices.GetService<AppLanguageState>()?.ForcedCulture;
        return Task.FromResult(forced is null
            ? null
            : new ProviderCultureResult(forced, "en"));   // culture=en-US, uiCulture=en
    }
}
