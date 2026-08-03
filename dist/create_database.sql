-- ============================================================
-- create_database.sql
-- AMES_DEV 데이터베이스 생성 — 콜레이션 Korean_Wansung_CI_AS 고정
-- ★ AMES_Schema.sql 실행 전에 먼저 실행할 것 (스키마는 USE [AMES_DEV] 로 시작하며 DB 생성/콜레이션 지정 안 함).
--   AMES_Schema.sql 의 컬럼들은 COLLATE 명시가 없어 이 DB 기본 콜레이션(Korean)을 상속한다.
-- 서버/tempdb 도 Korean_Wansung_CI_AS 라 임시테이블 조인 콜레이션 충돌도 예방됨.
-- 접속: sqlcmd -S "<server>" -E(또는 -U ... -P ...) -i dist\create_database.sql
-- ============================================================
IF DB_ID('AMES_DEV') IS NULL
BEGIN
    CREATE DATABASE AMES_DEV COLLATE Korean_Wansung_CI_AS;
    PRINT 'AMES_DEV 생성 (COLLATE Korean_Wansung_CI_AS)';
END
ELSE
    PRINT 'AMES_DEV 이미 존재 — 스킵 (콜레이션은 변경하지 않음)';
GO
