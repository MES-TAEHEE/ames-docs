-- ════════════════════════════════════════════════════════════════════════
--  migrate_andon_workflow.sql
--  POP 안돈 대응 워크플로 — 슈퍼바이저 QR 응답 → 원인·부서 호출 → 담당자 도착 → ACK
--
--  1) 공통코드 ANDON_DEPT(호출 부서) · ANDON_CAUSE(문제 유형, Attribute1 = 기본 부서)
--  2) MD_LineSupervisor — 라인별 슈퍼바이저 사번. 등록된 사번만 안돈에 응답할 수 있다.
--     등록 화면은 Web 에 별도 개발 예정. 이 스크립트는 시드를 넣지 않는다(dev: seed_andon_dev.sql).
--  3) PR_AndonCall.SupervisorName + IX_PR_AndonCall_Line_Status
--     AckedBy/AckedAt 는 이제 슈퍼바이저 사번/응답 시각, ReasonCode 는 ANDON_CAUSE.CodeValue,
--     Status 는 OPEN → SUP_ACKED → DEPT_CALLED → RESOLVED.
--  4) PR_AndonDeptCall — 부서 호출·도착·ACK 이력. (AndonID, DeptCode) 유니크.
--  5) 구 상태값 정리: RESUMED/ACKED → RESOLVED 백필, 종료시각 채움, 살아있는 구 대소문자(Open 등) → 대문자
--
--  순서 무관, 재실행 안전.
--  적용:  sqlcmd -S 192.168.1.100,1433 -U ames_app -P !Dev2026 -d AMES_DEV -f 65001 -i dist/migrate_andon_workflow.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── 1. 공통코드 ──────────────────────────────────────────────────────
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('ANDON_DEPT',  N'안돈 호출 부서', N'Andon Department', N'안돈 발생 시 호출할 부서'),
    ('ANDON_CAUSE', N'안돈 문제 유형', N'Andon Cause',      N'안돈 문제 유형. Attribute1 = 기본 호출 부서(ANDON_DEPT.CodeValue)')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'migrate', SYSDATETIME());
GO

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('ANDON_DEPT_MAINT',     'ANDON_DEPT',  'MAINT',    N'보전',     N'Maintenance',       NULL,       1),
    ('ANDON_DEPT_QC',        'ANDON_DEPT',  'QC',       N'품질',     N'Quality',           NULL,       2),
    ('ANDON_DEPT_MATERIAL',  'ANDON_DEPT',  'MATERIAL', N'자재',     N'Material',          NULL,       3),
    ('ANDON_CAUSE_EQUIP',    'ANDON_CAUSE', 'EQUIP',    N'설비고장', N'Equipment failure', N'MAINT',    1),
    ('ANDON_CAUSE_MOLD',     'ANDON_CAUSE', 'MOLD',     N'금형이상', N'Mold issue',        N'MAINT',    2),
    ('ANDON_CAUSE_QUALITY',  'ANDON_CAUSE', 'QUALITY',  N'품질불량', N'Quality defect',    N'QC',       3),
    ('ANDON_CAUSE_MATERIAL', 'ANDON_CAUSE', 'MATERIAL', N'자재부족', N'Material shortage', N'MATERIAL', 4),
    ('ANDON_CAUSE_OTHER',    'ANDON_CAUSE', 'OTHER',    N'기타',     N'Other',             NULL,       9)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'ANDON_DEPT / ANDON_CAUSE code items merged';
GO

-- ── 2. MD_LineSupervisor ────────────────────────────────────────────
IF OBJECT_ID('dbo.MD_LineSupervisor', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MD_LineSupervisor (
      [LineID]      VARCHAR(20)    NOT NULL,  -- FK -> MD_Line.LineID
      [WorkerNo]    VARCHAR(20)    NOT NULL,  -- 사번. SYS_UserProfile.EmployeeNo / MD_Worker.WorkerNo 공용
      [ActiveFlag]  BIT            NOT NULL DEFAULT 1,
      [CreatedBy]   VARCHAR(50)    NOT NULL,
      [CreatedTS]   DATETIME2          NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]  NVARCHAR(450)      NULL,
      [ModifiedTS]  DATETIME2          NULL,
      CONSTRAINT PK_MD_LineSupervisor PRIMARY KEY CLUSTERED ([LineID], [WorkerNo])
    );
    PRINT 'MD_LineSupervisor created';
END
ELSE
    PRINT 'MD_LineSupervisor already exists';
GO

-- ── 3. PR_AndonCall ─────────────────────────────────────────────────
IF COL_LENGTH('dbo.PR_AndonCall', 'SupervisorName') IS NULL
BEGIN
    ALTER TABLE dbo.PR_AndonCall ADD [SupervisorName] NVARCHAR(50) NULL;
    PRINT 'PR_AndonCall.SupervisorName added';
END
ELSE
    PRINT 'PR_AndonCall.SupervisorName already exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_PR_AndonCall_Line_Status'
                 AND object_id = OBJECT_ID('dbo.PR_AndonCall'))
BEGIN
    CREATE INDEX IX_PR_AndonCall_Line_Status ON dbo.PR_AndonCall ([LineID], [Status]) INCLUDE ([TriggeredAt]);
    PRINT 'IX_PR_AndonCall_Line_Status created';
END
ELSE
    PRINT 'IX_PR_AndonCall_Line_Status already exists';
GO

-- ── 4. PR_AndonDeptCall ─────────────────────────────────────────────
IF OBJECT_ID('dbo.PR_AndonDeptCall', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PR_AndonDeptCall (
      [DeptCallID]   INT IDENTITY   NOT NULL,
      [AndonID]      INT            NOT NULL,  -- FK -> PR_AndonCall.AndonID
      [DeptCode]     VARCHAR(20)    NOT NULL,  -- ANDON_DEPT.CodeValue
      [CalledAt]     DATETIME2      NOT NULL DEFAULT SYSDATETIME(),
      [CalledBy]     VARCHAR(20)    NOT NULL,  -- 슈퍼바이저 사번
      [ArrivedAt]    DATETIME2          NULL,
      [ArrivedNo]    VARCHAR(20)        NULL,
      [ArrivedName]  NVARCHAR(50)       NULL,
      [AckedAt]      DATETIME2          NULL,
      [CreatedBy]    VARCHAR(50)    NOT NULL,
      [CreatedTS]    DATETIME2          NULL DEFAULT SYSDATETIME(),
      [ModifiedBy]   NVARCHAR(450)      NULL,
      [ModifiedTS]   DATETIME2          NULL,
      CONSTRAINT PK_PR_AndonDeptCall PRIMARY KEY CLUSTERED ([DeptCallID]),
      CONSTRAINT FK_PR_AndonDeptCall_Andon FOREIGN KEY ([AndonID]) REFERENCES dbo.PR_AndonCall([AndonID]),
      CONSTRAINT UX_PR_AndonDeptCall_Dept UNIQUE ([AndonID], [DeptCode])
    );
    PRINT 'PR_AndonDeptCall created';
END
ELSE
    PRINT 'PR_AndonDeptCall already exists';
GO

-- ── 5. 구 상태값 정리 ────────────────────────────────────────────────
-- 구 팝업(OPEN → ACKED → RESUMED)의 RESUMED/ACKED 는 신 라이프사이클에 없는 값이라
-- GetOpenForLine 의 IN 필터에서 계속 제외된 채 남는다 — 이력 조회가 하나의 종결값만 보도록 백필.
UPDATE dbo.PR_AndonCall SET Status = 'RESOLVED', ModifiedBy = 'migrate', ModifiedTS = SYSDATETIME()
WHERE  Status IN ('RESUMED', 'ACKED');              -- 구 팝업 종료/확인 상태 → 종료 (CI 콜레이션이라 대소문자 무관)
PRINT CAST(@@ROWCOUNT AS VARCHAR(10)) + ' legacy RESUMED/ACKED andon rows marked RESOLVED';
GO

UPDATE dbo.PR_AndonCall SET ResumedAt = COALESCE(ResumedAt, AckedAt, TriggeredAt, SYSDATETIME())
WHERE  Status = 'RESOLVED' AND ResumedAt IS NULL;   -- 백필된 행에 종료시각이 비지 않게
PRINT CAST(@@ROWCOUNT AS VARCHAR(10)) + ' backfilled RESOLVED rows given a ResumedAt';
GO

UPDATE dbo.PR_AndonCall SET Status = UPPER(Status), ModifiedBy = 'migrate', ModifiedTS = SYSDATETIME()
WHERE  Status COLLATE Latin1_General_CS_AS IN ('Open', 'open');  -- 살아있는 구 행은 신 대문자 값으로
PRINT CAST(@@ROWCOUNT AS VARCHAR(10)) + ' legacy mixed-case Open rows upper-cased';
GO

SELECT 'MD_LineSupervisor' AS tbl, COUNT(*) AS rows_ FROM dbo.MD_LineSupervisor
UNION ALL SELECT 'PR_AndonDeptCall', COUNT(*) FROM dbo.PR_AndonDeptCall
UNION ALL SELECT 'ANDON codes', COUNT(*) FROM dbo.MD_CodeItem WHERE GroupCode IN ('ANDON_DEPT','ANDON_CAUSE');
GO
