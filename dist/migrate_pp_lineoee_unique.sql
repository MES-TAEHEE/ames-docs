-- ════════════════════════════════════════════════════════════════════════
--  migrate_pp_lineoee_unique.sql
--  PP-OEE 스냅샷 키 유니크 인덱스 — (LineID, PeriodDate, ShiftCode) (10-14)
--
--  OeeRepository.SaveSnapshot 은 같은 라인·일자·교대가 있으면 덮어쓰고 없으면 새로 만든다.
--  전에는 잠금 없이 UPDATE 0행이면 INSERT 해 동시 저장 때 같은 키 행이 2개 생겼다 —
--  저장소는 이제 키를 UPDLOCK·HOLDLOCK 으로 잡고 확인하며, 이 인덱스는 직접 SQL 등
--  다른 경로까지 막는 마지막 안전망이다(교대 NULL = 일 단위 스냅샷도 키당 1행).
--  PP_LineOEE 는 사무실 PP-OEE 화면만 쓰므로 현장 저장을 막지 않는다(FK 아님, 유니크).
--
--  같은 키 행이 이미 있으면 인덱스를 만들지 않고 목록을 보인 뒤 중단한다 — 정리 후 다시 실행.
--  스키마 변경 없음(인덱스만) — 적용 순서 무관, 재실행 안전.
--  적용:  sqlcmd -S <서버> -U ames_app -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_pp_lineoee_unique.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PP_LineOEE_Line_Date_Shift' AND object_id = OBJECT_ID('dbo.PP_LineOEE'))
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.PP_LineOEE GROUP BY LineID, PeriodDate, ShiftCode HAVING COUNT(*) > 1)
    BEGIN
        SELECT LineID, PeriodDate, ShiftCode, COUNT(*) AS Cnt,
               STRING_AGG(CAST(OeeSnapshotID AS varchar(12)), ',') AS SnapshotIDs
        FROM   dbo.PP_LineOEE
        GROUP  BY LineID, PeriodDate, ShiftCode
        HAVING COUNT(*) > 1;
        THROW 50000, 'PP_LineOEE 에 같은 (LineID, PeriodDate, ShiftCode) 행이 있습니다 — 위 목록에서 남길 행만 두고 지운 뒤 다시 실행하십시오.', 1;
    END
    CREATE UNIQUE NONCLUSTERED INDEX UX_PP_LineOEE_Line_Date_Shift ON dbo.PP_LineOEE (LineID, PeriodDate, ShiftCode);
    PRINT 'UX_PP_LineOEE_Line_Date_Shift created';
END
ELSE PRINT 'UX_PP_LineOEE_Line_Date_Shift already exists';
GO

SELECT i.name AS IndexName, i.is_unique AS IsUnique,
       STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.object_id = OBJECT_ID('dbo.PP_LineOEE') AND i.name = 'UX_PP_LineOEE_Line_Date_Shift'
GROUP  BY i.name, i.is_unique;
GO
