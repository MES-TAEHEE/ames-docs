-- ============================================================
-- create_database.sql
-- AMES_DEV 데이터베이스 생성 — 콜레이션 SQL_Latin1_General_CP1_CI_AS 고정(10-29 사용자 결정: 모든 DB 를 이 콜레이션으로 통일)
-- ★ AMES_Schema.sql 실행 전에 먼저 실행할 것 (스키마는 DB 생성/콜레이션 지정 안 함).
--   AMES_Schema.sql·마이그레이션의 컬럼은 COLLATE DATABASE_DEFAULT 라 이 DB 기본 콜레이션을 상속한다.
-- 서버(tempdb)도 같은 콜레이션이어야 임시테이블 조인에서 콜레이션 충돌이 없다.
-- varchar·char 는 CP1252 라 한글을 담지 못한다 — 한글이 들어가는 값은 nvarchar 컬럼에 둔다.
-- 접속: sqlcmd -S "<server>" -E(또는 -U ... -P ...) -i dist\create_database.sql
-- ============================================================
IF DB_ID('AMES_DEV') IS NULL
BEGIN
    CREATE DATABASE AMES_DEV COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'AMES_DEV 생성 (COLLATE SQL_Latin1_General_CP1_CI_AS)';
END
ELSE
    PRINT 'AMES_DEV 이미 존재 — 스킵 (콜레이션은 변경하지 않음)';
GO
