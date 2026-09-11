namespace AMES.Pda.Components;

public static class FgDetailedScenarioCatalog
{
    private sealed record Spec(int Step, string Title, string Guide);

    private static PptScenarioPanel.Step[] Build(string screen, PptScenarioPanel.Step[] source, params Spec[] specs)
        => specs.Select((spec, index) => new PptScenarioPanel.Step(spec.Title, spec.Guide, source[spec.Step - 1].Values)
        {
            SourceStep = spec.Step,
            TestCaseId = $"{screen}-TC-{index + 1:000}"
        }).ToArray();

    public static PptScenarioPanel.Step[] Qc(PptScenarioPanel.Step[] source) => Build("FG001", source,
        new(1,"대기 재고 목록","QC 완료 후 아직 적재되지 않은 제품이 목록에 표시되는지 확인합니다."), new(1,"대기 현황 집계","전체 건수와 경과일 기준 건수가 목록과 일치하는지 확인합니다."),
        new(1,"대기 목록 항목","LOT, Part No, Part Name, Qty, Unit이 표시되는지 확인합니다."), new(2,"경과일 색상","1일, 5일, 10일 초과 건이 각각 노란색, 주황색, 빨간색으로 표시되는지 확인합니다."),
        new(2,"오래된 순 정렬","QC 완료 시간이 오래된 제품부터 표시되는지 확인합니다."), new(2,"새로고침","REFRESH 실행 시 최신 목록과 집계가 다시 조회되는지 확인합니다."));

    public static PptScenarioPanel.Step[] PutAway(PptScenarioPanel.Step[] source) => Build("FG002", source,
        new(1,"FG LOT 스캔","QC 완료된 정상 FG LOT을 스캔하면 Put-Away가 시작되는지 확인합니다."), new(1,"초기 화면 구성","Barcode 입력부와 CLEAR, CONFIRM 버튼이 표시되는지 확인합니다."),
        new(1,"확정 버튼 비활성화","필수 바코드를 스캔하기 전에는 CONFIRM이 비활성화되는지 확인합니다."), new(2,"LOT 정보","스캔한 LOT의 Part, Qty, QC Passed Date가 정확히 표시되는지 확인합니다."),
        new(2,"보관 방식 선택","LOT 확인 후 Storage 선택이 필수인지 확인합니다."), new(3,"보관 방식 항목","Box, Pallet, Rack, Location Only 항목이 모두 표시되는지 확인합니다."),
        new(3,"보관 단위 바코드","Box, Pallet, Rack 선택 시 해당 보관 단위 바코드 스캔이 필요한지 확인합니다."), new(3,"다음 스캔 안내","선택한 보관 방식에 따라 Next Scan 안내가 변경되는지 확인합니다."),
        new(4,"Location 스캔 단계","정상 보관 단위 스캔 후 Location Barcode 단계로 이동하는지 확인합니다."), new(4,"자동 이동 및 강조","화면이 Location 입력부로 자동 이동하고 입력부가 강조되는지 확인합니다."),
        new(5,"Location 상세 정보","Zone, Bay, Slot, Current, Free 정보가 표시되는지 확인합니다."), new(5,"적재 확정","CONFIRM 실행 시 보관 방식과 Location이 저장되는지 확인합니다."),
        new(5,"입력 초기화","CLEAR 실행 시 완료되지 않은 작업 내용이 초기화되는지 확인합니다."));

    public static PptScenarioPanel.Step[] Inventory(PptScenarioPanel.Step[] source) => Build("FG003", source,
        new(1,"재고 Location 목록","FG 재고가 있는 Location만 목록에 표시되는지 확인합니다."), new(1,"Location 수량","Location과 Qty가 실제 재고와 일치하는지 확인합니다."),
        new(1,"새로고침 및 초기화","REFRESH로 다시 조회되고 CLEAR로 검색어가 제거되는지 확인합니다."), new(2,"Location별 품목","Location 선택 시 해당 위치의 품목 목록이 표시되는지 확인합니다."),
        new(2,"품목 목록 항목","Part No, Part Name, Qty가 읽기 쉽게 표시되는지 확인합니다."), new(2,"품목 상세 열기","품목 선택 시 LOT 상세 화면이 열리는지 확인합니다."),
        new(3,"Location별 LOT 목록","선택한 Location과 품목에 속한 모든 LOT이 표시되는지 확인합니다."), new(3,"LOT 목록 항목","LOT, Qty, Unit, Location이 표시되는지 확인합니다."),
        new(4,"품목 기준 재고 조회","By Part 모드에서 여러 Location의 FG 재고가 품목 기준으로 표시되는지 확인합니다."), new(4,"품목 및 Location 정보","Part, Qty, Location이 표시되는지 확인합니다."),
        new(4,"동일 재고 합산","동일 Part와 Location의 재고가 하나의 행으로 합산되는지 확인합니다."), new(5,"품목별 LOT 열기","품목 선택 시 해당 품목의 LOT 목록이 열리는지 확인합니다."),
        new(5,"품목별 LOT 항목","각 LOT 행에 Qty, Unit, Location이 표시되는지 확인합니다."),
        new(6,"조회 오류 표시","API 또는 DB 연결 오류를 빈 재고 목록으로 처리하지 않고 오류 안내를 표시하는지 확인합니다."));

    public static PptScenarioPanel.Step[] Picking(PptScenarioPanel.Step[] source) => Build("FG004", source,
        new(1,"출고전표 스캔","정상 출고전표를 스캔하면 Customer, Destination, Ship Date가 표시되는지 확인합니다."), new(1,"요구 수량","각 품목에 Required와 Scanned 수량이 표시되는지 확인합니다."),
        new(2,"복수 LOT 수량 합산","같은 품목의 여러 LOT을 스캔하면 수량이 누적되는지 확인합니다."), new(2,"부분 스캔 상태","요구 수량을 충족하지 못한 품목에 PARTIAL과 진행 수량이 표시되는지 확인합니다."),
        new(2,"완료 버튼 비활성화","완료되지 않은 품목이 있으면 COMPLETE가 비활성화되는지 확인합니다."), new(3,"품목 스캔 완료","요구 수량을 충족한 품목이 초록색 CHECKED 상태로 변경되는지 확인합니다."),
        new(3,"품목별 진행 수량","각 품목의 스캔 수량이 서로 독립적으로 집계되는지 확인합니다."), new(3,"미완료 품목 상태","요구 수량을 충족하지 못한 품목은 PARTIAL 상태로 유지되는지 확인합니다."),
        new(4,"전체 품목 완료","모든 품목의 요구 수량을 충족하면 전체 행이 초록색으로 변경되는지 확인합니다."), new(4,"완료 버튼 활성화","모든 LOT 스캔 완료 후 COMPLETE가 활성화되는지 확인합니다."),
        new(4,"취소 시 스캔 초기화","COMPLETE 전에 CANCEL을 누르면 스캔 진행 내용이 저장되지 않고 초기화되는지 확인합니다."), new(5,"서버 FIFO 검증","서버가 현재 출고 가능한 가장 오래된 LOT을 우선하도록 검증하는지 확인합니다."),
        new(5,"FIFO 오류 정보","FIFO 오류에 먼저 스캔해야 할 LOT과 Location이 표시되는지 확인합니다."), new(6,"릴리즈 저장","COMPLETE 실행 시 스캔한 모든 LOT이 출고 처리되는지 확인합니다."),
        new(6,"릴리즈 완료 안내","Release Complete 알림이 표시되고 Truck Loading 가능한 상태가 되는지 확인합니다."));

    public static PptScenarioPanel.Step[] Loading(PptScenarioPanel.Step[] source) => Build("FG005", source,
        new(1,"트럭 바코드 우선 스캔","TRUCK:<차량번호> 형식의 바코드로 Truck Loading이 시작되는지 확인합니다."), new(1,"잘못된 스캔 순서 차단","트럭 확인 전에 Shipment 또는 Stock을 스캔하면 차단되는지 확인합니다."),
        new(2,"트럭 확인","Truck Information에 차량번호와 VERIFIED가 표시되는지 확인합니다."), new(2,"Shipment 스캔 안내","Next Scan이 Shipment Order Barcode로 변경되는지 확인합니다."),
        new(2,"다음 작업 안내","다음에 필요한 스캔 작업이 화면에 표시되는지 확인합니다."), new(3,"Shipment 정보","Customer, Destination, Ship Date, Product Count가 표시되는지 확인합니다."),
        new(3,"적재 대상 목록","Stock, Part, Qty, Location이 Products To Load에 표시되는지 확인합니다."), new(3,"Shipment 정보 자동 이동","Shipment 정보가 Next Scan 바로 아래에 보이도록 화면이 이동하는지 확인합니다."),
        new(4,"Stock 스캔 강조","스캔한 Stock 행이 초록색으로 변경되는지 확인합니다."), new(4,"스캔 진행 수량","Stock 하나를 스캔할 때마다 SCANNED 수량이 한 건씩 증가하는지 확인합니다."),
        new(4,"확정 버튼 비활성화","모든 Stock을 스캔하기 전에는 CONFIRM이 비활성화되는지 확인합니다."), new(5,"전체 Stock 완료","모든 Stock 스캔 후 전체 행이 초록색으로 변경되는지 확인합니다."),
        new(5,"전체 스캔 안내","SCANNED 3/3과 적재 완료 안내가 표시되는지 확인합니다."), new(5,"확정 버튼 활성화","모든 Stock 스캔 후 CONFIRM이 활성화되는지 확인합니다."),
        new(6,"중복 스캔 수량 유지","이미 스캔한 Stock을 다시 스캔해도 진행 수량이 증가하지 않는지 확인합니다."), new(6,"중복 스캔 알림","Already Scanned 알림에 중복 제품이 명확히 안내되는지 확인합니다."),
        new(7,"트럭 적재 저장","CONFIRM 실행 시 모든 제품이 해당 트럭에 적재 처리되는지 확인합니다."), new(7,"트럭 적재 완료 안내","완료 팝업에 적재 제품 수와 차량번호가 표시되는지 확인합니다."));

    public static PptScenarioPanel.Step[] Returns(PptScenarioPanel.Step[] source) => Build("FG006", source,
        new(1,"반품 제품 스캔","출하 완료된 FG 제품을 스캔하면 Customer Return이 시작되는지 확인합니다."), new(1,"수령 버튼 비활성화","제품과 반품 사유를 입력하기 전에는 RECEIVE가 비활성화되는지 확인합니다."),
        new(2,"반품 제품 정보","Part, Customer, Shipped At 정보가 표시되는지 확인합니다."), new(2,"반품 가능 상태","출하 완료 제품에 RETURN ELIGIBLE이 표시되는지 확인합니다."),
        new(2,"반품 사유 필수","Return Reason을 선택하지 않으면 RECEIVE가 차단되는지 확인합니다."), new(3,"드롭다운 방향","Return Reason 목록이 입력란 아래쪽으로 펼쳐지는지 확인합니다."),
        new(3,"반품 사유 항목","5개의 반품 사유가 모두 표시되는지 확인합니다."), new(3,"선택 입력 Note","Note가 선택 입력이며 왼쪽 위부터 작성되는지 확인합니다."),
        new(4,"반품 저장","RECEIVE 실행 시 반품 제품이 등록되는지 확인합니다."), new(4,"반품 완료 안내","완료 팝업에 수령된 제품 바코드가 표시되는지 확인합니다."),
        new(4,"반품 화면 초기화","완료 팝업의 OK를 누르면 입력 내용이 초기화되는지 확인합니다."));

    public static PptScenarioPanel.Step[] Adjust(PptScenarioPanel.Step[] source) => Build("FG007", source,
        new(1,"FG LOT 스캔","정상 FG LOT을 스캔하면 해당 재고가 조회되는지 확인합니다."), new(1,"초기 입력 상태","LOT 스캔 전에는 LOT 입력부만 사용할 수 있는지 확인합니다."),
        new(1,"WH LOT 차단","Warehouse 자재 LOT을 스캔하면 Warehouse Adjust 화면 안내가 표시되는지 확인합니다."), new(2,"재고 정보","Part, Current Quantity, Location이 표시되는지 확인합니다."),
        new(2,"수량 조절 버튼","마이너스와 플러스 버튼으로 정수 단위 수량이 변경되는지 확인합니다."), new(2,"수량 변경 요약","Before, Change, After 수량이 서로 일치하게 계산되는지 확인합니다."),
        new(3,"조정 입력 항목","Quantity, Reason, Note가 함께 표시되는지 확인합니다."), new(3,"Note 입력 유지","입력한 Note가 저장 전까지 화면에 유지되는지 확인합니다."),
        new(3,"입력 초기화","CLEAR 실행 시 완료되지 않은 조정 내용이 초기화되는지 확인합니다."), new(4,"조정 사유 목록","Count Diff, Damaged, Lost, Found, Other가 모두 표시되는지 확인합니다."),
        new(4,"조정 사유 선택","선택한 Reason이 저장 전까지 유지되는지 확인합니다."), new(5,"숫자 키패드","Quantity 입력란 선택 시 숫자 키패드가 표시되는지 확인합니다."),
        new(5,"정수 수량 입력","정수 수량과 Reason을 입력하면 SAVE가 활성화되는지 확인합니다."), new(6,"소수 입력 차단","소수 수량은 저장할 수 없는지 확인합니다."),
        new(6,"음수 입력 차단","음수 수량은 저장할 수 없는지 확인합니다."), new(6,"빈 수량 차단","수량이 비어 있으면 저장할 수 없는지 확인합니다."),
        new(7,"수량 조정 저장","SAVE 실행 시 FG 수량이 저장되고 Saved 알림이 표시되는지 확인합니다."), new(7,"저장 후 초기화","저장 성공 후 LOT, Quantity, Reason, Note가 초기화되는지 확인합니다."));

    public static PptScenarioPanel.Step[] Transactions(PptScenarioPanel.Step[] source) => Build("FG008", source,
        new(1,"최신 거래 순서","선택한 기간의 거래가 작업일시 최신순으로 표시되는지 확인합니다."), new(1,"거래 유형별 건수","ALL과 IN, PICK, LOAD, RETURN, ADJ 건수가 목록과 일치하는지 확인합니다."),
        new(1,"새로고침","REFRESH 실행 시 조회 조건을 유지하면서 최신 목록을 다시 불러오는지 확인합니다."), new(2,"조회 기간 적용","APPLY 실행 시 선택한 시작일과 종료일 안의 거래만 표시되는지 확인합니다."),
        new(2,"작업자 및 사유 필터","More Filters에서 Worker ID와 Reason으로 결과를 좁힐 수 있는지 확인합니다."), new(3,"바코드 검색","LOT, Stock, Part, 출고전표로 거래 이력을 검색할 수 있는지 확인합니다."),
        new(3,"검색 결과 없음","존재하지 않는 바코드는 빈 결과를 표시하고 CLEAR로 초기화되는지 확인합니다."), new(4,"거래 목록 항목","Date, LOT, Part, Location, Qty, Worker, Type이 표시되는지 확인합니다."),
        new(4,"피킹과 적재 구분","PICK과 LOAD가 서로 다른 거래 유형으로 기록되는지 확인합니다."), new(4,"반품 상세 정보","반품 상세에 Reason, Note, Reference가 표시되는지 확인합니다."),
        new(5,"수량 조정 상세 열기","ADJ 행의 DETAIL을 누르면 수량 조정 상세가 열리는지 확인합니다."), new(5,"수량 조정 값","상세 화면에 Before 20, Change +2, After 22가 표시되는지 확인합니다."),
        new(5,"상세 화면 닫기","CLOSE 실행 후 기존 검색어와 필터 조건이 유지되는지 확인합니다."));
}
