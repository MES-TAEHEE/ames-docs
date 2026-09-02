/* ------------------------------------------------------------------
   migrate_codeid_rule_fix.sql
   MD_CodeItem.CodeID 규칙 정합화: CodeID = GroupCode + '_' + CodeValue.
   과거 시드에서 CodeValue 부분이 절단된 CodeID(예: DAY_TYPE_WORKDA,
   DEFECT_SEVERITY_CRI, RFID_READER_STATUS_ONLIN 등)를 규칙대로 복원.
   가드형: 규칙을 이미 만족하면 아무 것도 하지 않음. 반복 실행 안전.
   전제(현 DB 확인 결과): CodeID 를 참조하는 FK 없음, ParentCodeID 참조 0건,
   기대값 충돌 0건. 기대값이 이미 다른 행으로 존재하면 해당 행은 건너뜀.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

-- (안전) 계층 코드가 생기는 경우를 대비해 ParentCodeID도 함께 갱신
UPDATE ch
   SET ch.ParentCodeID = pa.GroupCode + '_' + pa.CodeValue
FROM dbo.MD_CodeItem ch
JOIN dbo.MD_CodeItem pa ON pa.CodeID = ch.ParentCodeID
WHERE pa.CodeID <> pa.GroupCode + '_' + pa.CodeValue COLLATE Latin1_General_BIN
  AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem x
                  WHERE x.CodeID = pa.GroupCode + '_' + pa.CodeValue AND x.CodeID <> pa.CodeID);
PRINT CONCAT('ParentCodeID 갱신 행수: ', @@ROWCOUNT);

UPDATE ci
   SET ci.CodeID = ci.GroupCode + '_' + ci.CodeValue
FROM dbo.MD_CodeItem ci
WHERE ci.CodeID <> ci.GroupCode + '_' + ci.CodeValue COLLATE Latin1_General_BIN
  AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem x
                  WHERE x.CodeID = ci.GroupCode + '_' + ci.CodeValue AND x.CodeID <> ci.CodeID);
PRINT CONCAT('CodeID 교정 행수: ', @@ROWCOUNT);

COMMIT;

-- 확인: 남은 위반(없어야 함)
SELECT CodeID, GroupCode, CodeValue, GroupCode + '_' + CodeValue AS Expected
FROM   dbo.MD_CodeItem
WHERE  CodeID <> GroupCode + '_' + CodeValue COLLATE Latin1_General_BIN
ORDER  BY GroupCode, CodeValue;
