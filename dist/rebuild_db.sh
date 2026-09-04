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
  AMES_Schema.sql                     # 전체 구조 (158 dbo 테이블 + 전 컬럼)
  seed_user_code_groups.sql           # 공통 코드 그룹
  create_sys_screen.sql               # SYS_Screen 레지스트리 (화면 목록)
  reseed_menu.sql                     #   → 메뉴/HRef 정본 재시드
  seed_admin_permissions.sql          #   → Admin RBAC(REA) + admin@ames.local 계정
  seed_md_code.sql                    # 마스터 공통코드
  seed_md_routing_step.sql            # 라우팅 템플릿(A/B) 시드 (테이블은 AMES_Schema.sql)
  reseed_md_item_partmaster.sql       # 품목(파트마스터)
  migrate_inj_agent.sql               # 사출: MD_Mold 4종 + 사출조건 시드
  migrate_mold_master.sql             # 금형: 매핑/색상/라인 시드 + FK 3종
  migrate_routing_step.sql            # 라우팅: MD_RoutingStep 생성 + A/B 시퀀스 시드 (seed_admin_permissions 이후 — Admin 권한 필요)
  migrate_wo_step_line.sql            # WO 공정 단계: PP_WorkOrderRouting CompletedQty·TerminalLock·인덱스 + 백필 (migrate_routing_step 이후)
  migrate_wh_inventory_setting.sql    # 창고 재고 설정 시드
  migrate_wh_location_master_audit.sql # Warehouse Location Master audit + Admin role backfill
  migrate_wh_use_md_location_master.sql # 중복 WH Location Master 제거 (MD-018 사용)
  pda/migrate_pda_wh_schedule.sql     # PDA 창고: 입고예정/출고예정/조정
  pda/migrate_pda_wh_inbound.sql
  pda/migrate_pda_wh_release.sql
  cleanup_legacy_sis_test.sql
  cleanup_cancelled_wo_slots.sql      # 취소 WO 가 남긴 라인 스케줄 슬롯 정리 (신규 DB 에서는 no-op)
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
CREATE DATABASE [$DB];
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
  echo "  -> $f"
  run_file "$f"
done

echo ""
echo "✅ $DB rebuild 완료. 검증:"
docker exec "$CONTAINER" "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -d "$DB" -Q "
SELECT
  (SELECT COUNT(*) FROM sys.tables)         AS tables,
  (SELECT COUNT(*) FROM SYS_Screen)         AS screens,
  (SELECT COUNT(*) FROM SYS_RolePermission) AS perms,
  (SELECT COUNT(*) FROM MD_Item)            AS items;"
