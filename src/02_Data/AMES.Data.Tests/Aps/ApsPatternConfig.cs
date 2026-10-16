using AMES.Data.Aps;
using AMES.Data.Connection;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests.Aps;

/// <summary>
/// 공유 개발 DB 의 APS 가동 시간 패턴 설정(APS_SETTING.DEFAULT_PATTERN · MD_ApsLineStage.PatternID)을 테스트 동안만 바꾸고 되돌린다.
/// 2026-10-06 부터 APS 는 이 설정이 없는 사출 라인이 나오면 조회·WO 생성을 막으므로, 통합 테스트는 자기 패턴을 여기로 지정한다.
/// Apply 가 처음 만지는 행의 원래 값을 기억하고 Restore 가 그대로 돌려놓는다(행이 없었으면 삭제) — 같은 컬렉션("AMES_DEV plan week")이라 직렬 실행.
/// Cleanup 이 테스트 패턴을 지우기 전에 Restore 를 불러야 FK(FK_MD_ApsLineStage_Pattern)에 걸리지 않는다.
/// </summary>
internal static class ApsPatternConfig
{
    const string Actor = "ITEST-APS-PAT";
    static string? _savedDefault;
    static bool _defaultSaved;
    static readonly Dictionary<string, (bool Existed, string? PatternId)> _savedLines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>기본 패턴을 defaultPattern(null = 비움)으로, 각 라인의 MD_ApsLineStage.PatternID 를 지정값으로.</summary>
    public static void Apply(AmesConnectionFactory f, string? defaultPattern, params (string Line, string? Pattern)[] lines)
    {
        if (!_defaultSaved)
        {
            _savedDefault = Scalar(f, "SELECT Attribute1 FROM dbo.MD_CodeItem WHERE GroupCode = @G AND CodeValue = @K;",
                                   ("@G", ApsSettingsLoader.GroupSetting), ("@K", ApsSettingsLoader.KeyDefaultPattern)) as string;
            _defaultSaved = true;
        }
        SetDefault(f, defaultPattern);
        foreach (var (line, pattern) in lines)
        {
            if (!_savedLines.ContainsKey(line))
            {
                var cur = Scalar(f, "SELECT ISNULL(PatternID, '') FROM dbo.MD_ApsLineStage WHERE LineID = @L;", ("@L", line)) as string;
                _savedLines[line] = (cur is not null, string.IsNullOrEmpty(cur) ? null : cur);
            }
            Exec(f, """
                UPDATE dbo.MD_ApsLineStage SET PatternID = @P WHERE LineID = @L;
                IF @@ROWCOUNT = 0
                    INSERT INTO dbo.MD_ApsLineStage (LineID, OffsetDays, UseStock, PatternID, CreatedBy) VALUES (@L, 1, 1, @P, @By);
                """, ("@L", line), ("@P", (object?)pattern ?? DBNull.Value), ("@By", Actor));
        }
    }

    public static void Restore(AmesConnectionFactory f)
    {
        foreach (var (line, (existed, pattern)) in _savedLines)
            Exec(f, existed ? "UPDATE dbo.MD_ApsLineStage SET PatternID = @P WHERE LineID = @L;" : "DELETE FROM dbo.MD_ApsLineStage WHERE LineID = @L;",
                 ("@L", line), ("@P", (object?)pattern ?? DBNull.Value));
        _savedLines.Clear();
        if (_defaultSaved) { SetDefault(f, _savedDefault); _defaultSaved = false; _savedDefault = null; }
    }

    static void SetDefault(AmesConnectionFactory f, string? value) => Exec(f, """
        UPDATE dbo.MD_CodeItem SET Attribute1 = @V WHERE GroupCode = @G AND CodeValue = @K;
        IF @@ROWCOUNT = 0
            INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
            VALUES (@G + '_' + @K, @G, @K, @K, @K, @V, 10, 1, @By, SYSDATETIME());
        """, ("@G", ApsSettingsLoader.GroupSetting), ("@K", ApsSettingsLoader.KeyDefaultPattern), ("@V", (object?)value ?? DBNull.Value), ("@By", Actor));
}
