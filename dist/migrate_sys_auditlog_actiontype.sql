/* ------------------------------------------------------------------
   migrate_sys_auditlog_actiontype.sql  (2026-10-15)

   SYS_AuditLog.ActionType  varchar(15) → varchar(20)  (감사 동작 코드 — 사용자 결정)
     · ALTER COLUMN 이라 컬럼 위치는 그대로다. 기존 값은 그대로. 인덱스·제약 없음, NULL 허용 그대로
     · 같이 바뀐 곳: AMES.Web AuditLogger 의 동작 코드 절단 15 → 20자,
       SYS-007 감사 로그 화면(Sys/Audit.razor) 동작 열 너비
       (SysRepository.InsertAuditLog 는 ColumnParams 로 DB 컬럼 길이를 읽으므로 코드 변경 없음)
     · 이 테이블은 컬럼 설명(MS_Description)이 없어 설명은 더하지 않는다
     · DB 프로시저 SP_PDA_STOCK_MOVE·SP_PDA_SP_STOCK_CANCEL 은 IN/OUT/…_CANCEL 만 넣어 변경 없음

   · 재실행 안전(이미 varchar(20) 이면 건너뛴다). 적용: sqlcmd -f 65001 -I -b
   · 구 Web 과 함께 써도 예외는 없다 — 구 Web 은 동작 코드를 15자로 잘라 넣을 뿐이다.
     실행 중인 신 Web·Api 는 컬럼 길이를 처음 한 번 읽어 캐시하므로, 적용 뒤 앱풀을 재활용(또는 재게시)해야 16~20자 코드가 저장된다
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.SYS_AuditLog') AND name = 'ActionType'
             AND (TYPE_NAME(user_type_id) <> 'varchar' OR max_length <> 20))
BEGIN
    ALTER TABLE dbo.SYS_AuditLog ALTER COLUMN ActionType varchar(20) COLLATE Korean_Wansung_CI_AS NULL;
END

GO

-- 확인
SELECT c.name, TYPE_NAME(c.user_type_id) AS type_name, c.max_length, c.is_nullable, c.collation_name, c.column_id
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.SYS_AuditLog') AND c.name = 'ActionType';
