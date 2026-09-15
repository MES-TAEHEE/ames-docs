using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.InjAgent.Tests.AmesDevDb;

namespace AMES.InjAgent.Tests;

/// <summary>
/// PP-006 구매요청 상태 전이(전송·실패·PO 승인·거래처 지정)와 PP_PRSendLog 기록. AMES_DEV 통합 테스트, DB 미기동 시 skip.
/// </summary>
public class PrRepositoryTests
{
    const string Item = "ITEST-PR-ITEM";
    const string Actor = "ITEST";

    static void Seed(AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT INTO dbo.MD_Item (ItemNo, ItemName, ItemType, DefaultUOM, ActiveFlag, CreatedBy)
            VALUES (@I, N'ITEST PR item', 'MATERIAL', 'KG', 1, @By);
            INSERT INTO dbo.PP_PurchaseRequest (PrNumber, ItemNo, RequiredQty, RequiredDate, Status, CreatedBy)
            VALUES ('ITEST-PR-D1', @I, 10, '2026-10-01', 'Draft', @By),
                   ('ITEST-PR-D2', @I, 20, '2026-10-02', 'Draft', @By),
                   ('ITEST-PR-S1', @I, 30, '2026-10-03', 'Sent', @By),
                   ('ITEST-PR-A1', @I, 40, '2026-10-04', 'Approved', @By);
            UPDATE dbo.PP_PurchaseRequest SET SapPoNumber = 'PO-ITEST' WHERE PrNumber = 'ITEST-PR-A1';
            """, ("@I", Item), ("@By", Actor));
    }

    static void Cleanup(AmesConnectionFactory f)
    {
        Exec(f, """
            DELETE l FROM dbo.PP_PRSendLog l JOIN dbo.PP_PurchaseRequest p ON p.PrID = l.PrID WHERE p.ItemNo = @I;
            DELETE FROM dbo.PP_PurchaseRequest WHERE ItemNo = @I;
            DELETE FROM dbo.MD_Item WHERE ItemNo = @I;
            """, ("@I", Item));
    }

    static int Id(AmesConnectionFactory f, string prNumber)
        => (int)Scalar(f, "SELECT PrID FROM dbo.PP_PurchaseRequest WHERE PrNumber = @P;", ("@P", prNumber))!;

    static PpRepository.PrRow Row(PpRepository repo, string prNumber)
        => repo.ListPurchaseRequests().Single(r => r.PrNumber == prNumber);

    [SkippableFact]
    public void SendPrs_marks_selected_sendable_rows_sent_and_logs_attempt()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var ids = new[] { Id(f!, "ITEST-PR-D1"), Id(f!, "ITEST-PR-D2"), Id(f!, "ITEST-PR-S1"), Id(f!, "ITEST-PR-A1") };

            var sent = repo.SendPrs(ids, null, Actor);
            Assert.Equal(2, sent.Count);                       // Sent·Approved 행은 건너뛴다

            var d1 = Row(repo, "ITEST-PR-D1");
            Assert.Equal(PrStatusRules.Sent, d1.Status);
            Assert.NotNull(d1.SentAt);
            Assert.Null(d1.SapDocNum);
            Assert.Equal("KG", d1.Uom);

            var log = Assert.Single(repo.ListPrSendLog(d1.PrId));
            Assert.Equal(1, log.AttemptNo);
            Assert.Equal("Sent", log.Result);
            Assert.Equal(Actor, log.By);

            Assert.Empty(repo.SendPrs(new[] { d1.PrId }, null, Actor));   // 이미 Sent → 재전송 없음
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void FailPr_then_resend_keeps_retry_count_and_doc_num()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var id = Id(f!, "ITEST-PR-D1");

            repo.FailPr(id, "SAP B1 no response", Actor);
            var failed = Row(repo, "ITEST-PR-D1");
            Assert.Equal(PrStatusRules.Failed, failed.Status);
            Assert.Equal(1, failed.RetryCount);
            Assert.Equal("SAP B1 no response", failed.LastError);

            repo.FailPr(id, "timeout", Actor);
            Assert.Equal(2, Row(repo, "ITEST-PR-D1").RetryCount);

            Assert.Single(repo.SendPrs(new[] { id }, "PR-B1-5543", Actor));
            var resent = Row(repo, "ITEST-PR-D1");
            Assert.Equal(PrStatusRules.Sent, resent.Status);
            Assert.Equal("PR-B1-5543", resent.SapDocNum);
            Assert.Equal(2, resent.RetryCount);               // 재시도 횟수는 이력으로 남긴다
            Assert.Null(resent.LastError);

            var log = repo.ListPrSendLog(id);
            Assert.Equal(new[] { "Failed", "Failed", "Sent" }, log.Select(l => l.Result));
            Assert.Equal(new[] { 1, 2, 3 }, log.Select(l => l.AttemptNo));
            Assert.Equal("timeout", log[1].Message);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void ApprovePr_requires_sent_and_po_number()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var sentId = Id(f!, "ITEST-PR-S1");
            var draftId = Id(f!, "ITEST-PR-D1");

            Assert.Throws<InvalidOperationException>(() => repo.ApprovePr(draftId, "PO-1", Actor));
            Assert.Throws<ArgumentException>(() => repo.ApprovePr(sentId, " ", Actor));

            repo.ApprovePr(sentId, "PO-B1-8831", Actor);
            var a = Row(repo, "ITEST-PR-S1");
            Assert.Equal(PrStatusRules.Approved, a.Status);
            Assert.Equal("PO-B1-8831", a.SapPoNumber);
            Assert.Equal(Actor, a.ApprovedBy);
            Assert.NotNull(a.ApprovedAt);
            Assert.Equal("Approved", Assert.Single(repo.ListPrSendLog(sentId)).Result);

            Assert.Throws<InvalidOperationException>(() => repo.FailPr(sentId, "late", Actor));   // Approved 는 종결
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void UpdatePrVendor_only_before_send()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new PpRepository(f!);
            var draftId = Id(f!, "ITEST-PR-D1");
            var sentId = Id(f!, "ITEST-PR-S1");

            repo.UpdatePrVendor(draftId, "V-ITEST", Actor);
            Assert.Equal("V-ITEST", Row(repo, "ITEST-PR-D1").VendorId);
            repo.UpdatePrVendor(draftId, null, Actor);
            Assert.Null(Row(repo, "ITEST-PR-D1").VendorId);

            Assert.Throws<InvalidOperationException>(() => repo.UpdatePrVendor(sentId, "V-ITEST", Actor));
        }
        finally { Cleanup(f!); }
    }
}
