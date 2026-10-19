using AMES.Data.Services;

namespace AMES.Web.Services;

/// <summary>엑셀 올리기 미리보기 — 등록할 것(키 목록)·건너뛸 것(안내 문구)·문제(등록하지 않음).</summary>
public sealed record ExcelImportPreview(IReadOnlyList<string> NewKeys, IReadOnlyList<string> SkipNotes, IReadOnlyList<MasterExcel.Problem> Problems);

/// <summary>엑셀 올리기 결과 — 등록 건수·건너뜀(등록 직전에 다른 사용자가 먼저 만든 것 포함)·실패 문구.</summary>
public sealed record ExcelImportOutcome(int Created, int Skipped, IReadOnlyList<string> Failures);
