# AMES.Web MNT·RPT·SYS 화면

> `CLAUDE.md` 에서 분리한 상세 문서입니다(원문 그대로, 11-18 토큰 절감 정리). 필요한 주제만 `grep -rn 키워드 docs/claude/` 로 찾아 해당 줄 범위만 읽으세요.

#### MNT (설비보전) — 10화면
`Dashboard` / `EquipmentCard` / `WorkOrder` / `EquipPmSchedule`(MNT-005, PM_CLASS=EQUIP) / `MaintPmSchedule`(MNT-010, PM_CLASS=MAINT — 둘 다 `PmScheduleBoard` 공유, 테이블 `MNT_PMSchedule` 공용) / `Downtime`
`Failure` / `Mold` / `OeeAnalysis` / `SpareParts`

#### RPT (보고서) — 10화면
`DailyProduction` / `DailyShipment` / `DefectPareto` / `EquipmentOee`
`Inventory` / `MonthlyKpi` / `OnTime` / `ScheduleAdherence`
`ReportBuilder` / `ReportCenter`

#### SYS (시스템) — 8화면
`Users` / `Rbac` / `Audit` / `Health` / `Notifications`
`Config` / `Interfaces` / `Calendar`

**SYS-001 사용자 관리는 탭 3개다**(11-10 사용자 요청): 「웹 사용자」(종전 화면) · 「현장 작업자」(MD-032 `Md/Fd/Workers.razor` 를 `Embedded="true"` 로 그대로 넣음) · 「외부 사용자」(`Scm/PortalUsers.razor`, 같은 방식). 두 탭은 그 화면의 읽기 권한(`md/fd/workers`·`scm/portal-users`)이 있을 때만 보이고 수정·등록도 그 화면의 권한을 따른다. 원래 메뉴(MD-032·외부 사용자 관리)는 그대로 있다 — `Embedded` 는 화면 제목·머리글만 빼고, 외부 사용자 화면은 권한이 없을 때 `/unauthorized` 로 보내지 않는다.

---
