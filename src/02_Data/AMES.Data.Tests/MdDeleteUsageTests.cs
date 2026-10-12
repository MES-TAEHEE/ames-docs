using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD 삭제 사용처 검사(품목·시간 패턴·작업장·라인·스테이션·고객·협력업체·설비) — 사용 중이면 지우지 않고 사용처를 돌려주고, 없을 때만 지운다.
// PM 템플릿은 참조하는 곳이 자기 점검 단계뿐이라 단계와 함께 지운다.
public class MdDeleteUsageTests
{
    const string Wc = "ITEST-WC-1012", Line = "ITEST-LN-1012", Station = "ITEST-ST-1012", Bop = "ITEST-BOP-1012";
    const string Tpl = "ITEST-PMT-1012", Step1 = "ITEST-PMS-1012-1", Step2 = "ITEST-PMS-1012-2";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Bop            WHERE BOPID = @B;
        DELETE dbo.MD_Station        WHERE StationCode = @S;
        DELETE dbo.MD_Line           WHERE LineID = @L;
        DELETE dbo.MD_WorkCenter     WHERE WCID = @W;
        DELETE dbo.MD_PmTemplateStep WHERE PMTemplateID = @T;
        DELETE dbo.MD_PmTemplate     WHERE PMTemplateID = @T;
        """, ("@B", Bop), ("@S", Station), ("@L", Line), ("@W", Wc), ("@T", Tpl));

    static int Count(AmesConnectionFactory f, string table, string col, string key)
        => (int)Scalar(f, $"SELECT COUNT(*) FROM dbo.{table} WHERE {col} = @K", ("@K", key))!;

    // 검사 SQL 이 실제 컬럼과 맞는지 — 각 마스터의 기존 행 하나로 사용처만 읽는다(쓰기 없음, 트랜잭션 롤백)
    [SkippableFact]
    public void Usage_queries_run_against_existing_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var md = new MasterDataRepository(f);
        string? First(string table, string col) => Scalar(f, $"SELECT TOP 1 {col} FROM dbo.{table} ORDER BY {col}") as string;

        foreach (var (table, col, check) in new (string, string, Func<string, List<MasterDataRepository.UsageRef>>)[]
        {
            ("MD_WorkCenter", "WCID",        md.WorkCenterUsages),
            ("MD_Line",       "LineID",      md.LineUsages),
            ("MD_Station",    "StationCode", md.StationUsages),
            ("MD_Customer",   "CustomerID",  md.CustomerUsages),
            ("MD_Vendor",     "VendorID",    md.VendorUsages),
            ("MD_Equipment",  "EquipID",     md.EquipmentUsages),
            ("MD_Item",       "ItemNo",      md.ItemUsages),
            ("MD_LineTimePattern", "PatternID", md.LineTimePatternUsages),
            ("MD_Mold",       "MoldID",      md.MoldUsages),
        })
        {
            Assert.Empty(check("ITEST-NONE-1012"));
            if (First(table, col) is { } key)
                Assert.All(check(key), u => Assert.True(u.Count > 0, $"{table} {key} {u.Kind}"));
        }
    }

    [SkippableFact]
    public void In_use_masters_are_kept_and_unused_ones_are_deleted()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            Exec(f, """
                INSERT dbo.MD_WorkCenter (WCID, ProcessCode, CreatedBy) VALUES (@W, 'INJ', 'itest');
                INSERT dbo.MD_Line       (LineID, WCID, CreatedBy)      VALUES (@L, @W, 'itest');
                INSERT dbo.MD_Station    (StationCode, LineID, OrderSeq, CreatedBy) VALUES (@S, @L, 1, 'itest');
                INSERT dbo.MD_Bop        (BOPID, StationCode, ItemNo, StepSeq, CreatedBy) VALUES (@B, @S, 'ITEST-ITEM', 10, 'itest');
                """, ("@W", Wc), ("@L", Line), ("@S", Station), ("@B", Bop));
            var md = new MasterDataRepository(f);

            var wc = md.DeleteWorkCenter(Wc);
            Assert.Equal("LINE", Assert.Single(wc).Kind);
            Assert.Equal(Line, wc[0].Samples);
            var line = md.DeleteLine(Line);
            Assert.Equal("STATION", Assert.Single(line).Kind);
            var st = md.DeleteStation(Station);
            Assert.Equal("BOP", Assert.Single(st).Kind);
            Assert.Equal("ITEST-ITEM #10", st[0].Samples);
            Assert.Equal(1, Count(f, "MD_Station", "StationCode", Station));
            Assert.Equal(1, Count(f, "MD_Line", "LineID", Line));
            Assert.Equal(1, Count(f, "MD_WorkCenter", "WCID", Wc));

            // 확인창 전 검사도 같은 결과
            Assert.Equal("STATION", Assert.Single(md.LineUsages(Line)).Kind);

            Exec(f, "DELETE dbo.MD_Bop WHERE BOPID = @B", ("@B", Bop));
            Assert.Empty(md.DeleteStation(Station));
            Assert.Empty(md.DeleteLine(Line));
            Assert.Empty(md.DeleteWorkCenter(Wc));
            Assert.Equal(0, Count(f, "MD_Station", "StationCode", Station));
            Assert.Equal(0, Count(f, "MD_Line", "LineID", Line));
            Assert.Equal(0, Count(f, "MD_WorkCenter", "WCID", Wc));
        }
        finally { Cleanup(f); }
    }

    // 공통코드 그룹 삭제 — DB 의 항목 전부(화면이 모르는 항목 포함)와 그룹을 한 번에 지우고 지운 항목을 돌려준다
    [SkippableFact]
    public void Code_group_is_deleted_with_all_its_items()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        const string G = "ITEST_GRP_1012";
        void Clean() => Exec(f, "DELETE dbo.MD_CodeItem WHERE GroupCode = @G; DELETE dbo.MD_CodeGroup WHERE GroupCode = @G;", ("@G", G));
        Clean();
        try
        {
            Exec(f, """
                INSERT dbo.MD_CodeGroup (GroupCode, CreatedBy) VALUES (@G, 'itest');
                INSERT dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, SortOrder, CreatedBy)
                VALUES (@G + '_A', @G, 'A', 10, 'itest'), (@G + '_B', @G, 'B', 20, 'itest');
                """, ("@G", G));
            var removed = new MasterDataRepository(f).DeleteCodeGroup(G);
            Assert.Equal(new[] { "A", "B" }, removed.Select(r => r.CodeValue));
            Assert.Equal(0, Count(f, "MD_CodeItem", "GroupCode", G));
            Assert.Equal(0, Count(f, "MD_CodeGroup", "GroupCode", G));
        }
        finally { Clean(); }
    }

    // 금형 삭제 — 라인 배정이 있으면 금형·색상 모두 남고, 없으면 색상과 함께 지운다
    [SkippableFact]
    public void Mold_in_use_is_kept_and_unused_mold_is_deleted_with_colors()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        const string M = "ITEST-MOLD-1012";
        void Clean() => Exec(f, "DELETE dbo.MD_MoldLine WHERE MoldID = @M; DELETE dbo.MD_MoldColor WHERE MoldID = @M; DELETE dbo.MD_Mold WHERE MoldID = @M;", ("@M", M));
        Clean();
        try
        {
            Exec(f, """
                INSERT dbo.MD_Mold      (MoldID, CreatedBy)                 VALUES (@M, 'itest');
                INSERT dbo.MD_MoldColor (MoldID, Color, CreatedBy)          VALUES (@M, 'NNB', 'itest');
                INSERT dbo.MD_MoldLine  (LineCode, MoldID, CreatedBy)       VALUES ('ITEST-LN-1012', @M, 'itest');
                """, ("@M", M));
            var md = new MasterDataRepository(f);
            var used = md.DeleteMold(M);
            Assert.Equal("MOLDLINE", Assert.Single(used).Kind);
            Assert.Equal("MOLDLINE", Assert.Single(md.MoldUsages(M)).Kind);
            Assert.Equal(1, Count(f, "MD_Mold", "MoldID", M));
            Assert.Equal(1, Count(f, "MD_MoldColor", "MoldID", M));

            Exec(f, "DELETE dbo.MD_MoldLine WHERE MoldID = @M", ("@M", M));
            Assert.Empty(md.DeleteMold(M));
            Assert.Equal(0, Count(f, "MD_Mold", "MoldID", M));
            Assert.Equal(0, Count(f, "MD_MoldColor", "MoldID", M));
        }
        finally { Clean(); }
    }

    [SkippableFact]
    public void Pm_template_is_deleted_with_its_steps()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            Exec(f, """
                INSERT dbo.MD_PmTemplate     (PMTemplateID, CreatedBy) VALUES (@T, 'itest');
                INSERT dbo.MD_PmTemplateStep (PMStepID, PMTemplateID, StepSeq, CreatedBy) VALUES (@S1, @T, 1, 'itest'), (@S2, @T, 2, 'itest');
                """, ("@T", Tpl), ("@S1", Step1), ("@S2", Step2));
            var md = new MasterDataRepository(f);

            Assert.Equal(2, md.PmTemplateStepCount(Tpl));
            md.DeletePmTemplate(Tpl);
            Assert.Equal(0, Count(f, "MD_PmTemplate", "PMTemplateID", Tpl));
            Assert.Equal(0, Count(f, "MD_PmTemplateStep", "PMTemplateID", Tpl));
        }
        finally { Cleanup(f); }
    }
}
