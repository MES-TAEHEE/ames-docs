-- MD SUBPROCESS 공통코드 등록 + SYS_Screen HRef/SubProcessCode 일괄 업데이트
-- 적용 대상: AMES_DEV (2026-07-09)
-- NOTE: DB의 ModuleCode='WEB', ProcessCode='MD' 구조를 기반으로 동작

USE AMES_DEV;
GO

-- ── 1. SUBPROCESS 공통코드 그룹 ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'SUBPROCESS')
    INSERT INTO dbo.MD_CodeGroup
        (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES
        ('SUBPROCESS', 'MD 서브 프로세스', 'MD Sub-Process',
         'MD 모듈 서브 그룹 분류 (NavMenu 계층 구조)', 1, 'system', SYSDATETIME());
GO

-- ── 2. SUBPROCESS 코드 아이템 (CodeValue = HRef 서브디렉토리 명) ─────────
-- 이미 존재하면 CodeValue/이름 정규화
IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE CodeID = 'SUBPROCESS_FD')
BEGIN
    UPDATE dbo.MD_CodeItem SET CodeValue='Fd', CodeName=N'기반정보',      CodeNameEn='Foundation',            SortOrder=10 WHERE CodeID='SUBPROCESS_FD';
    UPDATE dbo.MD_CodeItem SET CodeValue='Rp', CodeName=N'라우팅·생산',   CodeNameEn='Routing & Production',  SortOrder=20 WHERE CodeID='SUBPROCESS_RP';
    UPDATE dbo.MD_CodeItem SET CodeValue='Re', CodeName=N'자원·설비',     CodeNameEn='Resources & Equipment', SortOrder=30 WHERE CodeID='SUBPROCESS_RE';
    UPDATE dbo.MD_CodeItem SET CodeValue='Rm', CodeName=N'원부자재·도장', CodeNameEn='Raw Materials & Paint', SortOrder=40 WHERE CodeID='SUBPROCESS_RM';
    UPDATE dbo.MD_CodeItem SET CodeValue='Ql', CodeName=N'품질·물류',     CodeNameEn='Quality & Logistics',   SortOrder=50 WHERE CodeID='SUBPROCESS_QL';
END
ELSE
BEGIN
    INSERT INTO dbo.MD_CodeItem (CodeID,GroupCode,CodeValue,CodeName,CodeNameEn,SortOrder,Attribute1,UseFlag,CreatedBy,CreatedTS) VALUES
        ('SUBPROCESS_Fd','SUBPROCESS','Fd',N'기반정보',     'Foundation',            10,'MD',1,'system',SYSDATETIME()),
        ('SUBPROCESS_Rp','SUBPROCESS','Rp',N'라우팅·생산',  'Routing & Production',  20,'MD',1,'system',SYSDATETIME()),
        ('SUBPROCESS_Re','SUBPROCESS','Re',N'자원·설비',    'Resources & Equipment', 30,'MD',1,'system',SYSDATETIME()),
        ('SUBPROCESS_Rm','SUBPROCESS','Rm',N'원부자재·도장','Raw Materials & Paint', 40,'MD',1,'system',SYSDATETIME()),
        ('SUBPROCESS_Ql','SUBPROCESS','Ql',N'품질·물류',    'Quality & Logistics',   50,'MD',1,'system',SYSDATETIME());
END
GO

-- ── 3. SYS_Screen HRef 수정 (서브디렉토리 경로로 통일) ──────────────────
-- ProcessCode='MD', ModuleCode='WEB' 기준
-- Rp (라우팅·생산)
UPDATE dbo.SYS_Screen SET HRef='md/rp/line'              WHERE ProcessCode='MD' AND ScreenCode='MD-001';
UPDATE dbo.SYS_Screen SET HRef='md/rp/station'           WHERE ProcessCode='MD' AND ScreenCode='MD-002';
UPDATE dbo.SYS_Screen SET HRef='md/rp/bop'               WHERE ProcessCode='MD' AND ScreenCode='MD-005';
UPDATE dbo.SYS_Screen SET HRef='md/rp/work-center'       WHERE ProcessCode='MD' AND ScreenCode='MD-006';

-- Fd (기반정보)
UPDATE dbo.SYS_Screen SET HRef='md/fd/items'             WHERE ProcessCode='MD' AND ScreenCode='MD-003';
UPDATE dbo.SYS_Screen SET HRef='md/fd/bom'               WHERE ProcessCode='MD' AND ScreenCode='MD-004';
UPDATE dbo.SYS_Screen SET HRef='md/fd/uom'               WHERE ProcessCode='MD' AND ScreenCode='MD-019';

-- Re (자원·설비) — reason-code, spare-part ScreenCode는 DB 실제값 기준
UPDATE dbo.SYS_Screen SET HRef='md/re/mold'              WHERE ProcessCode='MD' AND HRef IN ('md/mold','md/re/mold');
UPDATE dbo.SYS_Screen SET HRef='md/re/vendor'            WHERE ProcessCode='MD' AND HRef IN ('md/vendor','md/re/vendor');
UPDATE dbo.SYS_Screen SET HRef='md/re/equipment'         WHERE ProcessCode='MD' AND HRef IN ('md/equipment','md/re/equipment');
UPDATE dbo.SYS_Screen SET HRef='md/re/oven'              WHERE ProcessCode='MD' AND HRef IN ('md/oven','md/re/oven');
UPDATE dbo.SYS_Screen SET HRef='md/re/jig'               WHERE ProcessCode='MD' AND HRef IN ('md/jig','md/re/jig');
UPDATE dbo.SYS_Screen SET HRef='md/re/reason-code'       WHERE ProcessCode='MD' AND HRef IN ('md/reason-code','md/re/reason-code');
UPDATE dbo.SYS_Screen SET HRef='md/re/spare-part'        WHERE ProcessCode='MD' AND HRef IN ('md/spare-part','md/re/spare-part');

-- Rp (추가 — pm-template, line-time-pattern)
UPDATE dbo.SYS_Screen SET HRef='md/rp/pm-template'       WHERE ProcessCode='MD' AND HRef IN ('md/pm-template','md/rp/pm-template');
UPDATE dbo.SYS_Screen SET HRef='md/rp/line-time-pattern' WHERE ProcessCode='MD' AND HRef IN ('md/line-time-pattern','md/rp/line-time-pattern');

-- Rm (원부자재·도장) — 이미 올바른 경로이나 확인 차 업데이트
UPDATE dbo.SYS_Screen SET HRef='md/rm/paint-fabric'      WHERE ProcessCode='MD' AND HRef IN ('md/paint-fabric','md/rm/paint-fabric');
UPDATE dbo.SYS_Screen SET HRef='md/rm/rfid-tag'          WHERE ProcessCode='MD' AND HRef IN ('md/rfid-tag','md/rm/rfid-tag');
UPDATE dbo.SYS_Screen SET HRef='md/rm/ral-color'         WHERE ProcessCode='MD' AND HRef IN ('md/ral-color','md/rm/ral-color');
UPDATE dbo.SYS_Screen SET HRef='md/rm/rfid-reader'       WHERE ProcessCode='MD' AND HRef IN ('md/rfid-reader','md/rm/rfid-reader');

-- Ql (품질·물류) — 이미 올바른 경로이나 확인 차 업데이트
UPDATE dbo.SYS_Screen SET HRef='md/ql/customer'            WHERE ProcessCode='MD' AND HRef IN ('md/customer','md/ql/customer');
UPDATE dbo.SYS_Screen SET HRef='md/ql/shipment-dest'       WHERE ProcessCode='MD' AND HRef IN ('md/shipment-dest','md/ql/shipment-dest');
UPDATE dbo.SYS_Screen SET HRef='md/ql/defect-code'         WHERE ProcessCode='MD' AND HRef IN ('md/defect-code','md/ql/defect-code');
UPDATE dbo.SYS_Screen SET HRef='md/ql/defect-cause'        WHERE ProcessCode='MD' AND HRef IN ('md/defect-cause','md/ql/defect-cause');
UPDATE dbo.SYS_Screen SET HRef='md/ql/inspection-standard' WHERE ProcessCode='MD' AND HRef IN ('md/inspection-standard','md/ql/inspection-standard');
UPDATE dbo.SYS_Screen SET HRef='md/ql/packaging-spec'      WHERE ProcessCode='MD' AND HRef IN ('md/packaging-spec','md/ql/packaging-spec');
UPDATE dbo.SYS_Screen SET HRef='md/ql/label-template'      WHERE ProcessCode='MD' AND HRef IN ('md/label-template','md/ql/label-template');
UPDATE dbo.SYS_Screen SET HRef='md/ql/recipe'              WHERE ProcessCode='MD' AND HRef IN ('md/recipe','md/ql/recipe');
UPDATE dbo.SYS_Screen SET HRef='md/ql/common-code'         WHERE ProcessCode='MD' AND HRef IN ('md/common-code','md/ql/common-code');

-- Fd 추가 — Location (창고/로케이션)
UPDATE dbo.SYS_Screen SET HRef='md/fd/location'            WHERE ProcessCode='MD' AND HRef IN ('md/location','md/fd/location','md/ql/location');
GO

-- ── 4. SubProcessCode를 HRef 경로 기반으로 설정 ───────────────────────────
UPDATE dbo.SYS_Screen SET SubProcessCode='Fd' WHERE ProcessCode='MD' AND HRef LIKE 'md/fd/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Rp' WHERE ProcessCode='MD' AND HRef LIKE 'md/rp/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Re' WHERE ProcessCode='MD' AND HRef LIKE 'md/re/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Rm' WHERE ProcessCode='MD' AND HRef LIKE 'md/rm/%';
UPDATE dbo.SYS_Screen SET SubProcessCode='Ql' WHERE ProcessCode='MD' AND HRef LIKE 'md/ql/%';
UPDATE dbo.SYS_Screen SET SubProcessCode=NULL WHERE ProcessCode='MD' AND HRef NOT LIKE 'md/%/%';
GO

-- ── 검증 ─────────────────────────────────────────────────────────────────
SELECT ScreenCode, SubProcessCode, HRef
FROM   dbo.SYS_Screen
WHERE  ProcessCode = 'MD'
ORDER  BY SubProcessCode, ScreenCode;

SELECT CodeValue, CodeName, CodeNameEn, SortOrder
FROM   dbo.MD_CodeItem
WHERE  GroupCode = 'SUBPROCESS'
ORDER  BY SortOrder;

PRINT '✓ SUBPROCESS 공통코드 정규화 + MD 화면 HRef/SubProcessCode 업데이트 완료';
GO
