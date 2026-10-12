using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD-026 예비품 저장 — 등록은 채번·INSERT·초기 재고 입고(IN 이력)를 한 트랜잭션으로, 수정은 이미지까지 같은 UPDATE 로(10-12).
// 부품번호 ITEST-SP-1012* 만 쓰고 끝에 이력째 지운다(예비품번호는 테스트 분류 K9 에서 다음 번호를 받는다).
public class SparePartSaveTests
{
    const string Pfx = "ITEST-SP-1012";

    static void Cleanup(AmesConnectionFactory f) => Exec(f, """
        DELETE t FROM dbo.MNT_SparePartsTxn t JOIN dbo.MD_SparePart p ON p.SparePartNo = t.SparePartNo WHERE p.PartNo LIKE @P + '%';
        DELETE dbo.MD_SparePart WHERE PartNo LIKE @P + '%';
        """, ("@P", Pfx));

    static string Row(AmesConnectionFactory f, string spNo) => (string)Scalar(f, """
        SELECT CONCAT(p.OnHandQty, '|', ISNULL(DATALENGTH(p.SparePartImage), -1), '|',
               (SELECT COUNT(*) FROM dbo.MNT_SparePartsTxn t WHERE t.SparePartNo = p.SparePartNo), '|',
               (SELECT TOP 1 CONCAT(t.MoveType, ' ', t.Qty, ' ', t.BalanceBefore, '>', t.BalanceAfter, ' ', t.RefType, ' ', t.Note)
                  FROM dbo.MNT_SparePartsTxn t WHERE t.SparePartNo = p.SparePartNo ORDER BY t.SparePartsTxnID))
        FROM dbo.MD_SparePart p WHERE p.SparePartNo = @S
        """, ("@S", spNo))!;

    [SkippableFact]
    public void Insert_writes_initial_stock_with_its_receipt_in_one_save()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var md = new MasterDataRepository(f);
            var withStock = md.InsertSparePart(Pfx + "-A", "itest", "K", "9", null, "EA", null, null, null, null, null, true, "itest",
                image: [1, 2, 3], initialQty: 7, initialNote: "init");
            Assert.Equal("7|3|1|IN 7 0>7 ADJ init", Row(f, withStock));

            var noStock = md.InsertSparePart(Pfx + "-B", "itest", "K", "9", null, "EA", null, null, null, null, null, true, "itest");
            Assert.Equal("0|-1|0|", Row(f, noStock));

            Assert.Throws<ArgumentException>(() => md.InsertSparePart(Pfx + "-C", "itest", "K", "9", null, "EA", null, null, null, null, null, true, "itest", initialQty: -1));
            Assert.Equal(0, (int)Scalar(f, "SELECT COUNT(*) FROM dbo.MD_SparePart WHERE PartNo = @P", ("@P", Pfx + "-C"))!);
        }
        finally { Cleanup(f); }
    }

    [SkippableFact]
    public void Update_changes_the_image_only_when_asked()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Cleanup(f);
        try
        {
            var md = new MasterDataRepository(f);
            var sp = md.InsertSparePart(Pfx + "-A", "itest", "K", "9", null, "EA", null, null, null, null, null, true, "itest", image: [1, 2, 3]);
            void Update(bool changed, byte[]? image) => md.UpdateSparePart(sp, "itest2", null, "EA", null, null, null, null, null, true, "itest",
                imageChanged: changed, image: image);

            Update(false, null);
            Assert.Equal(3, md.GetSparePartImage(sp)!.Length);   // 이미지를 안 바꾸면 그대로
            Update(true, [9, 9, 9, 9, 9]);
            Assert.Equal(5, md.GetSparePartImage(sp)!.Length);
            Update(true, null);
            Assert.Null(md.GetSparePartImage(sp));                // 제거
            Assert.Equal("itest2", Scalar(f, "SELECT PartName FROM dbo.MD_SparePart WHERE SparePartNo = @S", ("@S", sp)));
        }
        finally { Cleanup(f); }
    }
}
