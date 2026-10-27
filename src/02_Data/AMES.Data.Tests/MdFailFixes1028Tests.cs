using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD 단위테스트 결과서 FAIL 조치(10-28) — 값 규칙(순수 함수)과 삭제 사용처·길이 검사·공통코드 항목 삭제 제한(DB, 쓰기라 로컬 DB 권장)
public class MdFailFixes1028Tests
{
    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData(5.0, 1.0, 10.0, null)]
    [InlineData(null, 10.0, 1.0, "LslAboveUsl")]
    [InlineData(0.5, 1.0, 10.0, "NominalOutsideSpec")]
    [InlineData(11.0, 1.0, 10.0, "NominalOutsideSpec")]
    [InlineData(11.0, 1.0, null, null)]
    [InlineData(1.0, 1.0, 1.0, null)]
    public void Inspection_spec_requires_lsl_nominal_usl_order(double? nominal, double? lsl, double? usl, string? expected)
        => Assert.Equal(expected, MdValueRules.InspectionSpec((decimal?)nominal, (decimal?)lsl, (decimal?)usl));

    [Fact]
    public void Ranges_follow_the_decided_limits()
    {
        Assert.Null(MdValueRules.RfidHeatRating(null));
        Assert.Null(MdValueRules.RfidHeatRating(0));
        Assert.Null(MdValueRules.RfidHeatRating(300));
        Assert.Equal("HeatRatingRange", MdValueRules.RfidHeatRating(-1));
        Assert.Equal("HeatRatingRange", MdValueRules.RfidHeatRating(301));

        Assert.Null(MdValueRules.RalCure(180, 20, 70));
        Assert.Null(MdValueRules.RalCure(null, null, null));
        Assert.Equal("CureTempRange", MdValueRules.RalCure(0, 20, 70));
        Assert.Equal("CureTempRange", MdValueRules.RalCure(301, 20, 70));
        Assert.Equal("CureDurationRange", MdValueRules.RalCure(180, -5, 70));
        Assert.Equal("CureDurationRange", MdValueRules.RalCure(180, 601, 70));
        Assert.Equal("ElectroVRange", MdValueRules.RalCure(180, 20, 0));
        Assert.Equal("ElectroVRange", MdValueRules.RalCure(180, 20, 501));
    }

    [Fact]
    public void Packaging_quantities_and_weights_are_checked()
    {
        Assert.Null(MdValueRules.Packaging(10, 5, 20, 0.5m, 0.6m));
        Assert.Null(MdValueRules.Packaging(null, null, null, null, null));
        Assert.Equal("PackQtyMin", MdValueRules.Packaging(0, 5, 20, null, null));
        Assert.Equal("PackQtyMin", MdValueRules.Packaging(10, -1, 20, null, null));
        Assert.Equal("PackQtyMin", MdValueRules.Packaging(10, 5, 0, null, null));
        Assert.Equal("PackWeightPositive", MdValueRules.Packaging(10, 5, 20, 0m, 1m));
        Assert.Equal("PackWeightPositive", MdValueRules.Packaging(10, 5, 20, null, -1m));
        Assert.Equal("NetAboveGross", MdValueRules.Packaging(10, 5, 20, 2m, 1m));
    }

    [Fact]
    public void Period_and_group_code_rules()
    {
        Assert.Null(MdValueRules.Period(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1)));
        Assert.Null(MdValueRules.Period(null, new DateOnly(2026, 1, 1)));
        Assert.Equal("PeriodReversed", MdValueRules.Period(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)));

        Assert.Null(MdValueRules.GroupCode("WORK_SHIFT"));
        Assert.Null(MdValueRules.GroupCode("aps_setting2"));   // 저장 때 대문자로 바꾼다
        Assert.Equal("GroupCodeChars", MdValueRules.GroupCode("BAD-CODE"));
        Assert.Equal("GroupCodeChars", MdValueRules.GroupCode("한글"));
        Assert.Equal("GroupCodeChars", MdValueRules.GroupCode("A B"));
    }

    // 새 사용처 검사 SQL 이 실제 컬럼과 맞는지 — 각 마스터의 기존 행 하나로 사용처만 읽는다(쓰기 없음, 롤백)
    [SkippableFact]
    public void New_usage_queries_run_against_existing_rows()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var md = new MasterDataRepository(f);
        string? First(string table, string col) => Scalar(f, $"SELECT TOP 1 {col} FROM dbo.{table} ORDER BY {col}") as string;
        foreach (var (table, col, check) in new (string, string, Func<string, List<MasterDataRepository.UsageRef>>)[]
        {
            ("MD_DefectCode",    "DefectCode",      md.DefectCodeUsages),
            ("MD_DefectCause",   "CauseCode",       md.DefectCauseUsages),
            ("MD_Oven",          "OvenID",          md.OvenUsages),
            ("MD_Jig",           "JigID",           md.JigUsages),
            ("MD_Uom",           "UOMCode",         md.UomUsages),
            ("MD_RfidTag",       "TagID",           md.RfidTagUsages),
            ("MD_RalColor",      "RALCode",         md.RalColorUsages),
            ("MD_RfidReader",    "ReaderID",        md.RfidReaderUsages),
            ("MD_LabelTemplate", "LabelTemplateID", md.LabelTemplateUsages),
            ("MD_ReasonCode",    "ReasonCode",      md.ReasonCodeUsages),
            ("MD_PmTemplate",    "PMTemplateID",    md.PmTemplateUsages),
            ("MD_Recipe",        "RecipeID",        md.RecipeUsages),
        })
        {
            Assert.Empty(check("ITEST-NONE"));
            if (First(table, col) is { } key)
                Assert.All(check(key), u => Assert.True(u.Count > 0, $"{table} {key} {u.Kind}"));
        }
        Assert.NotEmpty(md.UomUsages("EA"));   // 품목 기본 단위 — 지울 수 없어야 한다
    }

    const string Lt = "ITEST-LT-1028", Ps = "ITEST-PS-1028", Uom = "ITQ1028", UomChild = "ITQ1028C", Cause = "ITEST-CC-1028",
                 Defect = "ITEST-DC-1028", Jig = "ITEST-JG-1028", Tag = "ITEST-TG-1028", Reason = "ITEST-RC-1028",
                 Pattern = "ITEST-PT-1028", Seg = "ITEST-SG-1028", Tpl = "ITEST-PMT-1028", Grp = "ITQGRP1028";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_PackagingSpec   WHERE PackSpecID = @Ps;
        DELETE dbo.MD_LabelTemplate   WHERE LabelTemplateID = @Lt;
        DELETE dbo.MD_Uom             WHERE UOMCode IN (@U, @Uc);
        DELETE dbo.MD_DefectCode      WHERE DefectCode = @D;
        DELETE dbo.MD_DefectCause     WHERE CauseCode = @C;
        DELETE dbo.MD_RfidTag         WHERE TagID = @Tg;
        DELETE dbo.MD_Jig             WHERE JigID = @J;
        DELETE dbo.MD_LineTimeSegment WHERE SegmentID = @Sg;
        DELETE dbo.MD_LineTimePattern WHERE PatternID = @Pt;
        DELETE dbo.MD_ReasonCode      WHERE ReasonCode = @R;
        DELETE dbo.MNT_PMSchedule     WHERE ChecklistID = @T AND CreatedBy = 'itest';
        DELETE dbo.MD_PmTemplate      WHERE PMTemplateID = @T;
        DELETE dbo.MD_CodeItem        WHERE GroupCode = @G;
        DELETE dbo.MD_CodeGroup       WHERE GroupCode = @G;
        """, ("@Ps", Ps), ("@Lt", Lt), ("@U", Uom), ("@Uc", UomChild), ("@D", Defect), ("@C", Cause), ("@Tg", Tag), ("@J", Jig),
        ("@Sg", Seg), ("@Pt", Pattern), ("@R", Reason), ("@T", Tpl), ("@G", Grp));

    static int Count(AmesConnectionFactory f, string table, string col, string key)
        => (int)Scalar(f, $"SELECT COUNT(*) FROM dbo.{table} WHERE {col} = @K", ("@K", key))!;

    [SkippableFact]
    public void In_use_masters_are_kept_until_their_user_is_gone()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            Exec(f, """
                INSERT dbo.MD_LabelTemplate   (LabelTemplateID, CreatedBy) VALUES (@Lt, 'itest');
                INSERT dbo.MD_PackagingSpec   (PackSpecID, LabelTemplateID, CreatedBy) VALUES (@Ps, @Lt, 'itest');
                INSERT dbo.MD_Uom             (UOMCode, CreatedBy) VALUES (@U, 'itest');
                INSERT dbo.MD_Uom             (UOMCode, BaseUOM, CreatedBy) VALUES (@Uc, @U, 'itest');
                INSERT dbo.MD_DefectCause     (CauseCode, CreatedBy) VALUES (@C, 'itest');
                INSERT dbo.MD_DefectCode      (DefectCode, DefaultCauseCode, CreatedBy) VALUES (@D, @C, 'itest');
                INSERT dbo.MD_Jig             (JigID, CreatedBy) VALUES (@J, 'itest');
                INSERT dbo.MD_RfidTag         (TagID, JigID, CreatedBy) VALUES (@Tg, @J, 'itest');
                INSERT dbo.MD_ReasonCode      (ReasonCode, CreatedBy) VALUES (@R, 'itest');
                INSERT dbo.MD_LineTimePattern (PatternID, CreatedBy) VALUES (@Pt, 'itest');
                INSERT dbo.MD_LineTimeSegment (SegmentID, PatternID, StartMin, EndMin, SegmentState, ReasonCode, CreatedBy)
                                       VALUES (@Sg, @Pt, 0, 60, 'PLANNED_DOWN', @R, 'itest');
                INSERT dbo.MD_PmTemplate      (PMTemplateID, CreatedBy) VALUES (@T, 'itest');
                INSERT dbo.MNT_PMSchedule     (ChecklistID, Status, CreatedBy) VALUES (@T, 'OK', 'itest');
                """, ("@Lt", Lt), ("@Ps", Ps), ("@U", Uom), ("@Uc", UomChild), ("@C", Cause), ("@D", Defect), ("@J", Jig),
                ("@Tg", Tag), ("@R", Reason), ("@Pt", Pattern), ("@Sg", Seg), ("@T", Tpl));
            var md = new MasterDataRepository(f);

            Assert.Equal("PACKSPEC",   Assert.Single(md.DeleteLabelTemplate(Lt)).Kind);
            Assert.Equal("UOM",        Assert.Single(md.DeleteUom(Uom)).Kind);
            Assert.Equal("DEFECTCODE", Assert.Single(md.DeleteDefectCause(Cause)).Kind);
            Assert.Equal("RFIDTAG",    Assert.Single(md.DeleteJig(Jig)).Kind);
            Assert.Equal("PATTERN",    Assert.Single(md.DeleteReasonCode(Reason)).Kind);
            Assert.Equal("PM",         Assert.Single(md.DeletePmTemplate(Tpl)).Kind);
            Assert.Equal(1, Count(f, "MD_LabelTemplate", "LabelTemplateID", Lt));
            Assert.Equal(1, Count(f, "MD_Uom", "UOMCode", Uom));
            Assert.Equal(1, Count(f, "MD_DefectCause", "CauseCode", Cause));
            Assert.Equal(1, Count(f, "MD_Jig", "JigID", Jig));
            Assert.Equal(1, Count(f, "MD_ReasonCode", "ReasonCode", Reason));
            Assert.Equal(1, Count(f, "MD_PmTemplate", "PMTemplateID", Tpl));

            // 쓰는 쪽을 없애면 지워진다(취소된 PM 일정은 사용처가 아니다)
            Exec(f, """
                DELETE dbo.MD_PackagingSpec WHERE PackSpecID = @Ps;
                DELETE dbo.MD_Uom WHERE UOMCode = @Uc;
                DELETE dbo.MD_DefectCode WHERE DefectCode = @D;
                DELETE dbo.MD_RfidTag WHERE TagID = @Tg;
                DELETE dbo.MD_LineTimeSegment WHERE SegmentID = @Sg;
                UPDATE dbo.MNT_PMSchedule SET Status = 'CANCELLED' WHERE ChecklistID = @T AND CreatedBy = 'itest';
                """, ("@Ps", Ps), ("@Uc", UomChild), ("@D", Defect), ("@Tg", Tag), ("@Sg", Seg), ("@T", Tpl));
            Assert.Empty(md.DeleteLabelTemplate(Lt));
            Assert.Empty(md.DeleteUom(Uom));
            Assert.Empty(md.DeleteDefectCause(Cause));
            Assert.Empty(md.DeleteJig(Jig));
            Assert.Empty(md.DeleteReasonCode(Reason));
            Assert.Empty(md.DeletePmTemplate(Tpl));
            Assert.Equal(0, Count(f, "MD_LabelTemplate", "LabelTemplateID", Lt));
            Assert.Equal(0, Count(f, "MD_Uom", "UOMCode", Uom));
            Assert.Equal(0, Count(f, "MD_DefectCause", "CauseCode", Cause));
            Assert.Equal(0, Count(f, "MD_Jig", "JigID", Jig));
            Assert.Equal(0, Count(f, "MD_ReasonCode", "ReasonCode", Reason));
            Assert.Equal(0, Count(f, "MD_PmTemplate", "PMTemplateID", Tpl));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Too_long_names_and_bad_values_are_rejected_instead_of_truncated()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var md = new MasterDataRepository(f);
            Assert.Throws<DbValueException>(() => md.InsertDefectCode(Defect, new string('가', 61), null, null, null, null, null, null, false, true, "itest"));
            Assert.Equal(0, Count(f, "MD_DefectCode", "DefectCode", Defect));
            md.InsertDefectCode(Defect, new string('가', 60), null, null, null, null, null, null, false, true, "itest");
            Assert.Throws<DbValueException>(() => md.UpdateDefectCode(Defect, new string('가', 61), null, null, null, null, null, null, false, true, "itest"));
            Assert.Equal(new string('가', 60), Scalar(f, "SELECT DefectName FROM dbo.MD_DefectCode WHERE DefectCode = @K", ("@K", Defect)));

            Assert.Throws<DbValueException>(() => md.InsertDefectCause(Cause, new string('A', 61), null, null, null, null, false, null, null, null, true, "itest"));
            Assert.Throws<MdValueRuleException>(() => md.InsertInspectionStandard("ITEST-IS-1028", null, null, null, "X", 5m, 10m, 1m,
                null, null, null, false, null, true, "itest"));
            Assert.Equal(0, Count(f, "MD_InspectionStandard", "InspStdID", "ITEST-IS-1028"));

            Assert.Throws<MdValueRuleException>(() => md.InsertCodeGroup("ITQ-BAD", "x", null, null, true, "itest"));
        }
        finally { Cleanup(f); Exec(f, "DELETE dbo.MD_InspectionStandard WHERE InspStdID = 'ITEST-IS-1028'"); }
    }

    [SkippableFact]
    public void Items_of_groups_the_program_reads_are_not_deleted_from_md030()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        var md = new MasterDataRepository(f);
        var codeId = Scalar(f, "SELECT TOP 1 CodeID FROM dbo.MD_CodeItem WHERE GroupCode = 'WORK_SHIFT'") as string;
        Skip.If(codeId is null, "WORK_SHIFT 항목 없음");
        Assert.Throws<InvalidOperationException>(() => md.DeleteCodeItem(codeId));
        Assert.Equal(1, Count(f, "MD_CodeItem", "CodeID", codeId));

        Cleanup(f);
        try
        {
            md.InsertCodeGroup(Grp, "itest", null, null, true, "itest");
            Exec(f, "INSERT dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CreatedBy) VALUES (@I, @G, 'A', 'itest')", ("@I", Grp + "_A"), ("@G", Grp));
            md.DeleteCodeItem(Grp + "_A");
            Assert.Equal(0, Count(f, "MD_CodeItem", "CodeID", Grp + "_A"));
        }
        finally { Cleanup(f); }
    }
}
