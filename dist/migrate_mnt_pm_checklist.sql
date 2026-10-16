/* ------------------------------------------------------------------
   migrate_mnt_pm_checklist.sql  (2026-10-15)

   PM 템플릿 점검 단계를 PM 작업지시 점검 항목으로 복사하기 위한 구조(사용자 결정).

   1) MD_PmTemplateStep — 필요 부품을 예비품 마스터로 연결
        RequiredPartNo varchar(20)  → RequiredSparePartNo varchar(16)  (MD_SparePart.SparePartNo, 같은 위치)
        RequiredQty    decimal(10,3) → int                               (MD_SparePart.OnHandQty 와 같은 정수 수량)
      기존 값은 예비품번호면 그대로, 예비품 PartNo 와 같으면 그 예비품번호로 바꾸고, 어느 쪽도 아니면 비운다.
      수량은 반올림한다.

   2) MNT_WorkOrderTask — 템플릿 단계 스냅샷 컬럼을 단계 테이블과 같은 순서로 더하고 테이블을 다시 만든다
        WorkOrderTaskID · WorkOrderID · PMStepID(원래 단계) · TaskSeq · TaskName nvarchar(120 → 200)
        · AcceptanceCriteria · RequiredSparePartNo · RequiredQty · StepDurationMin
        · TaskType · Result(공통코드 MNT_RESULT) · Note · EvidenceURL · CompletedBy · CompletedAt · 감사 컬럼
      IDENTITY·값·컬럼 설명 보존, 작업지시별 조회 인덱스 IX_MNT_WorkOrderTask_WorkOrderID.

   3) 공통코드 MNT_RESULT(점검 결과) — PASS 합격 · FAIL 불합격 · NA 해당 없음 · PENDING 미점검

   · 재실행 안전(구조가 이미 맞으면 건너뜀). 적용: sqlcmd -f 65001 -I -b
   · 신 Web 과 함께 반영한다 — 구 Web 은 RequiredPartNo 를 읽어 MD-027 점검 항목 조회가 실패하고,
     신 Web 은 새 컬럼을 읽어 마이그레이션 없이는 MD-027·MNT-007 이 실패한다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1) MD_PmTemplateStep ------------------------------------------------
-- 옛 컬럼 이름을 쓰는 문장은 동적 SQL — 이미 바뀐 DB 에서 다시 돌려도 배치가 컴파일 오류로 멈추지 않게
IF COL_LENGTH('dbo.MD_PmTemplateStep', 'RequiredPartNo') IS NOT NULL
BEGIN
    BEGIN TRAN;

    EXEC (N'
    UPDATE s
    SET    RequiredPartNo = COALESCE(sp.SparePartNo, byPart.SparePartNo),
           RequiredQty    = CASE WHEN COALESCE(sp.SparePartNo, byPart.SparePartNo) IS NULL THEN NULL ELSE ROUND(s.RequiredQty, 0) END
    FROM   dbo.MD_PmTemplateStep s
    LEFT JOIN dbo.MD_SparePart sp     ON sp.SparePartNo = s.RequiredPartNo
    LEFT JOIN dbo.MD_SparePart byPart ON byPart.PartNo  = s.RequiredPartNo AND sp.SparePartNo IS NULL
    WHERE  s.RequiredPartNo IS NOT NULL OR s.RequiredQty IS NOT NULL;
    PRINT CONCAT(''MD_PmTemplateStep 필요 부품 정리: '', @@ROWCOUNT, ''행'');');

    EXEC sp_rename N'dbo.MD_PmTemplateStep.RequiredPartNo', N'RequiredSparePartNo', N'COLUMN';
    EXEC (N'ALTER TABLE dbo.MD_PmTemplateStep ALTER COLUMN RequiredSparePartNo VARCHAR(16) NULL;
            ALTER TABLE dbo.MD_PmTemplateStep ALTER COLUMN RequiredQty INT NULL;');

    COMMIT;
    PRINT 'MD_PmTemplateStep: RequiredPartNo → RequiredSparePartNo varchar(16), RequiredQty → int';
END
ELSE
    PRINT 'MD_PmTemplateStep 필요 부품 컬럼 이미 변경됨';
GO

-- 단계 컬럼 설명(있으면 고치고 없으면 만든다)
DECLARE @d TABLE (Col sysname, Txt nvarchar(200));
INSERT @d VALUES (N'RequiredSparePartNo', N'필요 예비품 · MD_SparePart.SparePartNo · varchar(16)'),
                 (N'RequiredQty',         N'필요 수량 · int');
DECLARE @c sysname, @t nvarchar(200);
DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT Col, Txt FROM @d;
OPEN cur; FETCH NEXT FROM cur INTO @c, @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID('dbo.MD_PmTemplateStep')
               AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_PmTemplateStep'), @c, 'ColumnId') AND name = N'MS_Description')
        EXEC sp_updateextendedproperty N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'MD_PmTemplateStep', N'COLUMN', @c;
    ELSE
        EXEC sp_addextendedproperty    N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'MD_PmTemplateStep', N'COLUMN', @c;
    FETCH NEXT FROM cur INTO @c, @t;
END
CLOSE cur; DEALLOCATE cur;
GO

-- 2) MNT_WorkOrderTask ------------------------------------------------
IF COL_LENGTH('dbo.MNT_WorkOrderTask', 'PMStepID') IS NULL
   OR COLUMNPROPERTY(OBJECT_ID('dbo.MNT_WorkOrderTask'), 'PMStepID', 'ColumnId') <> 3
   OR COLUMNPROPERTY(OBJECT_ID('dbo.MNT_WorkOrderTask'), 'TaskType', 'ColumnId') <> 10
BEGIN
    BEGIN TRAN;

    -- 컬럼 설명을 보관했다가 새 테이블에 되살린다
    SELECT c.name AS Col, CAST(ep.value AS nvarchar(4000)) AS Txt
    INTO   #desc
    FROM   sys.extended_properties ep
    LEFT JOIN sys.columns c ON c.object_id = ep.major_id AND c.column_id = ep.minor_id
    WHERE  ep.class = 1 AND ep.major_id = OBJECT_ID('dbo.MNT_WorkOrderTask') AND ep.name = N'MS_Description';

    CREATE TABLE dbo.MNT_WorkOrderTask_new (
      [WorkOrderTaskID]      INT IDENTITY      NOT NULL,
      [WorkOrderID]          INT                   NULL,  -- MNT_WorkOrder.WorkOrderID
      [PMStepID]             VARCHAR(24)           NULL,  -- 복사해 온 MD_PmTemplateStep.PMStepID (스냅샷 출처)
      [TaskSeq]              INT                   NULL,
      [TaskName]             NVARCHAR(200)         NULL,  -- 점검 항목 (MD_PmTemplateStep.StepDescription)
      [AcceptanceCriteria]   NVARCHAR(200)         NULL,
      [RequiredSparePartNo]  VARCHAR(16)           NULL,  -- MD_SparePart.SparePartNo
      [RequiredQty]          INT                   NULL,
      [StepDurationMin]      INT                   NULL,
      [TaskType]             VARCHAR(20)           NULL,
      [Result]               VARCHAR(10)           NULL,  -- 공통코드 MNT_RESULT
      [Note]                 NVARCHAR(500)         NULL,
      [EvidenceURL]          VARCHAR(255)          NULL,
      [CompletedBy]          VARCHAR(20)           NULL,
      [CompletedAt]          DATETIME2             NULL,
      [CreatedBy]            VARCHAR(20)       NOT NULL,
      [CreatedTS]            DATETIME2             NULL CONSTRAINT DF_MNT_WorkOrderTask_CreatedTS_new DEFAULT SYSDATETIME(),
      [ModifiedBy]           VARCHAR(20)           NULL,
      [ModifiedTS]           DATETIME2             NULL,
      CONSTRAINT PK_MNT_WorkOrderTask_new PRIMARY KEY CLUSTERED ([WorkOrderTaskID])
    );

    DECLARE @hasSnap bit = CASE WHEN COL_LENGTH('dbo.MNT_WorkOrderTask', 'PMStepID') IS NULL THEN 0 ELSE 1 END;
    DECLARE @sql nvarchar(max) = N'
    SET IDENTITY_INSERT dbo.MNT_WorkOrderTask_new ON;
    INSERT INTO dbo.MNT_WorkOrderTask_new
        (WorkOrderTaskID, WorkOrderID, PMStepID, TaskSeq, TaskName, AcceptanceCriteria, RequiredSparePartNo, RequiredQty, StepDurationMin,
         TaskType, Result, Note, EvidenceURL, CompletedBy, CompletedAt, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
    SELECT WorkOrderTaskID, WorkOrderID, ' + CASE WHEN @hasSnap = 1
        THEN N'PMStepID, TaskSeq, TaskName, AcceptanceCriteria, RequiredSparePartNo, RequiredQty, StepDurationMin,'
        ELSE N'NULL, TaskSeq, TaskName, NULL, NULL, NULL, NULL,' END + N'
           TaskType, Result, Note, EvidenceURL, CompletedBy, CompletedAt, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
    FROM   dbo.MNT_WorkOrderTask;
    SET IDENTITY_INSERT dbo.MNT_WorkOrderTask_new OFF;';
    EXEC sp_executesql @sql;

    DROP TABLE dbo.MNT_WorkOrderTask;
    EXEC sp_rename N'dbo.MNT_WorkOrderTask_new', N'MNT_WorkOrderTask', N'OBJECT';
    EXEC sp_rename N'dbo.PK_MNT_WorkOrderTask_new', N'PK_MNT_WorkOrderTask', N'OBJECT';
    EXEC sp_rename N'dbo.DF_MNT_WorkOrderTask_CreatedTS_new', N'DF_MNT_WorkOrderTask_CreatedTS', N'OBJECT';

    -- 설명: 보관한 것 되살리기 → 새 컬럼·바뀐 형식 설명
    DECLARE @c sysname, @t nvarchar(4000);
    DECLARE cur CURSOR LOCAL FAST_FORWARD FOR SELECT Col, Txt FROM #desc;
    OPEN cur; FETCH NEXT FROM cur INTO @c, @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @c IS NULL
            EXEC sp_addextendedproperty N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'MNT_WorkOrderTask';
        ELSE IF COL_LENGTH('dbo.MNT_WorkOrderTask', @c) IS NOT NULL
            EXEC sp_addextendedproperty N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'MNT_WorkOrderTask', N'COLUMN', @c;
        FETCH NEXT FROM cur INTO @c, @t;
    END
    CLOSE cur; DEALLOCATE cur;

    DECLARE @nd TABLE (Col sysname, Txt nvarchar(200));
    INSERT @nd VALUES (N'PMStepID',            N'원래 PM 템플릿 단계 · MD_PmTemplateStep.PMStepID · varchar(24)'),
                      (N'TaskName',            N'점검 항목 · nvarchar(200)'),
                      (N'AcceptanceCriteria',  N'합격 기준 · nvarchar(200)'),
                      (N'RequiredSparePartNo', N'필요 예비품 · MD_SparePart.SparePartNo · varchar(16)'),
                      (N'RequiredQty',         N'필요 수량 · int'),
                      (N'StepDurationMin',     N'단계 소요 시간(분) · int'),
                      (N'Result',              N'점검 결과 · 공통코드 MNT_RESULT (PASS/FAIL/NA/PENDING) · varchar(10)');
    DECLARE cur2 CURSOR LOCAL FAST_FORWARD FOR SELECT Col, Txt FROM @nd;
    OPEN cur2; FETCH NEXT FROM cur2 INTO @c, @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID('dbo.MNT_WorkOrderTask')
                   AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MNT_WorkOrderTask'), @c, 'ColumnId') AND name = N'MS_Description')
            EXEC sp_updateextendedproperty N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'MNT_WorkOrderTask', N'COLUMN', @c;
        ELSE
            EXEC sp_addextendedproperty    N'MS_Description', @t, N'SCHEMA', N'dbo', N'TABLE', N'MNT_WorkOrderTask', N'COLUMN', @c;
        FETCH NEXT FROM cur2 INTO @c, @t;
    END
    CLOSE cur2; DEALLOCATE cur2;

    DROP TABLE #desc;
    COMMIT;
    PRINT 'MNT_WorkOrderTask rebuilt: PMStepID · AcceptanceCriteria · RequiredSparePartNo · RequiredQty · StepDurationMin, TaskName nvarchar(200)';
END
ELSE
    PRINT 'MNT_WorkOrderTask 점검 항목 컬럼 이미 있음';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MNT_WorkOrderTask_WorkOrderID' AND object_id = OBJECT_ID('dbo.MNT_WorkOrderTask'))
BEGIN
    CREATE INDEX IX_MNT_WorkOrderTask_WorkOrderID ON dbo.MNT_WorkOrderTask (WorkOrderID, TaskSeq);
    PRINT 'IX_MNT_WorkOrderTask_WorkOrderID created';
END
GO

-- 3) 공통코드 MNT_RESULT ---------------------------------------------
BEGIN TRAN;
MERGE dbo.MD_CodeGroup AS t
USING (SELECT 'MNT_RESULT' AS GroupCode, N'보전 점검 결과' AS GroupName, N'Maintenance check result' AS GroupNameEn,
              N'MNT_WorkOrderTask.Result — PASS 합격·FAIL 불합격·NA 해당 없음·PENDING 미점검' AS Description) s
   ON t.GroupCode = s.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (s.GroupCode, s.GroupName, s.GroupNameEn, s.Description, 1, 'system', SYSDATETIME());

MERGE dbo.MD_CodeItem AS t
USING (VALUES
        ('MNT_RESULT_PASS',    'PASS',    N'합격',      N'Pass',           10, N'합격 기준 충족'),
        ('MNT_RESULT_FAIL',    'FAIL',    N'불합격',    N'Fail',           20, N'합격 기준 미달 — 후속 조치 검토'),
        ('MNT_RESULT_NA',      'NA',      N'해당 없음', N'Not applicable', 30, N'이번 작업에 해당하지 않음'),
        ('MNT_RESULT_PENDING', 'PENDING', N'미점검',    N'Pending',        40, N'아직 점검하지 않음(발행 때 기본값)')
      ) AS s (CodeID, CodeValue, CodeName, CodeNameEn, SortOrder, Descr)
   ON t.CodeID = s.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, 'MNT_RESULT', s.CodeValue, s.CodeName, s.CodeNameEn, s.SortOrder, 1, s.Descr, 'system', SYSDATETIME());
COMMIT;
GO

SELECT c.column_id, c.name, t.name AS type_name,
       CASE WHEN t.name LIKE 'n%char' THEN c.max_length / 2 ELSE c.max_length END AS len
FROM   sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE  c.object_id = OBJECT_ID('dbo.MNT_WorkOrderTask') ORDER BY c.column_id;
SELECT PMStepID, RequiredSparePartNo, RequiredQty FROM dbo.MD_PmTemplateStep;
SELECT CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'MNT_RESULT' ORDER BY SortOrder;
GO
