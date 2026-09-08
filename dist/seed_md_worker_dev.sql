-- ════════════════════════════════════════════════════════════════════════
--  seed_md_worker_dev.sql
--  MD_Worker 개발 시드 — POP 전용 현장 작업자 5명
--
--  WorkerNo  Name            PIN     ActiveFlag
--  --------  --------------  ------  ----------
--  W001      Jang Dae-ho     1234    1
--  W002      Oh Se-rin       1234    1
--  W003      Seo Kang-min    1234    1
--  W004      Bae Yu-na       1234    1
--  W005      Noh Tae-il      1234    0   ← 비활성 계정 게이트 테스트용
--
--  PinHash 는 PBKDF2-HMACSHA256 10,000회(AMES.Data.Security.PinHasher 포맷)를
--  미리 계산해 리터럴로 박아 뒀다. 솔트가 고정이라 운영에는 절대 쓰지 말 것 —
--  운영 계정은 등록 화면이 PinHasher.Hash() 로 매번 새 솔트를 만든다.
--
--  사번은 W 로 시작해 seed_pop_users 의 웹 계정(E/I/P/Q/S)과 겹치지 않는다.
--  겹치면 웹 계정이 이겨 워커가 로그인되지 않는다(PopAuthService 해석 순서).
--
--  전제: migrate_md_worker.sql 적용 후. 재실행 안전(사번 기준 UPSERT).
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/seed_md_worker_dev.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.MD_Worker', 'U') IS NULL
BEGIN
    RAISERROR('MD_Worker 가 없습니다. dist/migrate_md_worker.sql 을 먼저 적용하세요.', 16, 1);
    RETURN;
END
GO

DECLARE @seed TABLE (
    WorkerNo   VARCHAR(20),
    WorkerName NVARCHAR(50),
    PinHash    NVARCHAR(200),
    ActiveFlag BIT
);

INSERT INTO @seed (WorkerNo, WorkerName, PinHash, ActiveFlag) VALUES
 ('W001', N'Jang Dae-ho',  N'AQAAAAEAACcQAAAAENOhgxypR/CyomaSpVDlizwXrg4pjAUc6QcY2pJdN7zlT8PJVny1F75ncH3HHF/dIA==', 1),
 ('W002', N'Oh Se-rin',    N'AQAAAAEAACcQAAAAEE3xHC11cHQsuQeT+MsdIKPO/SiTuDvbWs+oEjQUotdJtuEIoPX+fjEqVjaLjo5TFg==', 1),
 ('W003', N'Seo Kang-min', N'AQAAAAEAACcQAAAAENvSRnSwa6Bc1VTWm5KbotdTFwCTu7keGkovuhnG3OzDnqaRcZL0IZUGI5UHrsDIyQ==', 1),
 ('W004', N'Bae Yu-na',    N'AQAAAAEAACcQAAAAEFE5Kd2G/ixNFeL56Fs8ns6VRnpqRsJus0r4roPGOmN2T9As/RsK9FBRun1F9aqZGw==', 1),
 ('W005', N'Noh Tae-il',   N'AQAAAAEAACcQAAAAENNxtW/yUocWAMVhU3Cx+2pXC1+Wyi8HY4h/28kWRiN1xEm20TLOQvxn5QeyoBn3xg==', 0);

UPDATE w
SET    w.WorkerName = s.WorkerName,
       w.PinHash    = s.PinHash,
       w.ActiveFlag = s.ActiveFlag,
       w.ModifiedBy = 'seed_dev',
       w.ModifiedTS = SYSDATETIME()
FROM   dbo.MD_Worker w
JOIN   @seed         s ON s.WorkerNo = w.WorkerNo;

INSERT INTO dbo.MD_Worker (WorkerNo, WorkerName, PinHash, ActiveFlag, CreatedBy)
SELECT s.WorkerNo, s.WorkerName, s.PinHash, s.ActiveFlag, 'seed_dev'
FROM   @seed s
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_Worker w WHERE w.WorkerNo = s.WorkerNo);
GO

SELECT WorkerID, WorkerNo, WorkerName, ActiveFlag,
       CASE WHEN PinHash IS NULL THEN 'NO PIN' ELSE 'set' END AS Pin
FROM   dbo.MD_Worker
ORDER  BY WorkerNo;
GO
