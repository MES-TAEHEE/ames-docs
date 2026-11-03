using AMES.Data.Services.Srm;

namespace AMES.Web.Services;

/// <summary>SRM MM31006 구매오더 리스트 → 수주 행. 파일 읽기만 하고 열 판정·행 규칙은 AMES.Data PoListGrid(열 머리글 기준).</summary>
public static class PoListParser
{
    public static IReadOnlyList<PoListGrid.PoRow> Parse(Stream stream) => PoListGrid.Parse(SrmFileReader.ReadGrid(stream));
}
