using AMES.Data.Repositories;
using AMES.Pop.Services;

namespace AMES.Pop.Common;

/// <summary>확정 뒤 BOM 자동 차감 결과를 작업자에게 — 부족은 경고 토스트, 실패는 토스트 + 파일 로그(확정 자체는 성공).</summary>
internal static class BackflushNotice
{
    public static void Show(ToastService toasts, BackflushResult r, string lotCode)
    {
        if (r.Error is { } err)
        {
            PopLog.Append("backflush", "BACKFLUSH", $"{lotCode}: {err}");
            toasts.Warn(PopLang.T("BackflushFailed"));
            return;
        }
        if (!r.HasShortage) return;
        var items = string.Join(", ", r.Shortages.Select(s => $"{s.PartNo} {s.Qty:0.###} {s.Uom}"));
        toasts.Warn($"{PopLang.T("MaterialShort")}: {items}");
    }
}
