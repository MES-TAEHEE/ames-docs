-- ════════════════════════════════════════════════════════════════════════
--  migrate_md_worker.sql
--  MD_Worker — POP 전용 현장 작업자 마스터
--
--  지금까지 POP 로그인은 SYS_UserProfile JOIN AspNetUsers 였다. 즉 라인에
--  서는 작업자도 웹 포탈 계정(AspNetUsers)이 있어야만 터미널에 들어올 수
--  있었다. MD_Worker 는 웹 계정 없이 사번+PIN 만으로 POP 에 로그인하는
--  작업자를 담는다.
--
--  최소 구성인 이유: POP 로그인에 실제로 필요한 것은 사번·이름·PIN·사용여부
--  뿐이다. 라인 배정(AssignedLines)과 PIN 5회 잠금(FailedLoginCount)은 두지
--  않는다 — 워커는 전 라인 허용이고 잠기지 않는다. 웹 계정 작업자와 동작이
--  다르다는 뜻이므로 운영에서 헷갈리지 않게 할 것.
--
--  세션 기록: 워커로 로그인하면 PR_PopSession.OperatorID 에 WorkerNo 가
--  그대로 들어간다(웹 계정은 AspNetUsers.Id GUID). 사번이 양쪽에 겹치면
--  과거 실적의 작성자 구분이 불가능해지므로 사번은 전사 유일하게 유지할 것.
--
--  등록 화면: Web MD-032 (md/fd/workers) — 메뉴·권한은 migrate_md_worker_screen.sql. 이 스크립트는 시드를 넣지 않는다.
--
--  스키마 변경: 테이블 추가만. 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_md_worker.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID('dbo.MD_Worker', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MD_Worker (
      [WorkerID]                  INT IDENTITY         NOT NULL,
      [WorkerNo]                  VARCHAR(20)          NOT NULL,  -- POP 로그인 ID (사번 / 배지 번호)
      [WorkerName]                NVARCHAR(50)         NOT NULL,
      [PinHash]                   NVARCHAR(200)            NULL,  -- POP 4자리 PIN (PBKDF2) — SYS_UserProfile.PinHash 와 동일 포맷
      [ActiveFlag]                BIT                  NOT NULL DEFAULT 1,
      [CreatedBy]                 VARCHAR(50)          NOT NULL,
      [CreatedTS]                 DATETIME2                NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]                NVARCHAR(450)            NULL,
      [ModifiedTS]                DATETIME2                NULL,
      CONSTRAINT PK_MD_Worker PRIMARY KEY CLUSTERED ([WorkerID])
    );
    PRINT 'MD_Worker created';
END
ELSE
    PRINT 'MD_Worker already exists';
GO

-- 사번은 로그인 키다. 중복되면 어느 작업자가 들어왔는지 판별할 수 없다.
IF OBJECT_ID('dbo.MD_Worker', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes
                   WHERE name = 'UQ_MD_Worker_WorkerNo'
                     AND object_id = OBJECT_ID('dbo.MD_Worker'))
BEGIN
    CREATE UNIQUE INDEX UQ_MD_Worker_WorkerNo ON dbo.MD_Worker ([WorkerNo]);
    PRINT 'UQ_MD_Worker_WorkerNo created';
END
ELSE
    PRINT 'UQ_MD_Worker_WorkerNo already exists';
GO

SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
FROM   sys.columns c
JOIN   sys.types   t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MD_Worker')
ORDER  BY c.column_id;
GO
