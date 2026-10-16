-- ════════════════════════════════════════════════════════════════════════
--  migrate_drop_user_assigned_lines.sql
--  SYS_UserProfile.AssignedLines(JSON 라인 목록) 삭제 — POP·PDA 로그인 라인 제한 폐지 (2026-09-24)
--
--  · 이 컬럼은 SYS-001 사용자 화면의 라인 선택과 POP 로그인(PopAuthService)의 라인 검사에만 쓰였다.
--    둘 다 제거됐으므로 이제 모든 사용자가 모든 라인에 로그인할 수 있다.
--  · 뷰·프로시저·인덱스·기본값 제약의 참조 없음(적용 전 확인). 컬럼 설명(extended property)은 DROP 때 같이 지워진다.
--  · 순서: 신 AMES.Web·AMES.Pop·AMES.Api 배포 후 적용. 구 바이너리는 이 컬럼을 SELECT 하므로
--    먼저 적용하면 구 SYS-001 목록·POP 로그인이 Invalid column name 으로 실패한다.
--  · 가드형, 재실행 안전. 기존 값은 되살릴 수 없다(필요하면 적용 전 백업).
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;

IF COL_LENGTH('dbo.SYS_UserProfile', 'AssignedLines') IS NOT NULL
BEGIN
    ALTER TABLE dbo.SYS_UserProfile DROP COLUMN AssignedLines;
    PRINT N'✓ SYS_UserProfile.AssignedLines 삭제';
END
ELSE
    PRINT N'· SYS_UserProfile.AssignedLines 이미 없음';
GO

SELECT c.column_id, c.name
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.SYS_UserProfile')
ORDER  BY c.column_id;
GO
