using AMES.Contracts.Enums;
using AMES.Data.Repositories;
using AMES.Data.Services;

namespace AMES.Pop.Common;

/// <summary>
/// INJ/IMG-MAIN 의 현재 팔레트 칩(번호 · 실린 수 / 적입수량)과 팔레트 관련 결과 → 안내 문자열 키.
/// 현재 팔레트는 화면 메모리에만 있다 — 재로그인·재시작하면 다시 스캔한다(2026-10-21 사용자 결정).
/// </summary>
internal static class PalletChip
{
    /// <summary>칩 문구와 색 — 팔레트가 없거나 가득 차면 빨강.</summary>
    public static (string Text, string Css) Of(PalletStockRepository.PalletStatus? p)
        => p is null ? ("—", "bad") : ($"{p.PalletNo} · {p.CapacityText}", p.IsFull ? "bad" : "ok");

    /// <summary>팔레트 스캔 결과 중 현재 팔레트로 삼을 수 있는 것 — 있고·사용 중·출고 안 됨. 혼적·가득 참은 칩에 보이게 받아 둔다.</summary>
    public static bool Accepts(PalletLoadOutcome o) => o is PalletLoadOutcome.Ok or PalletLoadOutcome.Mixed or PalletLoadOutcome.Full;

    public static string? NoticeKey(PalletLoadOutcome o) => o switch
    {
        PalletLoadOutcome.NotFound => "PalletNotFound",
        PalletLoadOutcome.Inactive => "PalletUnavailable",
        PalletLoadOutcome.Shipped  => "PalletUnavailable",
        PalletLoadOutcome.Mixed    => "PalletMixed",
        PalletLoadOutcome.Full     => "PalletFull",
        _                          => null,
    };

    public static string? NoticeKey(InjConfirmOutcome o) => o switch
    {
        InjConfirmOutcome.PalletNotFound    => "PalletNotFound",
        InjConfirmOutcome.PalletUnavailable => "PalletUnavailable",
        InjConfirmOutcome.PalletMixed       => "PalletMixed",
        InjConfirmOutcome.PalletFull        => "PalletFull",
        _                                   => null,
    };

    public static string? NoticeKey(ImgConfirmOutcome o) => o switch
    {
        ImgConfirmOutcome.PalletNotFound    => "PalletNotFound",
        ImgConfirmOutcome.PalletUnavailable => "PalletUnavailable",
        ImgConfirmOutcome.PalletMixed       => "PalletMixed",
        ImgConfirmOutcome.PalletFull        => "PalletFull",
        _                                   => null,
    };
}
