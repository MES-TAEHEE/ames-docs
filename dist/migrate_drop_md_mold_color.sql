-- ════════════════════════════════════════════════════════════════════════
--  migrate_drop_md_mold_color.sql
--  MD_MoldColor(금형 색상 목록, SIS ZPD20041) 테이블 삭제
--
--  금형별 제품정보(MD_MoldItem.Color)의 색상을 MD-007 화면에서 직접 입력하게 바꿔서
--  이 목록을 읽는 곳이 없어졌다(사용자 결정 10-16 — 따로 테이블을 두지 않는다).
--  PP-APS 형제 묶음·InjAgent 색상 매칭은 원래 MD_MoldItem.Color 만 본다.
--  외래키 FK_MD_MoldColor_Mold 는 테이블과 함께 삭제된다.
--
--  배포 순서: 신 Web → 이 마이그레이션. 구 Web 은 금형 삭제 때 이 테이블을 지우므로
--  먼저 적용하면 구 Web 의 MD-007 금형 삭제가 Invalid object name 으로 실패한다.
--  재실행 안전, 순서 무관(rebuild_db.sh 에서는 스키마에 테이블이 없어 건너뛴다).
--  적용:  sqlcmd -S <서버> -U ames_app -P ... -d AMES_DEV -f 65001 -I -b -i dist/migrate_drop_md_mold_color.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF OBJECT_ID(N'dbo.MD_MoldColor', N'U') IS NOT NULL
BEGIN
    DECLARE @n int = (SELECT COUNT(*) FROM dbo.MD_MoldColor);
    DROP TABLE dbo.MD_MoldColor;
    PRINT CONCAT(N'MD_MoldColor dropped (', @n, N' rows)');
END
ELSE
    PRINT N'MD_MoldColor already absent';
GO

SELECT OBJECT_ID(N'dbo.MD_MoldColor', N'U') AS md_moldcolor_object_id;
GO
