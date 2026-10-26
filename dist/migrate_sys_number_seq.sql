-- ════════════════════════════════════════════════════════════════════════
-- migrate_sys_number_seq.sql — MNT 문서 번호 채번 카운터(10-26)
--
-- 고장번호 FAIL-yyMM-nnn · 보전 작업지시 MWO-yyMM-nnn · PM 계획번호 PM-yyMM-nnn 은 그 달 번호 범위를
-- UPDLOCK·HOLDLOCK 으로 잡고 MAX+1 을 셌다. 같은 범위를 동시에 읽고 새 키를 넣는 채번이 몰리면(특히 달 초 범위가
-- 비어 있을 때) 범위 잠금끼리 엇갈려 교착(1205)이 났다(10-26 로컬 DB system_health — 고장 번호 인덱스 안의
-- RangeS-U 고리). 번호 종류마다 미리 만든 행 하나를 잠그는 카운터로 바꿔 범위 잠금을 없앤다.
--   · SYS_NumberSeq: 번호 종류(FAIL·MWO·PM)당 1행 — PeriodKey(yyMM)·LastNo. 달이 바뀌어도 행을 새로 넣지 않는다.
--   · SP_SYS_NumberSeq_Next: 그 행을 잠그고 다음 번호 = max(그 달 카운터, 그 달 실제 최대 번호) + 1.
--     직접 입력한 큰 번호·카운터 도입 전 번호와도 겹치지 않는다. 웹(MntRepository.ClaimNumber)과
--     POP 안돈 보전 호출(SP_POP_ANDON_CallDepts_InsMNT_FailureRegister)이 같은 프로시저를 쓴다.
--   · 잠금 순서: 고장 등록은 FAIL → MWO, PM 등록은 PM → MWO — MWO 를 먼저 잡고 FAIL·PM 을 잡는 경로는 없다.
--
-- 재실행 안전. 신 Web 보다 먼저 적용(신 Web 은 이 프로시저를 부른다). 구 Web 은 그대로 동작한다(구 방식 MAX+1 과
-- 섞여도 카운터가 실제 최대 번호를 함께 보므로 겹치지 않는다 — 다만 구 Web 끼리의 교착 위험은 남는다).
-- 적용: sqlcmd -S <server> -d AMES_DEV -U ames_app -P ... -f 65001 -I -b -i dist/migrate_sys_number_seq.sql
-- ════════════════════════════════════════════════════════════════════════
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID(N'dbo.SYS_NumberSeq', N'U') IS NULL
CREATE TABLE dbo.SYS_NumberSeq (
  [SeqCode]    VARCHAR(10) NOT NULL,
  [PeriodKey]  CHAR(4)     NOT NULL,
  [LastNo]     INT         NOT NULL,
  [ModifiedTS] DATETIME2   NOT NULL CONSTRAINT DF_SYS_NumberSeq_ModifiedTS DEFAULT SYSDATETIME(),
  CONSTRAINT PK_SYS_NumberSeq PRIMARY KEY CLUSTERED ([SeqCode])
);
GO

-- 번호 종류 행은 미리 만든다 — 채번 중에 행을 넣으면 그 자리에서 다시 범위 잠금이 생긴다.
-- PeriodKey '0000' 으로 두면 첫 채번이 그 달 실제 최대 번호에서 이어 간다.
INSERT INTO dbo.SYS_NumberSeq (SeqCode, PeriodKey, LastNo)
SELECT v.SeqCode, '0000', 0
FROM   (VALUES ('FAIL'), ('MWO'), ('PM')) v(SeqCode)
WHERE  NOT EXISTS (SELECT 1 FROM dbo.SYS_NumberSeq s WHERE s.SeqCode = v.SeqCode);
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID(N'dbo.SYS_NumberSeq') AND minor_id = 0 AND name = N'MS_Description')
  EXEC sys.sp_addextendedproperty N'MS_Description', N'MNT 문서 번호 채번 카운터 — 번호 종류(FAIL·MWO·PM)당 1행, 그 달(PeriodKey yyMM)의 마지막 순번', N'SCHEMA', N'dbo', N'TABLE', N'SYS_NumberSeq';
GO

CREATE OR ALTER PROCEDURE dbo.SP_SYS_NumberSeq_Next
    @SeqCode VARCHAR(10),
    @Ym      CHAR(4),
    @No      INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @period CHAR(4), @last INT;
    -- 이 행 하나를 트랜잭션 끝까지 잠가 같은 번호 종류의 채번을 줄 세운다(범위 잠금 없음)
    UPDATE dbo.SYS_NumberSeq WITH (ROWLOCK)
    SET    @period = PeriodKey, @last = LastNo, ModifiedTS = SYSDATETIME()
    WHERE  SeqCode = @SeqCode;
    IF @@ROWCOUNT = 0
    BEGIN
        DECLARE @msg NVARCHAR(200) = N'SYS_NumberSeq row missing: ' + @SeqCode;
        THROW 52100, @msg, 1;
    END;

    DECLARE @prefix VARCHAR(20) = @SeqCode + '-' + @Ym + '-';
    DECLARE @max INT =
        CASE @SeqCode
            WHEN 'FAIL' THEN (SELECT MAX(TRY_CAST(SUBSTRING(FailureNumber, LEN(@prefix) + 1, 10) AS INT))
                              FROM dbo.MNT_FailureRegister WHERE FailureNumber LIKE @prefix + '%')
            WHEN 'MWO'  THEN (SELECT MAX(TRY_CAST(SUBSTRING(WoNumber, LEN(@prefix) + 1, 10) AS INT))
                              FROM dbo.MNT_WorkOrder WHERE WoNumber LIKE @prefix + '%')
            WHEN 'PM'   THEN (SELECT MAX(TRY_CAST(SUBSTRING(PMPlanNumber, LEN(@prefix) + 1, 10) AS INT))
                              FROM dbo.MNT_PMSchedule WHERE PMPlanNumber LIKE @prefix + '%')
        END;

    SET @No = CASE WHEN @period = @Ym THEN @last ELSE 0 END;
    IF ISNULL(@max, 0) > @No SET @No = @max;
    SET @No = @No + 1;

    UPDATE dbo.SYS_NumberSeq SET PeriodKey = @Ym, LastNo = @No WHERE SeqCode = @SeqCode;
END;
GO

-- POP 안돈 보전 호출의 고장 등록 — 고장번호를 같은 카운터로 받는다(migrate_pop_stored_procedures.sql 의 같은 프로시저와 동일 본문)
CREATE OR ALTER PROCEDURE dbo.SP_POP_ANDON_CallDepts_InsMNT_FailureRegister
    @Ref VARCHAR(24),
    @C VARCHAR(30),
    @S VARCHAR(10),
    @By VARCHAR(20),
    @Op VARCHAR(20),
    @ID INT
AS
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.MNT_FailureRegister WHERE Source = 'ANDON' AND AndonRefID = @Ref)
    BEGIN
        -- 고장번호 채번 = SYS_NumberSeq 카운터(웹 MntRepository.ClaimNumber 와 공용, dist/migrate_sys_number_seq.sql) — 범위 잠금 교착 방지(10-26)
        DECLARE @ym  CHAR(4) = FORMAT(SYSDATETIME(), 'yyMM');
        DECLARE @seq INT;
        EXEC dbo.SP_SYS_NumberSeq_Next @SeqCode = 'FAIL', @Ym = @ym, @No = @seq OUTPUT;
        INSERT INTO dbo.MNT_FailureRegister
            (FailureNumber, EquipID, FailureType, Symptom, Severity, Source, AndonRefID, DowntimeID,
             Status, ReportedBy, ReportedAt, CreatedBy, CreatedTS)
        SELECT 'FAIL-' + @ym + '-' + RIGHT('000' + CAST(@seq AS VARCHAR(4)), 3),
               a.EquipID, @C,
               N'Andon #' + @Ref + N' · ' + COALESCE(ci.CodeName, @C),
               @S, 'ANDON', @Ref, d.DowntimeID,
               'OPEN', @By, SYSDATETIME(), @Op, SYSDATETIME()
        FROM   dbo.PR_AndonCall a
        LEFT   JOIN dbo.MD_CodeItem ci ON ci.GroupCode = 'ANDON_CAUSE' AND ci.CodeValue = @C
        OUTER  APPLY (SELECT TOP 1 DowntimeID FROM dbo.PP_LineDowntimeLog WHERE AndonID = a.AndonID ORDER BY DowntimeID DESC) d
        WHERE  a.AndonID = @ID;
    END
END;
GO

SELECT SeqCode, PeriodKey, LastNo FROM dbo.SYS_NumberSeq ORDER BY SeqCode;
GO
PRINT N'✓ migrate_sys_number_seq.sql applied';
GO
