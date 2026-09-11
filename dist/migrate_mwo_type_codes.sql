/* ------------------------------------------------------------------
   migrate_mwo_type_codes.sql
   공통코드 그룹 MWO_TYPE(정비 작업지시 유형) 신설 — MNT_WorkOrder.WoType 의 값.
     PM  예방정비 (Preventive Maintenance)  — MNT-005/010 PM 등록이 자동 발행
     CM  사후정비 (Corrective Maintenance)  — MNT-002 고장 등록이 자동 발행
     PdM 예지정비 (Predictive Maintenance)  — 발행 경로 없음(시연 데이터)
   CodeValue 는 기존 MNT_WorkOrder 행의 값(PM/CM/PdM)과 같게 두어 데이터 변경이 없다.
   가드형: 그룹·아이템이 이미 있으면 건너뜀. 반복 실행 안전.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'MWO_TYPE')
BEGIN
    INSERT INTO dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('MWO_TYPE', N'정비 작업지시 유형', N'Maintenance WO type', N'MNT_WorkOrder.WoType', 1, 'admin@ames.local');
    PRINT 'MD_CodeGroup: MWO_TYPE 추가';
END
ELSE
    PRINT 'MD_CodeGroup: MWO_TYPE 이미 존재';

DECLARE @items TABLE (CodeValue VARCHAR(10), NameKo NVARCHAR(50), NameEn NVARCHAR(50), SortOrder INT, Descr NVARCHAR(200));
INSERT @items VALUES
    ('PM',  N'예방정비', N'Preventive', 10, N'PM 일정 등록 시 자동 발행'),
    ('CM',  N'사후정비', N'Corrective', 20, N'고장 등록 시 자동 발행'),
    ('PdM', N'예지정비', N'Predictive', 30, NULL);

INSERT INTO dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
SELECT 'MWO_TYPE_' + i.CodeValue, 'MWO_TYPE', i.CodeValue, i.NameKo, i.NameEn, NULL, i.SortOrder, NULL, 1, i.Descr, 'admin@ames.local'
FROM   @items i
WHERE  NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.GroupCode = 'MWO_TYPE' AND c.CodeValue = i.CodeValue);
PRINT CONCAT('MD_CodeItem 추가 행수: ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT CodeID, CodeValue, CodeName, CodeNameEn, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'MWO_TYPE' ORDER BY SortOrder;
SELECT 'MNT_WorkOrder 에 있으나 코드 미등록인 WoType' AS Chk, WoType, COUNT(*) AS Cnt
FROM   dbo.MNT_WorkOrder w
WHERE  w.WoType IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem c WHERE c.GroupCode = 'MWO_TYPE' AND c.CodeValue = w.WoType)
GROUP  BY WoType;
