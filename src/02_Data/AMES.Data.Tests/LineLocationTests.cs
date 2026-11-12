using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 라인 등록 시 라인 위치(MD_Location LINE)와 라인 구역 코드(MD_CodeItem WH_AREA_<접두어>)가 같이 생기고,
// 비어 있으면 삭제 때 사용처로 막지 않고 함께 지워지는지 확인한다(쓰기 — AMES_TEST_CONN 지정 시에만 실행).
public class LineLocationTests
{
    const string Line = "ITEST-LN-LOC", Prefix = "Z9", Loc = "Z901A1F1", Area = "WH_AREA_Z9";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.MD_Location WHERE LocationID = @Loc;
        DELETE dbo.MD_CodeItem WHERE CodeID = @Area;
        DELETE dbo.MD_Line     WHERE LineID = @L;
        """, ("@Loc", Loc), ("@Area", Area), ("@L", Line));

    [SkippableFact]
    public void Line_registration_creates_location_and_area_code_and_delete_removes_them()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_TEST_CONN not set");
        Cleanup(f);
        try
        {
            var md = new MasterDataRepository(f);
            md.InsertLine(Line, "Location test line", "Location test line", null, "WC-INJ",
                null, null, false, "ACTIVE", "ITEST", Prefix);

            // 위치: 기존 라인 규칙(ID = 접두어 + 01A1F1, 구역 = 접두어, LINE 유형, 라인 연결, 활성)
            Assert.Equal(1, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Location WHERE LocationID=@L AND LocationType='LINE' AND AreaCode=@P AND LineID=@ID AND ActiveFlag=1", ("@L", Loc), ("@P", Prefix), ("@ID", Line))!);
            // 구역 코드: WH_AREA_<접두어>, 상위 EOS 창고
            Assert.Equal(1, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_CodeItem WHERE CodeID=@A AND GroupCode='WH_AREA' AND CodeValue=@P AND UseFlag=1 AND ParentCodeID=(SELECT CodeID FROM dbo.MD_CodeItem WHERE GroupCode='WH_CODE' AND CodeValue='EOS')", ("@A", Area), ("@P", Prefix))!);
            // 방금 만든 빈 라인은 사용처가 없다
            Assert.Empty(md.LineUsages(Line));

            // 삭제: 빈 자동 위치·구역 코드·라인이 함께 지워지고 사용처 목록은 비어야 한다
            Assert.Empty(md.DeleteLine(Line));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Location WHERE LocationID=@L", ("@L", Loc))!);
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_CodeItem WHERE CodeID=@A", ("@A", Area))!);
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_Line WHERE LineID=@ID", ("@ID", Line))!);
        }
        finally { Cleanup(f); }
    }
}
