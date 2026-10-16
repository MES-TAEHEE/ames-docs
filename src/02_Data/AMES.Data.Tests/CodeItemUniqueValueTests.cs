using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 같은 그룹 안 코드값은 하나뿐 — 등록·수정 모두 겹치면 DuplicateCodeValueException 이고 아무것도 바뀌지 않는다(대소문자 무시).
// 전용 그룹 코드(그룹 행 없이도 항목은 저장된다 — FK 없음)만 쓰고 끝에 지운다.
public class CodeItemUniqueValueTests
{
    const string Grp = "ITEST_UQ_GRP";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, "DELETE dbo.MD_CodeItem WHERE GroupCode = @G", ("@G", Grp));

    static string Values(AmesConnectionFactory f)
    {
        using var conn = f.OpenConnection();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            "SELECT STRING_AGG(CodeID + '=' + CodeValue, ',') WITHIN GROUP (ORDER BY CodeID) FROM dbo.MD_CodeItem WHERE GroupCode = @G", conn);
        cmd.Parameters.AddWithValue("@G", Grp);
        return cmd.ExecuteScalar() as string ?? "";
    }

    static void Add(MasterDataRepository md, string id, string value) =>
        md.InsertCodeItem(id, Grp, value, value, value, 10, null, true, null, "itest");

    static void Set(MasterDataRepository md, string id, string value) =>
        md.UpdateCodeItem(id, value, value, value, 10, "a1", true, null, "itest");

    [SkippableFact]
    public void Duplicate_value_in_a_group_is_rejected_on_insert_and_update()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var md = new MasterDataRepository(f);
            Add(md, Grp + "_KEY", "KEY");
            Add(md, Grp + "_OTHER", "OTHER");

            // 다른 CodeID 로 같은 값(대소문자만 다름)을 등록
            var dup = Assert.Throws<MasterDataRepository.DuplicateCodeValueException>(() => Add(md, Grp + "_KEY2", "key"));
            Assert.Equal((Grp, "key"), (dup.GroupCode, dup.CodeValue));

            // 다른 행의 값으로 바꾸기
            Assert.Throws<MasterDataRepository.DuplicateCodeValueException>(() => Set(md, Grp + "_OTHER", "KEY"));
            Assert.Equal($"{Grp}_KEY=KEY,{Grp}_OTHER=OTHER", Values(f));

            // 자기 값 그대로 저장·겹치지 않는 값으로 변경(PP-APS 구간 하한처럼)은 된다
            Set(md, Grp + "_KEY", "KEY");
            Set(md, Grp + "_OTHER", "OTHER2");
            Assert.Equal($"{Grp}_KEY=KEY,{Grp}_OTHER=OTHER2", Values(f));
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Same_value_in_another_group_is_allowed()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        Exec(f, "DELETE dbo.MD_CodeItem WHERE GroupCode = 'ITEST_UQ_GRP2'");
        try
        {
            var md = new MasterDataRepository(f);
            Add(md, Grp + "_KEY", "KEY");
            md.InsertCodeItem("ITEST_UQ_GRP2_KEY", "ITEST_UQ_GRP2", "KEY", "KEY", "KEY", 10, null, true, null, "itest");
            Assert.Equal($"{Grp}_KEY=KEY", Values(f));
        }
        finally { Cleanup(f); Exec(f, "DELETE dbo.MD_CodeItem WHERE GroupCode = 'ITEST_UQ_GRP2'"); }
    }
}
