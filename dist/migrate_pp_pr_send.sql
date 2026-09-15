-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_pr_send.sql
--  PP-006 구매요청 SAP 전송 상태 — 화면 주도 상태 전이(Draft/Sent/Approved/Failed)
--
--  SAP B1 Service Layer 연동 전까지 전송·승인·실패를 화면에서 확정하고 PP_PRSendLog 에 남긴다.
--  DocNum(전송 응답)·전송 시각·재시도 횟수·마지막 실패 사유를 PP_PurchaseRequest 에 둔다.
--  상태 어휘를 Draft/Sent/Approved/Failed 로 통일한다: 구 Pending → Draft, 구 Rejected → Failed(사유 'Legacy: Rejected').
--
--  컬럼 추가 + 상태 이관뿐 — 적용 순서 무관, 재실행 안전(멱등).
--  적용:  sqlcmd -S 192.168.1.100 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -b -i dist/migrate_pp_pr_send.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.PP_PurchaseRequest', 'SapDocNum') IS NULL
    ALTER TABLE dbo.PP_PurchaseRequest ADD SapDocNum VARCHAR(20) NULL;      -- SAP B1 PR DocNum (전송 응답)
GO
IF COL_LENGTH('dbo.PP_PurchaseRequest', 'SentAt') IS NULL
    ALTER TABLE dbo.PP_PurchaseRequest ADD SentAt DATETIME2 NULL;           -- 마지막 전송(Sent) 시각
GO
IF COL_LENGTH('dbo.PP_PurchaseRequest', 'RetryCount') IS NULL
    ALTER TABLE dbo.PP_PurchaseRequest ADD RetryCount TINYINT NOT NULL CONSTRAINT DF_PP_PurchaseRequest_RetryCount DEFAULT 0;  -- 실패 기록 횟수
GO
IF COL_LENGTH('dbo.PP_PurchaseRequest', 'LastError') IS NULL
    ALTER TABLE dbo.PP_PurchaseRequest ADD LastError NVARCHAR(200) NULL;    -- 마지막 실패 사유
GO

UPDATE dbo.PP_PurchaseRequest SET Status = 'Draft'
WHERE  Status = 'Pending';
PRINT CONCAT(N'Pending → Draft: ', @@ROWCOUNT);

UPDATE dbo.PP_PurchaseRequest SET Status = 'Failed', LastError = COALESCE(LastError, N'Legacy: Rejected')
WHERE  Status = 'Rejected';
PRINT CONCAT(N'Rejected → Failed: ', @@ROWCOUNT);

-- PO 번호가 이미 있는 행은 Approved 여야 한다 (구 화면은 SapPoNumber 유무로 "전송" 을 판단했다)
UPDATE dbo.PP_PurchaseRequest SET Status = 'Approved'
WHERE  SapPoNumber IS NOT NULL AND Status <> 'Approved';
PRINT CONCAT(N'PO 보유 행 → Approved: ', @@ROWCOUNT);
GO

PRINT 'migrate_pp_pr_send: done';
GO
