/* ------------------------------------------------------------------
   run_seed_md_item_bom_master_list.sql
   이미 운영 중인 DB 에 seed_md_item_bom_master_list.sql 을 단독 적용한다(rebuild_db.sh 없이).

   시드는 품목·BOM 을 FK 로 참조하는 행이 있으면 중단한다. 마지막 FK(FG_CustomerReturn → MD_Item)는
   migrate_drop_fg_customer_return_item_fk.sql 이 지우므로 그것을 먼저 적용하고, 이 스크립트는
   다른 테이블 데이터를 지우지 않는다. FK 없는 참조(수주·Forecast·WO·LOT·재고·BOP·반품 등)는
   그대로이며 엑셀에 없는 품번을 가리키는 행은 고아로 남는다(사용자 결정).

   전체가 트랜잭션 하나다. Mode=ROLLBACK 이면 끝에서 되돌리고(시험 실행), COMMIT 이면 반영한다.
   중간 오류는 -b 로 중단되며 연결이 끊기면서 전부 롤백된다.

   실행 (저장소 루트에서 — :r 경로가 현재 디렉터리 기준):
     sqlcmd -S "98.95.142.192,1433" -d AMES_DEV -U ames_app -P <pw> -C -f 65001 -I -b ^
            -v Mode=ROLLBACK -i dist\run_seed_md_item_bom_master_list.sql
     → 결과 확인 후 -v Mode=COMMIT 로 다시 실행
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF '$(Mode)' NOT IN ('ROLLBACK', 'COMMIT') THROW 50100, N'Mode 는 ROLLBACK 또는 COMMIT', 1;
PRINT CONCAT(N'대상: ', @@SERVERNAME, N' / ', DB_NAME(), N' / Mode=$(Mode)');
DECLARE @before nvarchar(200) = CONCAT(N'적용 전: MD_Item ', (SELECT COUNT(*) FROM dbo.MD_Item), N' · MD_BomVersion ', (SELECT COUNT(*) FROM dbo.MD_BomVersion),
             N' · MD_Bom ', (SELECT COUNT(*) FROM dbo.MD_Bom), N' · SCM_ItemVendor ', (SELECT COUNT(*) FROM dbo.SCM_ItemVendor));
PRINT @before;
BEGIN TRAN;
GO

:r dist\seed_md_item_bom_master_list.sql

SET NOCOUNT ON;
IF @@TRANCOUNT <> 1 THROW 50101, N'트랜잭션 상태가 예상과 다르다 — 반영하지 않는다', 1;
DECLARE @after nvarchar(200) = CONCAT(N'적용 후: MD_Item ', (SELECT COUNT(*) FROM dbo.MD_Item), N' · MD_BomVersion ', (SELECT COUNT(*) FROM dbo.MD_BomVersion),
             N' · MD_Bom ', (SELECT COUNT(*) FROM dbo.MD_Bom), N' · SCM_ItemVendor ', (SELECT COUNT(*) FROM dbo.SCM_ItemVendor));
PRINT @after;
IF '$(Mode)' = 'COMMIT'
BEGIN COMMIT;   PRINT N'COMMIT — 반영됨'; END
ELSE
BEGIN ROLLBACK; PRINT N'ROLLBACK — 시험 실행, 반영 안 됨'; END;
GO
