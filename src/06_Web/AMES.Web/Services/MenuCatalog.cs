using AMES.Data.Repositories;

namespace AMES.Web.Services;

/// <summary>
/// 웹 메뉴 카탈로그 — SYS_Screen(ModuleCode='WEB') 을 대분류(Section)·화면(Item)으로 정리한다.
/// 좌측 메뉴(NavMenu)와 홈 사이트맵(Home)이 같은 데이터를 쓰기 위해 NavMenu 에서 분리했다.
/// 권한 판정은 여기서 하지 않는다 — 호출측이 PermissionService 로 Href 별 가시성을 정한다.
/// </summary>
public sealed class MenuCatalog
{
    private readonly SysRepository _sys;
    private readonly MasterDataRepository _md;
    private bool _loaded;

    public MenuCatalog(SysRepository sys, MasterDataRepository md) { _sys = sys; _md = md; }

    public record Section(string Key, string Icon, string Ko, string En, string Es);
    public record Item(string Section, string Href, string Lid, string Ko, string En, string? Sub = null);
    public record MdGroup(string Key, string Letter, string Ko, string En, string Es, List<Item> Items);

    public List<Item> Items { get; private set; } = new();
    /// <summary>DB 로딩 실패 시 정적 목록 사용 → 권한 필터 미적용</summary>
    public bool UsingFallback { get; private set; }
    /// <summary>MD 서브그룹 정의 (SUBPROCESS 공통코드, DB 로딩)</summary>
    public List<MasterDataRepository.CodeItemRow> SubprocessItems { get; private set; } = new();

    public static readonly Section[] Sections =
    {
        new("pp",  "📅", "생산 계획",   "Production Planning", "Planificación"),
        new("mnt", "🔧", "설비 보전",   "Maintenance",         "Mantenimiento"),
        new("rpt", "📊", "보고서",      "Reports",             "Informes"),
        new("wh",  "📦", "창고 관리",     "Warehouse",          "Almacén"),
        new("fg",  "🚚", "완제품 관리",   "Finished Goods",     "Producto Terminado"),
        new("md",  "🗂️", "마스터 데이터", "Master Data",         "Datos Maestros"),
        new("sys", "⚙️", "시스템",      "System",              "Sistema"),
    };

    // 메뉴 항목은 SYS_Screen(ModuleCode='WEB')에서 동적으로 로딩된다.
    // DB 조회 실패/빈 결과 시 아래 정적 목록으로 폴백(권한 필터 미적용, 서브그룹 미표시).
    List<Item> _items = new();

    static readonly Item[] FallbackItems =
    {
        // -- WH --
        new("wh", "wh/inventory",       "WH-006", "재고 조회",   "Inventory Search"),
        new("wh", "wh/location-map",    "WH-003", "로케이션 맵", "Location Map"),
        new("wh", "wh/log-history",     "WH-004", "재고 이력",   "Inventory History"),
        new("wh", "wh/picking-orders",  "WH-002", "피킹 오더",   "Picking Orders"),

        // -- FG --
        new("fg", "fg/inventory",        "FG-001", "재고 조회",   "Inventory Search"),
        new("fg", "fg/location-map",     "FG-002", "로케이션 맵", "Location Map"),
        new("fg", "fg/customer-returns", "FG-003", "고객사 리턴", "Customer Returns"),
        new("fg", "fg/shipments",        "FG-004", "출하 목록",   "Shipments"),
        new("fg", "fg/history",          "FG-005", "작업 이력",   "History"),

        // ── PP ──
        new("pp", "pp/forecast",         "PP-001", "수요 예측",        "Forecast"),
        new("pp", "pp/supply-plan-import", "PP-002", "공급계획 가져오기",  "Supply Plan Import"),
        new("pp", "pp/plan-confirm",     "PP-003", "계획 확정",        "Plan Confirm"),
        new("pp", "pp/work-order",       "PP-004", "작업 지시",        "Work Order"),
        new("pp", "pp/mrp",              "PP-005", "MRP",             "MRP"),
        new("pp", "pp/purchase-req",     "PP-006", "구매 요청",        "Purchase Req"),
        new("pp", "pp/calendar",         "CAL",    "캘린더",          "Calendar"),
        new("pp", "pp/line-schedule",    "LSB",    "라인 일정",        "Line Schedule"),
        new("pp", "pp/oee",              "OEE",    "라인 OEE",        "Line OEE"),
        new("pp", "pp/downtime",         "DTL",    "비가동 이력",      "Downtime Log"),
        new("pp", "pp/downtime-monitor", "ODM",    "비가동 모니터",    "Downtime Monitor"),
        new("pp", "pp/delivery",         "OTD",    "납기 준수율",      "On-Time Delivery"),

        // ── MNT ──
        new("mnt", "mnt/equipment-card", "MNT-001", "설비 카드",   "Equipment Card"),
        new("mnt", "mnt/failure",        "MNT-002", "고장 등록",   "Failure Register"),
        new("mnt", "mnt/oee-analysis",   "MNT-003", "OEE 분석",   "OEE Analysis"),
        new("mnt", "mnt/mold",           "MNT-004", "금형 관리",   "Mold Management"),
        new("mnt", "mnt/pm-schedule",    "MNT-005", "설비 PM 일정", "Equipment PM Schedule"),
        new("mnt", "mnt/downtime",       "MNT-006", "비가동 이력", "Downtime Log"),
        new("mnt", "mnt/work-order",     "MNT-007", "작업 지시",   "Work Order"),
        new("mnt", "mnt/spare-parts",    "MNT-008", "예비 부품",   "Spare Parts"),
        new("mnt", "mnt/dashboard",      "MNT-009", "대시보드",    "Dashboard"),
        new("mnt", "mnt/maint-pm-schedule", "MNT-010", "보전 PM 일정", "Maintenance PM Schedule"),

        // ── RPT ──
        new("rpt", "rpt/daily-production",   "RPT-001", "일별 생산 실적", "Daily Production"),
        new("rpt", "rpt/defect-pareto",      "RPT-002", "불량 파레토",    "Defect Pareto"),
        new("rpt", "rpt/daily-shipment",     "RPT-003", "일별 출하 현황", "Daily Shipment"),
        new("rpt", "rpt/on-time",            "RPT-004", "납기 준수율",    "On-Time Delivery"),
        new("rpt", "rpt/inventory",          "RPT-005", "재고 현황",      "Inventory Status"),
        new("rpt", "rpt/equipment-oee",      "RPT-006", "설비 OEE",      "Equipment OEE"),
        new("rpt", "rpt/monthly-kpi",        "RPT-007", "월간 KPI",      "Monthly KPI"),
        new("rpt", "rpt/schedule-adherence", "RPT-008", "계획 준수율",    "Schedule Adherence"),
        new("rpt", "rpt/report-center",      "RPT-009", "리포트 센터",    "Report Center"),
        new("rpt", "rpt/report-builder",     "RPT-010", "리포트 빌더",    "Report Builder"),

        // ── MD (Rp/Fd/Re/Rm/Ql 서브폴더 이동 경로 반영) ──
        new("md", "md/rp/line",                "MD-001", "공장/라인 기준정보 관리", "Factory / Line Master"),
        new("md", "md/rp/station",             "MD-002", "작업 위치 관리",         "Station Master"),
        new("md", "md/fd/items",               "MD-003", "제품 기준정보 관리",     "Product Item Master"),
        new("md", "md/fd/bom",                 "MD-004", "BOM 관리",             "BOM Management"),
        new("md", "md/rp/bop",                 "MD-005", "BOP 관리",             "BOP Management"),
        new("md", "md/rp/work-center",         "MD-006", "Work Center 관리",     "Work Center Management"),
        new("md", "md/re/mold",                "MD-007", "금형 기준정보 관리",     "Mold Master"),
        new("md", "md/rm/paint-fabric",        "MD-008", "원부자재 기준정보 관리",  "Paint & Fabric Master"),
        new("md", "md/re/vendor",              "MD-009", "공급업체 기준정보 관리",  "Vendor Master"),
        new("md", "md/ql/customer",            "MD-010", "고객사 기준정보 관리",    "Customer Master"),
        new("md", "md/ql/shipment-dest",       "MD-011", "출하처 기준정보 관리",    "Shipment Destination Master"),
        new("md", "md/ql/defect-code",         "MD-012", "불량유형 기준정보 관리",  "Defect Code Master"),
        new("md", "md/ql/defect-cause",        "MD-013", "불량원인 기준정보 관리",  "Defect Cause Master"),
        new("md", "md/re/equipment",           "MD-014", "설비 기준정보 관리",     "Equipment Master"),
        new("md", "md/re/oven",                "MD-015", "건조로 기준정보 관리",    "Oven Master"),
        new("md", "md/re/jig",                 "MD-016", "지그 기준정보 관리",     "Jig Master"),
        new("md", "md/ql/inspection-standard", "MD-017", "검사기준 기준정보 관리",  "Inspection Standard Master"),
        new("md", "md/fd/location",            "MD-018", "창고/로케이션 기준정보 관리", "Warehouse Location Master"),
        new("md", "md/fd/uom",                 "MD-019", "단위 관리",             "UOM Master"),
        new("md", "md/rm/rfid-tag",            "MD-020", "RFID 태그 관리",        "RFID Tag Master"),
        new("md", "md/rm/ral-color",           "MD-021", "RAL 색상 관리",         "RAL Color Master"),
        new("md", "md/rm/rfid-reader",         "MD-022", "RFID 리더 관리",        "RFID Reader Master"),
        new("md", "md/ql/packaging-spec",      "MD-023", "포장 사양 관리",         "Packaging Spec Master"),
        new("md", "md/ql/label-template",      "MD-024", "라벨 템플릿 관리",       "Label Template Master"),
        new("md", "md/re/reason-code",         "MD-025", "사유 코드 관리",         "Reason Code Master"),
        new("md", "md/re/spare-part",          "MD-026", "예비품 마스터",          "Spare Part Master"),
        new("md", "md/rp/pm-template",         "MD-027", "PM 템플릿 관리",        "PM Template Master"),
        new("md", "md/rp/line-time-pattern",   "MD-028", "라인 시간 패턴 관리",     "Line Time Pattern Master"),
        new("md", "md/ql/recipe",              "MD-029", "레시피 관리",           "Recipe Master"),
        new("md", "md/ql/common-code",         "MD-030", "코드 기준정보 관리",     "Common Code Master"),
        new("md", "md/fd/workers",             "MD-032", "현장 작업자 관리",       "Worker Master"),
        new("md", "md/fd/line-supervisors",    "MD-033", "라인 책임자 관리",    "Line Supervisor Master"),

        // ── SYS ──
        new("sys", "sys/users",         "SYS-001", "사용자 관리",         "User Management"),
        new("sys", "sys/roles",         "SYS-002", "역할 관리",           "Role Management"),
        new("sys", "sys/screens",       "SYS-003", "화면 관리",           "Screen Management"),
        new("sys", "sys/rbac",          "SYS-004", "역할/권한 관리 (RBAC)", "Role & Permission (RBAC)"),
        new("sys", "sys/calendar",      "SYS-005", "공장 캘린더",         "Factory Calendar"),
        new("sys", "sys/interfaces",    "SYS-006", "인터페이스 모니터",     "Interface Monitor"),
        new("sys", "sys/audit",         "SYS-007", "감사 로그",           "Audit Log"),
        new("sys", "sys/notifications", "SYS-008", "알림 관리",           "Notification Management"),
        new("sys", "sys/config",        "SYS-009", "시스템 설정",         "System Configuration"),
        new("sys", "sys/health",        "SYS-010", "시스템 상태",         "System Health"),
    };

    static readonly HashSet<string> HiddenItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "wh/locations",
    };

    public static bool IsHidden(string href) => HiddenItems.Contains(href.TrimStart('/'));

    public static bool IsRemovedMenu(string href)
    {
        var path = href.Trim('/');
        return path.Equals("wh/inventory-setting", StringComparison.OrdinalIgnoreCase)
            || path.Equals("fg/locations", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>SYS-003 화면 마스터 편집 뒤 다시 읽기(ScreenCatalogNotifier 수신 측에서 호출)</summary>
    public void Reload() { _loaded = false; Load(); }

    public void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var rows = _sys.ListScreens("WEB")
                .Where(s => s.IsVisible && !string.IsNullOrEmpty(s.ProcessCode) && !string.IsNullOrEmpty(s.HRef))
                .Where(s => !IsRemovedMenu(s.HRef!))
                .OrderBy(s => s.SortOrder ?? 999).ThenBy(s => s.ScreenCode)
                .Select(s => new Item(
                    s.ProcessCode!.ToLowerInvariant(),
                    s.HRef!.TrimStart('/'),
                    s.LidLabel ?? s.ScreenCode,
                    s.ScreenName,
                    s.ScreenNameEn ?? s.ScreenName,
                    s.SubProcessCode))
                .ToList();

            if (rows.Count > 0)
            {
                Items = rows;
                UsingFallback = false;
                try { SubprocessItems = _md.ListCodeItems("SUBPROCESS"); }
                catch { /* 서브그룹 정의 실패 시 MD 플랫 표시 */ }
                return;
            }
        }
        catch { /* fall through to fallback */ }

        Items = FallbackItems.ToList();
        UsingFallback = true;
    }

    /// <summary>대분류의 화면 목록(권한 필터 없음, 숨김 항목 제외). SYS_Screen.SortOrder 순.</summary>
    public List<Item> SectionItems(string sectionKey) =>
        Items.Where(i => i.Section == sectionKey && !IsHidden(i.Href)).ToList();

    public List<MdGroup> GroupedMd(List<Item> mdItems)
    {
        // 서브그룹 노출 순서를 SYS_Screen.SortOrder(데이터) 기준으로 결정.
        // mdItems 는 SortOrder 로 이미 정렬되어 있으므로 최초 등장 순 = 데이터 순서.
        // SYS_Screen.SubProcessCode 와 공통코드 SUBPROCESS.CodeValue 는 대소문자가 다를 수 있다
        // (create_sys_screen.sql 'Fd' vs seed_md_code.sql 'FD' — 09-22 개발 DB 재구축에서 서브메뉴가 통째로 사라진 원인)
        var order = mdItems
            .Where(x => !string.IsNullOrEmpty(x.Sub) && SubprocessItems.Any(s => SubEq(s.CodeValue, x.Sub)))
            .Select(x => x.Sub!.ToUpperInvariant())
            .Distinct()
            .ToList();
        return order
            .Select((code, i) =>
            {
                var g = SubprocessItems.First(s => SubEq(s.CodeValue, code));
                return new MdGroup(
                    Key:    code,
                    Letter: ((char)('A' + i)).ToString(),
                    Ko:     g.CodeName   ?? g.CodeValue ?? "",
                    En:     g.CodeNameEn ?? g.CodeName ?? g.CodeValue ?? "",
                    Es:     SubEs.TryGetValue(code, out var subEs) ? subEs : (g.CodeNameEn ?? g.CodeValue ?? ""),
                    Items:  mdItems.Where(x => SubEq(x.Sub, code)).ToList());
            })
            .Where(g => g.Items.Count > 0)
            .ToList();
    }

    public List<Item> UngroupedMd(List<Item> mdItems)
    {
        var keys = SubprocessItems.Select(g => g.CodeValue ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return mdItems.Where(x => string.IsNullOrEmpty(x.Sub) || !keys.Contains(x.Sub)).ToList();
    }

    static bool SubEq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // 화면/대분류의 스페인어 표기 (DB엔 KO/EN만 존재 → 인라인 공급, Href 키)
    static readonly Dictionary<string, string> EsByHref = new()
    {
        // WH
        ["wh/location-map"] = "Mapa de Ubicaciones",
        ["wh/picking-orders"] = "Órdenes de Picking",
        ["wh/log-history"] = "Historial de Inventario",
        ["fg/location-map"] = "Mapa de Ubicaciones",
        ["fg/customer-returns"] = "Devoluciones de Clientes",
        ["fg/shipments"] = "Embarques",
        ["fg/history"] = "Historial",
        // PP
        ["pp/forecast"] = "Pronóstico",
        ["pp/supply-plan-import"] = "Importar Plan de Suministro",
        ["pp/plan-confirm"] = "Confirmar Plan",
        ["pp/work-order"] = "Orden de Trabajo",
        ["pp/mrp"] = "MRP",
        ["pp/purchase-req"] = "Solicitud de Compra",
        ["pp/calendar"] = "Calendario",
        ["pp/line-schedule"] = "Programa de Línea",
        ["pp/oee"] = "OEE de Línea",
        ["pp/downtime"] = "Registro de Paradas",
        ["pp/downtime-monitor"] = "Monitor de Paradas",
        ["pp/delivery"] = "Entrega a Tiempo",
        // MNT
        ["mnt/equipment-card"] = "Ficha de Equipo",
        ["mnt/failure"] = "Registro de Fallos",
        ["mnt/oee-analysis"] = "Análisis OEE",
        ["mnt/mold"] = "Gestión de Moldes",
        ["mnt/pm-schedule"] = "Plan PM de Equipos",
        ["mnt/maint-pm-schedule"] = "Plan PM de Mantenimiento",
        ["mnt/downtime"] = "Registro de Paradas",
        ["mnt/work-order"] = "Orden de Trabajo",
        ["mnt/spare-parts"] = "Repuestos",
        ["mnt/dashboard"] = "Panel",
        // RPT
        ["rpt/daily-production"] = "Producción Diaria",
        ["rpt/defect-pareto"] = "Pareto de Defectos",
        ["rpt/daily-shipment"] = "Envíos Diarios",
        ["rpt/on-time"] = "Entrega a Tiempo",
        ["rpt/inventory"] = "Estado de Inventario",
        ["rpt/equipment-oee"] = "OEE de Equipo",
        ["rpt/monthly-kpi"] = "KPI Mensual",
        ["rpt/schedule-adherence"] = "Cumplimiento del Programa",
        ["rpt/report-center"] = "Centro de Informes",
        ["rpt/report-builder"] = "Constructor de Informes",
        // MD
        ["md/rp/line"] = "Maestro de Fábrica / Línea",
        ["md/rp/station"] = "Maestro de Estaciones",
        ["md/fd/items"] = "Maestro de Ítems de Producto",
        ["md/fd/bom"] = "Gestión de BOM",
        ["md/rp/bop"] = "Gestión de BOP",
        ["md/rp/work-center"] = "Gestión de Centros de Trabajo",
        ["md/re/mold"] = "Maestro de Moldes",
        ["md/rm/paint-fabric"] = "Maestro de Pintura y Tela",
        ["md/re/vendor"] = "Maestro de Proveedores",
        ["md/ql/customer"] = "Maestro de Clientes",
        ["md/ql/shipment-dest"] = "Maestro de Destinos de Envío",
        ["md/ql/defect-code"] = "Maestro de Códigos de Defecto",
        ["md/ql/defect-cause"] = "Maestro de Causas de Defecto",
        ["md/re/equipment"] = "Maestro de Equipos",
        ["md/re/oven"] = "Maestro de Hornos",
        ["md/re/jig"] = "Maestro de Jigs",
        ["md/ql/inspection-standard"] = "Maestro de Estándares de Inspección",
        ["md/fd/location"] = "Maestro de Ubicaciones de Almacén",
        ["md/fd/uom"] = "Maestro de UOM",
        ["md/fd/workers"] = "Maestro de operarios",
        ["md/fd/line-supervisors"] = "Maestro de supervisores de línea",
        ["md/rm/rfid-tag"] = "Maestro de Etiquetas RFID",
        ["md/rm/ral-color"] = "Maestro de Colores RAL",
        ["md/rm/rfid-reader"] = "Maestro de Lectores RFID",
        ["md/ql/packaging-spec"] = "Maestro de Especificaciones de Embalaje",
        ["md/ql/label-template"] = "Maestro de Plantillas de Etiqueta",
        ["md/re/reason-code"] = "Maestro de Códigos de Motivo",
        ["md/re/spare-part"] = "Maestro de Repuestos",
        ["md/rp/pm-template"] = "Maestro de Plantillas PM",
        ["md/rp/line-time-pattern"] = "Maestro de Patrones de Tiempo de Línea",
        ["md/ql/recipe"] = "Maestro de Recetas",
        ["md/ql/common-code"] = "Maestro de Códigos Comunes",
        // SYS
        ["sys/users"] = "Gestión de Usuarios",
        ["sys/roles"] = "Gestión de Roles",
        ["sys/screens"] = "Gestión de Pantallas",
        ["sys/rbac"] = "Roles y Permisos (RBAC)",
        ["sys/calendar"] = "Calendario de Fábrica",
        ["sys/interfaces"] = "Monitor de Interfaces",
        ["sys/audit"] = "Registro de Auditoría",
        ["sys/notifications"] = "Gestión de Notificaciones",
        ["sys/config"] = "Configuración del Sistema",
        ["sys/health"] = "Estado del Sistema",
    };

    // MD 서브그룹(SUBPROCESS 공통코드) 스페인어 표기 (CodeValue 키)
    static readonly Dictionary<string, string> SubEs = new()
    {
        ["FD"] = "Fundamentos",
        ["RP"] = "Ruta y Producción",
        ["RE"] = "Recursos y Equipos",
        ["RM"] = "Materias Primas y Pintura",
        ["QL"] = "Calidad y Logística",
    };

    public static string? EsFor(string href) => EsByHref.TryGetValue(href, out var v) ? v : null;

    /// <summary>언어별 화면 라벨 (ko / en / es — es 미제공은 en 폴백)</summary>
    public static string Label(Item item, string lang) => lang switch
    {
        "en" => item.En,
        "es" => EsFor(item.Href) ?? item.En,
        _    => item.Ko,
    };
    public static string Label(Section sec, string lang) => lang switch { "en" => sec.En, "es" => sec.Es, _ => sec.Ko };
}
