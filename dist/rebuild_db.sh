#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# AMES_DEV rebuild (Docker on Linux/Ubuntu)
#   AMES_DEV 를 드롭 후 재생성: 스키마(전체 구조) → 시드 12개 순서대로 적용.
#   컨테이너 내부 sqlcmd 를 docker exec 로 호출 → 리눅스 sqlcmd 가 UTF-8 을
#   그대로 읽어 한글이 깨지지 않음 (Windows 의 -f 65001 불필요).
#
#   Windows(PowerShell) 대응본은 dist/rebuild_db.ps1.
#
# 사용법:
#   ./dist/rebuild_db.sh            # 확인 프롬프트 후 재구축
#   ./dist/rebuild_db.sh --force    # 무확인 재구축
#   CONTAINER=ames-mssql SA_PASSWORD='AmesDev!2026Sa' ./dist/rebuild_db.sh --force
#
# ⚠️ 현재 AMES_DEV 의 모든 데이터가 삭제됩니다.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

CONTAINER="${CONTAINER:-ames-mssql}"
SA_PASSWORD="${SA_PASSWORD:-AmesDev!2026Sa}"
APP_LOGIN="${APP_LOGIN:-ames_app}"
APP_PASSWORD="${APP_PASSWORD:-!Dev2026}"
DB="${DB:-AMES_DEV}"
if [[ "$DB" != "AMES_DEV" ]]; then
  echo "ERROR: bundled SQL contains USE AMES_DEV; rebuilding another database is blocked." >&2
  exit 1
fi
FORCE=0
[[ "${1:-}" == "--force" || "${1:-}" == "-f" ]] && FORCE=1

# dist/ 디렉터리 — 기본값은 스크립트 위치. 스크립트를 다른 곳에 두고 실행하면
# DIST_DIR 환경변수로 실제 dist 경로를 지정하세요.
#   예: DIST_DIR=/home/ubuntu/projects/ames-docs/dist ~/rebuild_db.sh --force
DIST_DIR="${DIST_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)}"
if [[ ! -f "$DIST_DIR/AMES_Schema.sql" ]]; then
  echo "ERROR: '$DIST_DIR' 에 AMES_Schema.sql 이 없습니다." >&2
  echo "       DIST_DIR 환경변수로 프로젝트의 dist 폴더를 지정하세요:" >&2
  echo "       DIST_DIR=/home/ubuntu/projects/ames-docs/dist $0 $*" >&2
  exit 1
fi

# 2022 이미지는 mssql-tools18. 구 이미지 fallback 자동 탐지.
SQLCMD="/opt/mssql-tools18/bin/sqlcmd"
if ! docker exec "$CONTAINER" test -x "$SQLCMD" 2>/dev/null; then
  SQLCMD="/opt/mssql-tools/bin/sqlcmd"
fi

# -b: 오류 시 중단 / -I: QUOTED_IDENTIFIER ON (계산컬럼 인덱스 있는 MD_Mold 에
#     DELETE/INSERT 하려면 필수. sqlcmd 기본값은 OFF)
sql_base=(-S localhost -U sa -P "$SA_PASSWORD" -C -b -I)
run_master() { docker exec "$CONTAINER" "$SQLCMD" "${sql_base[@]}" -Q "$1"; }
run_file()   { docker exec "$CONTAINER" "$SQLCMD" "${sql_base[@]}" -d "$DB" -i "/tmp/dist/$1"; }

# 스키마 → 시드 (순서 고정!). 권한은 화면(create_sys_screen) 이후여야 매핑됨.
FILES=(
  AMES_Schema.sql                     # 실제 DB 통합 구조 (PDA 포함, TEST_* 테이블 제외) + 기존 기본 시드
  # Fresh AMES_Schema.sql already contains the consolidated WH/FG definition.
  # Legacy upgrade migrations must not overwrite it during a rebuild.
  seed_user_code_groups.sql           # 공통 코드 그룹
  # SYS_Screen is seeded by AMES_Schema.sql; old drop/truncate scripts are excluded.
  migrate_system_roles.sql            #   → 기본 역할 6개 고정 ID(ROLE-SYSADMIN·SUPERVISOR·OPERATOR·MAINTENANCE·QUALITY·PRODUCTION) — Admin 권한 시드보다 먼저(10-09)
  seed_admin_permissions.sql          #   → Admin RBAC(REA) + admin@ames.local 계정
  seed_md_code.sql                    # 마스터 공통코드
  seed_md_customer_seoyon.sql         # 고객 마스터 = Seoyon 세 플랜트(SAV·GEO·AUB), 데모 OEM 삭제
  seed_md_routing_step.sql            # 라우팅 템플릿(A/B) 시드 (테이블은 AMES_Schema.sql)
  seed_md_item_bom_master_list.sql    # 품목·BOM 정본 (docs/260828 BOM Master List.xlsx → tools/gen_md_item_bom_seed.py 생성) — MD_Item/MD_Bom 전체 재적재, FG_* 시드(PDA_SEED) 이전이어야 한다
  migrate_inj_agent.sql               # 사출: PR_InjLot·사출조건 테이블 + 사출조건 시드 (금형 시드는 seed_md_mold_master)
  migrate_mold_master.sql             # 금형: 자식 3종 → MD_Mold FK (시드 없음)
  migrate_mold_change_plan.sql        # 금형 교체 시간: MD_Mold.MoldChangeMin + PP_LineSchedule.MoldID (순서 무관)
  migrate_routing_step.sql            # 라우팅: MD_RoutingStep 생성 + A/B 시퀀스 시드 (seed_admin_permissions 이후 — Admin 권한 필요)
  migrate_wo_step_line.sql            # WO 공정 단계: PP_WorkOrderRouting CompletedQty·TerminalLock·인덱스 + 백필 (migrate_routing_step 이후)
  migrate_wo_step_item.sql            # WO 단계 생산 품번: PP_WorkOrderRouting.ItemNo (INJ 단계 = BOM 코어) + (LineID, ItemNo) 인덱스 (migrate_wo_step_line 이후)
  seed_inj_img_master_dev.sql         #   → 개발용 INJ(코어)/IMG(완제품) 스테이션 BOP (품목 시드 이후, 운영 금지)
  seed_pnt_dev.sql                    #   → 개발용 PNT 데모 완제품 2개(라우팅 B)·BOM·ST-PNT-01 BOP (품목 시드 이후, 운영 금지)
  seed_md_mold_master.sql             # 금형 마스터 정본 — MD_Item 사출 성형 SUB(코어·레일) 기준 MD_Mold·MoldItem·MoldLine 전체 재적재 (품목 시드·migrate_mold_master·migrate_mold_change_plan 이후)
  seed_pp_customer_order_dev.sql      #   → 개발용 가상 수주 231건(ASSY 77 × 3회차, Confirmed) — seed_md_customer_seoyon 이 먼저여야 함
  migrate_wo_prod_deadline.sql        # WO 생산 마감일 컬럼 + PP_PROD_BUFFER_WORKDAYS 설정 (순서 무관)
  migrate_mnt_failure_severity.sql    # MNT 고장 등록: Urgency → Severity 컬럼명 변경 (순서 무관)
  # MD_Worker 최신 EmployeeNo/EmployeeName 구조는 AMES_Schema.sql에 포함.
  # migrate_md_worker.sql은 이전 WorkerNo 컬럼용이므로 신규 DB에는 적용하지 않음.
  seed_md_worker_dev.sql              #   → 개발용 작업자 5명 (W001~W005)
  migrate_md_worker_screen.sql        # MD-032 현장 작업자 관리 화면 등록 + Admin 권한 (순서 무관)
  migrate_mnt_pm_class_type.sql       # MNT_PMSchedule.PMClass 추가·PMType ANNUALLY·공통코드 PM_CLASS/PM_TYPE (순서 무관)
  migrate_mnt_pm_screens.sql          # MNT-005 설비 PM·MNT-010 보전 PM 화면 등록 + 공통코드 PM_CYCLE_BASIS/PM_STATUS (순서 무관)
  migrate_mnt_failure_codes.sql       # MNT-002 고장 등록 모달용 공통코드 FAILURE_TYPE/FAILURE_SOURCE (순서 무관)
  migrate_md_item_mount_pos.sql       # MD_Item.MountPos 완제품 라벨 장착위치 (순서 무관)
  migrate_pr_result_prod_shift.sql    # PR_ProductionResult.ProdDate·ShiftCode + 백필 (순서 무관)
  migrate_day_cutoff_config.sql       # 전기일 기준 시각 공통코드 DAY_CUTOFF → SYS_Config.DAY_CUTOFF_TIME 이관 (재실행 안전)
  migrate_pp_so_auto_confirm.sql      # PP-002 수주 자동 확정 플래그 SYS_Config.PP_SO_AUTO_CONFIRM (기본 false, 순서 무관)
  migrate_bom_status_code.sql         # BOM 버전 상태 공통코드 BOM_STATUS + 기간 지난 승인 버전 EXPIRED (재실행 안전)
  migrate_md_codeitem_widen.sql       # PO source URLs and service-key metadata need current widths
  migrate_md_codeitem_attribute2.sql  # MD_CodeItem.Attribute2 nvarchar(200) (Attribute1 다음, 재생성 — widen 다음, 스키마에 이미 있으면 건너뜀)
  migrate_md_item_inj_flag.sql        # MD_Item.InjFlag 사출품 여부 bit (ItemCategory 다음, 재생성 — 스키마에 이미 있으면 건너뜀)
  migrate_sys_factory_calendar_net_hours.sql # SYS_FactoryCalendar.NetWorkHours decimal(4,1) → (4,2) (스키마에 이미 있으면 건너뜀)
  migrate_sys_public_holiday.sql      # SYS_PublicHoliday 테이블 + SP_SYS_FactoryCalendar_Fill (에이전트 작업은 setup_job_factory_calendar_fill.sql, sysadmin)
  seed_sys_public_holiday.sql         # 공휴일 2026–2050 350건(현대·기아 협력사 기준, 10-09 개발 DB 데이터)
  migrate_pp_mrp_result.sql           # PP-005 MRP 결과 스냅샷 PP_MRPResult/Wo + MD_Item.LeadTimeDays (순서 무관)
  migrate_pp_pr_send.sql              # PP-006 구매요청 SAP 전송 상태 컬럼 + 상태 어휘 이관 (순서 무관)
  migrate_andon_workflow.sql          # 안돈 워크플로: MD_LineSupervisor·PR_AndonDeptCall·PR_AndonCall.SupervisorName·공통코드 (순서 무관)
  migrate_lot_defect_rework.sql       # LOT 불량·재작업: PR_DefectDetail 컬럼·필터 유니크 인덱스·RWK 마스터 (순서 무관, -I 필수)
  migrate_img_core_lot.sql            # IMG 완제품↔사출 Core 1:1 필터 유니크 인덱스 UX_tbl_Lot_ImgParent (순서 무관, -I 필수)
  migrate_pnt_lot.sql                 # PNT 원천 LOT PR_PntLot + 구 SP_POP_PNT_* 17개 DROP (10-22, 순서 무관)
  seed_defect_first_article.sql       # INJ·IMG 초품 불량코드 INJ-D07·IMG-D07 (재실행 안전, 기본 원인은 있을 때만 연결)
  migrate_md_equipment_type_tonnage.sql # 설비 유형 공통코드 EQUIP_TYPE(INJ·WRAP·PNT) + 값 변환 + MD_Equipment.Tonnage (순서 무관)
  migrate_aps.sql                     # APS: MD_Item.BoxQty·MD_MoldItem.PartCavityCount 폐지·삭제 + MD_ApsLineStage·PP_ApsRun/PlanLine/RunWo + 공통코드 APS_SETTING/APS_COVER_TIER (migrate_mold_master 이후, -I 필수)
  migrate_pp_aps_screen.sql           # PP-APS 생산계획 화면 등록 + Admin 권한 (seed_admin_permissions 이후)
  migrate_demand_plan.sql             # 일별 구매계획(MM30011) 테이블·PP_ApsRun.IncludeDailyPlan·SW_DPSYNC* (migrate_aps 이후)
  migrate_po_sync.sql                 # SW_POSYNC* code groups
  migrate_po_sync_service_key.sql     # Web→API manual import key; created once per DB
  migrate_aps_plan_line_shift.sql     # APS 계획 행 교대별 수량 PP_ApsPlanLineShift (migrate_aps 이후)
  seed_aps_dev.sql                    #   → 개발용 APS 시드: PalletQty·LINE-INJ-01 2교대 패턴·MD_ApsLineStage·확정 수주·BOP 보강 (migrate_aps 이후)
  seed_demand_plan_dev.sql            #   → 개발용 일별계획 수집 소스 SEMS(URL 자리표시자)
  migrate_portal.sql                  # 외부 포탈: PROCESS/PORTAL 코드 · 구 출하 계획 화면 정리
  migrate_scm_item_vendor.sql         # SCM-003 발주품목-협력업체 매핑 SCM_ItemVendor
  migrate_scm_purchase_order_sequence.sql # SCM-001 발주번호 일별 채번
  migrate_scm_wh_purchase_order.sql   # WH_PurchaseOrder 납품장소·행 버전
  migrate_scm_portal_screens.sql      # SCM-001~003·PORTAL-001~005 화면 등록
  migrate_scm_portal_user_login.sql   # 외부 포탈 로그인 SCM_PortalVendorUser + SCM-004 화면, 포탈 RBAC 행 삭제
  migrate_scm_order_confirmation.sql # 수주 확인자·확인일
  migrate_scm_delivery.sql           # 납품서 헤더·품목
  migrate_scm_delivery_edit.sql      # 납품서 수정 이력·동시성
  migrate_scm_delivery_ship.sql      # 출하 확정
  migrate_scm_delivery_note.sql      # 딜리버리 노트 발행 스냅샷
  migrate_scm_delivery_note_batch.sql      # 복수 납품서 딜리버리 노트
  migrate_portal_legacy_cleanup.sql   # 구 외부 역할 ExternalCustomer·계정·포탈 RBAC 정리 (신규 DB 에서는 no-op)
  pda/PDA_SEED.sql                    # WH/FG seed, Web screens and role permissions
  seed_portal_dev.sql                 # optional development user; requires the vendor from PDA_SEED
  cleanup_legacy_sis_test.sql
  cleanup_cancelled_wo_slots.sql      # 취소 WO 가 남긴 라인 스케줄 슬롯 정리 (신규 DB 에서는 no-op)
  migrate_user_profile_backfill.sql   # 프로필 없는 내부 계정 보강(역할 있음 ACTIVE·없음 INACTIVE) — 시드가 만든 계정까지 보도록 맨 끝
  migrate_pda_bearer_session.sql      # PDA bearer hash + session snapshot (recycle-safe)
  migrate_user_status_mail_cleanup.sql  # 계정 메일 기능 폐지 — UNVERIFIED·PENDING → ACTIVE, 이메일 인증 정리, USER_STATUS 코드 삭제
  migrate_notification_push.sql      # 알림 채널 PUSH 전환 — EMAIL·SMS 는 남긴 채 사용 안 함, 규칙에 PUSH 추가(10-08)
  migrate_aspnetuserroles_pk.sql     # AspNetUserRoles 복합 PK(UserId, RoleId)+IX_RoleId — 스키마로 만든 DB 는 이미 있어 건너뜀(10-08)
  migrate_aspnetusertokens_pk.sql    # AspNetUserTokens 기본키에 UserId 포함(UserId, LoginProvider, Name) — 2FA 두 번째 사용자부터 키 중복 실패 수정, 스키마로 만든 DB 는 건너뜀(10-08)
  migrate_role_permission_orphan_cleanup.sql # 화면이 없는 SYS_RolePermission 행 정리 — SYS-003 화면 삭제가 권한 행을 남기던 것(10-09)
  migrate_sys_screen_permission_criteria.sql # 화면 기본 권한 SYS_Screen.PermissionCriteria(LidLabel 다음, varchar(10), _/X) + 역할 권한 3자리(R·E·A, _ 미부여, X 기능 없음) — create_sys_screen 이 다시 만든 표에도 넣는다(10-10)
  migrate_wh_inventory_backflush_placeholder.sql # BOM 자동 차감 자리표시 LOT(BF-*) 만 음수 재고 허용 — CK_WH_Inventory_Qty 완화(10-15)
  migrate_lsb_publish_removal.sql    # PP-LSB 발행 폐지 — PUBLISHED→DRAFT·PublishedAt/By 컬럼·발행 기록 테이블 삭제, SCHEDULE_STATUS 정리, PP-LSB 승인 기능 X(10-14)
  migrate_mnt_number_index.sql       # MNT 채번 범위 잠금용 일반 인덱스(고장·보전 WO·PM 계획 번호) — 스키마에 이미 있으면 건너뜀(10-13)
  migrate_mnt_failure_action_wo.sql  #고장 조치 이력의 작업지시(MNT_FailureAction.WorkOrderID) — 부분 완료한 원래 작업지시에서도 고장이 보이게(10-14)
  migrate_pp_lineoee_unique.sql      # PP-OEE 스냅샷 (LineID, PeriodDate, ShiftCode) 유니크 인덱스 — 스키마에 이미 있으면 건너뜀(10-14)
  migrate_pp_lineoee_fill.sql        # PP-OEE 자동 집계 프로시저 SP_PP_LineOEE_Fill(+Monitor) + 상태 로그·실적 인덱스 (에이전트 작업은 setup_job_pp_lineoee_fill.sql, 10-22)
  migrate_pp_linestatelog_fill.sql   # 라인 상태 분 단위 분류 프로시저 SP_PP_LineStateLog_Fill(+Monitor) + UX_PP_LineStateLog_Line_Minute + SYS_Config PP_LINE_RUN_GAP_* (작업은 setup_job_pp_linestatelog_fill.sql, 10-22)
  migrate_md_paint_fabric_lotno.sql  # MD_PaintFabric.LotNo varchar(24) → nvarchar(50) — 스키마에 이미 있으면 건너뜀(10-15)
  migrate_sys_auditlog_actiontype.sql # SYS_AuditLog.ActionType varchar(15) → varchar(20) — 스키마에 이미 있으면 건너뜀(10-15)
  migrate_by_columns_employee_no.sql # 사람 컬럼(...By) 33개 varchar(20)·값을 사번으로(SYS_AuditActorMap 에 원문 보존) — 스키마에 이미 있으면 건너뜀(10-15)
  migrate_mnt_status_code.sql        # 보전 작업지시 상태 공통코드 MNT_STATUS(발행·배정·진행 중·완료·종료), OPEN 정리(10-15)
  migrate_mnt_pm_checklist.sql       # PM 점검 단계 필요 부품 = 예비품(RequiredSparePartNo·int 수량), 작업지시 점검 항목 컬럼·인덱스, 공통코드 MNT_RESULT(10-15)
  migrate_mnt_failure_downtime.sql   # 고장 라인 정지 → 비가동(PP_LineDowntimeLog.FailureID), 쓰지 않는 MNT_WorkOrder.DowntimeID 삭제(10-16)
  migrate_mnt_pm_lead_days.sql       # SYS_Config MNT_PM_LEAD_DAYS — PM 도래·예정 기준 일수(10-16)
  migrate_mnt_oeelog_bigint.sql      # MNT_OEELog.OEELogID int → bigint(10-19)
  migrate_retire_packaging_uom.sql    # Retire packaging UOMs; remove obsolete BOX contents columns
  migrate_restore_sheet_uom.sql      # SH remains an individual quantity unit
  migrate_scm_delivery_progress_screen.sql # SCM-003 delivery receipts; retain grants on renumbered SCM-004/005
  # Fresh schema includes the actual-shipment customer-return contract.
  migrate_wh_fg_scm_column_lengths.sql # WH/FG/SCM keys and 20-byte actor codes
  migrate_fg_location_type.sql # FG-area locations use the explicit FG location type
  migrate_fg_shipment_actual.sql # Actual barcode shipments; no shipment seeds
  migrate_fg_shipment_no_foreign_keys.sql # No new shipment FKs; application SQL validates references
  migrate_fg_putaway_transactions.sql # FG put-away uses inventory transactions only
  migrate_md_sap_trans_flag.sql      # MD_Item·MD_Vendor·MD_Location.SAPTransFlag — SAP 전송 대상 여부(10-16)
  migrate_md_item_procure_type.sql   # MD_Item.ProcureType(SAPTransFlag 앞) + 공통코드 PROCURE_TYPE — 조달형태(10-20)
  migrate_md_item_allowance_pct.sql  # MD_Item.IssueAddPct·MrpAllowPct — 추가 불출율·MRP 여유율(%)(10-20)
  migrate_md_bomversion_sap_trans_flag.sql  # MD_BomVersion.SAPTransFlag — BOM 버전 SAP 전송 대상(10-20)
  migrate_md_customer_sap_flag.sql   # MD_Customer.SAPFlag — 고객사 SAP 수신 데이터 표시(10-21)
  migrate_md_sap_flag_rename.sql     # SAPTransFlag → SAPFlag(품목·협력업체·로케이션·고객사·BOM 버전) — SAP 에서 받는 데이터(10-21)
  migrate_md_uom_sap_flag.sql        # MD_Uom.SAPFlag — 단위 SAP 수신 데이터 표시(10-22)
  migrate_md_pallet.sql              # MD_Pallet·PALLET_USE, WH_Inventory.CaseNo→PalletNo, FG_Shipment.PalletNo, MD-034·035 화면(10-17) — FK 제거(no_foreign_keys) 뒤
  migrate_line_locations_shipping_orders.sql # Location minima, actual shipment LOT naming, shipment orders and DCR loads
  migrate_shipping_ui_line_defaults.sql # FG display order and registered production-line locations
  migrate_line_locations_eos.sql # Production line areas and locations belong to EOS
  migrate_md_location_parent.sql     # MD_Location.ParentLocationID·LocationLevel — 1차(SAP·MES)/상세 2단계 로케이션(10-22) — sap_flag_rename·line_locations 뒤
  migrate_fg_lot_defect_permission.sql # Separate Warehouse inventory defect permission
  migrate_wh_pda_test_permission.sql # Hidden Web receipt test in the existing Permission Matrix
  migrate_drop_md_mold_color.sql     # MD_MoldColor 삭제 — 금형 제품 색상은 MD_MoldItem.Color 직접 입력(10-16)
  migrate_audit_columns_last.sql     # 감사 컬럼을 맨 뒤 CreatedBy→CreatedTS→ModifiedBy→ModifiedTS 순으로 — 앞 마이그레이션이 뒤에 붙인 컬럼까지 정리하려고 항상 맨 끝(10-08)
  migrate_pop_stored_procedures.sql  # AMES.Pop 인라인 SQL -> dbo.SP_POP_* 저장 프로시저 145개(10-21) — 신 Pop/Web/Api 보다 먼저
)

echo "AMES DB rebuild — $DB @ container '$CONTAINER'"
echo "이 작업은 $DB 를 드롭하고 재생성합니다. 현재 데이터는 모두 사라집니다."
if [[ "$FORCE" -ne 1 ]]; then
  read -r -p "계속하려면 'yes' 입력: " ans
  [[ "$ans" == "yes" ]] || { echo "중단됨."; exit 0; }
fi

# 컨테이너 시작 + SQL Server 준비 대기
docker start "$CONTAINER" >/dev/null 2>&1 || true
echo "[0/3] SQL Server 준비 대기..."
until docker exec "$CONTAINER" "$SQLCMD" "${sql_base[@]}" -Q "SELECT 1" >/dev/null 2>&1; do
  echo "    waiting..."; sleep 2
done

# 1) DB 드롭 후 재생성 + ames_app 로그인/유저 보장
echo "[1/3] $DB 드롭 + 재생성..."
run_master "
IF DB_ID('$DB') IS NOT NULL
BEGIN
    ALTER DATABASE [$DB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DB];
END;
CREATE DATABASE [$DB] COLLATE Korean_Wansung_CI_AS;
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name='$APP_LOGIN')
    CREATE LOGIN [$APP_LOGIN] WITH PASSWORD='$APP_PASSWORD', CHECK_POLICY=OFF;"
run_master "
USE [$DB];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name='$APP_LOGIN')
    CREATE USER [$APP_LOGIN] FOR LOGIN [$APP_LOGIN];
ALTER ROLE db_owner ADD MEMBER [$APP_LOGIN];"

# 2) dist/ 를 컨테이너로 복사 (pda 하위폴더 포함)
#    docker cp 는 파일을 root 소유로 넣으므로, 삭제는 컨테이너 root(-u 0)로 해야
#    기본 사용자(mssql)의 "Permission denied" 를 피한다.
echo "[2/3] dist/*.sql 복사..."
docker exec -u 0 "$CONTAINER" rm -rf /tmp/dist
docker cp "$DIST_DIR/." "$CONTAINER:/tmp/dist/"

# 3) 스키마 → 시드 순서대로 적용
echo "[3/3] 스키마 + 시드 적용..."
for f in "${FILES[@]}"; do
  if [[ "$f" == "pda/PDA_SEED.sql" || "$f" == "seed_portal_dev.sql" ]]; then
    if [[ "${AMES_DEVELOPMENT_SEEDS:-0}" != "1" ]]; then
      echo "  -> $f skipped (development seed opt-in required)"
      continue
    fi
    printf '%s\n' "EXEC sys.sp_set_session_context @key=N'AMES_DEVELOPMENT_SEED', @value=1;" "GO" ":r /tmp/dist/$f" |
      docker exec -i "$CONTAINER" "$SQLCMD" "${sql_base[@]}" -d "$DB"
    continue
  fi
  echo "  -> $f"
  run_file "$f"
done

# 4) SQL Server 에이전트 작업 4개 — "[AMES] Factory Calendar Fill"(매월 1일, 공장 달력 3개월 채우기) ·
#    "[AMES] Line State Fill"(1분마다 최근 2시간 PP_LineStateLog 분 단위 상태 분류) · "[AMES] Line State Recalc"(매일 06:00 이틀치 재분류 + OEE 재집계) ·
#    "[AMES] Line OEE Fill"(5분마다 PP-OEE 라인 OEE 스냅샷).
#    msdb 는 DB 를 지워도 남으므로 매번 다시 등록한다(재실행 안전).
#    컨테이너에서 작업이 실제로 돌려면 MSSQL_AGENT_ENABLED=true 로 만든 컨테이너여야 한다. 등록 실패는 재구축을 막지 않는다.
echo "[4/4] 에이전트 작업 등록 (setup_job_factory_calendar_fill.sql · setup_job_pp_linestatelog_fill.sql · setup_job_pp_linestatelog_recalc.sql · setup_job_pp_lineoee_fill.sql)..."
run_file setup_job_factory_calendar_fill.sql || echo "    ⚠️ 작업 등록 실패 — 컨테이너 에이전트 설정을 확인하고 setup_job_factory_calendar_fill.sql 을 다시 실행하세요"
run_file setup_job_pp_linestatelog_fill.sql  || echo "    ⚠️ 작업 등록 실패 — 컨테이너 에이전트 설정을 확인하고 setup_job_pp_linestatelog_fill.sql 을 다시 실행하세요"
run_file setup_job_pp_linestatelog_recalc.sql || echo "    ⚠️ 작업 등록 실패 — 컨테이너 에이전트 설정을 확인하고 setup_job_pp_linestatelog_recalc.sql 을 다시 실행하세요"
run_file setup_job_pp_lineoee_fill.sql       || echo "    ⚠️ 작업 등록 실패 — 컨테이너 에이전트 설정을 확인하고 setup_job_pp_lineoee_fill.sql 을 다시 실행하세요"

echo ""
echo "✅ $DB rebuild 완료. 검증:"
docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -d "$DB" -Q "
SELECT
  (SELECT COUNT(*) FROM sys.tables)         AS tables,
  (SELECT COUNT(*) FROM SYS_Screen)         AS screens,
  (SELECT COUNT(*) FROM SYS_RolePermission) AS perms,
  (SELECT COUNT(*) FROM MD_Item)            AS items;"
