# 발주 → 외부 포탈 납품 → 내부 확인 테스트 매뉴얼 (yongkook)

내부 사용자 **yongkook@seoyonehga.com** (역할 Supply Manager) 한 사람이 처음부터 끝까지 진행합니다.

1. **내부 사이트** 에서 품목을 확인하고(없으면 등록), 협력업체와 연결하고, 구매발주를 만들어 발행합니다.
2. 같은 계정으로 **외부 포탈** 에 들어가 그 협력업체 입장에서 수주를 확인하고 납품서를 만듭니다.
3. 다시 **내부 사이트** 에서 수주 확인·납품이 제대로 반영됐는지 확인합니다.

| 항목 | 내용 |
|---|---|
| 내부 사이트 | <https://desktop-poedibs.tail526f68.ts.net/> |
| 외부 포탈 | <https://desktop-poedibs.tail526f68.ts.net/portal> |
| 계정 | yongkook@seoyonehga.com — 내부·포탈 모두 **같은 비밀번호**(담당자에게 받은 비밀번호) |
| 연결 협력업체 | 관리자가 이 계정에 연결해 둔 업체 하나(0단계에서 확인) |

> **그림에 대해** — 모든 그림은 사용자(yongkook) 계정으로 실제로 한 번 끝까지 진행하며 캡쳐한 화면입니다. 사용 된 값은 품번 `TST-MAT-PTL03`, 협력업체 `VC21216B8C9B441`, 발주 `PO-20261110-0003`, 납품서 `DLN-20261110-40` 입니다. 직접 테스트할 때는 새 품번·번호가 나옵니다.

> 한 브라우저에서 내부 사이트와 외부 포탈에 **동시에 로그인할 수 있습니다**(로그인이 따로입니다). 탭 두 개를 열어 두고 오가면 편합니다.

---

## 0. 내부 사이트 로그인과 연결 협력업체 확인

1. <https://desktop-poedibs.tail526f68.ts.net/> 에 yongkook 계정으로 로그인합니다. 홈에 SCM·RPT·WH·MD 메뉴가 보입니다.

![내부 사이트 홈 — Supply Manager](images/scm-portal-manual/a00-home.jpg)

2. 메뉴 **SCM → 외부 사용자 관리(SCM-005)** 에서 `yongkook@seoyonehga.com` 행을 찾습니다. **「내부 계정」** 표시와 **협력업체** 가 보입니다.

![SCM-005 — 내 계정의 연결 협력업체](images/scm-portal-manual/a01-scm005.jpg)

- ☐ 연결 협력업체: ____________________ (이후 모든 단계에서 이 업체를 씁니다)
- 행이 없으면 관리자에게 SYS-001 사용자 관리에서 「외부 포탈 사용」 을 켜 달라고 요청하세요.(이미 설정 되어 있습니다.)

---

## A. 내부 사이트 — 품목·업체 연결·발주

### A-1. 품목 확인·등록 — MD-003 제품 기준정보 관리

1. 메뉴 **MD → 제품 기준정보 관리(MD-003)** 에서 발주할 품번을 검색합니다.
   - **이미 있고 유형이 MATERIAL** 이면 A-2 로 넘어갑니다.
   - 「등록된 품목이 없습니다.」 이면 아래처럼 등록합니다.

![품목 검색 — 없음](images/scm-portal-manual/a02-item-search.jpg)

2. **등록** 을 누르고 입력합니다.
   - 품목번호(최대 20자), 품목명
   - **품목유형 `[MATERIAL] 구매자재`** ← 발주 품목은 반드시 MATERIAL
   - 카테고리(예: `[PART] 구매부품`), 단위(예: `EA (Each)`)
3. 아래 **등록** 을 누릅니다.

![품목 등록 — 유형 MATERIAL](images/scm-portal-manual/a03-item-create.jpg)

- ☐ 목록에 그 품번이 **MATERIAL** 로 보인다.

![품목 등록 완료](images/scm-portal-manual/a04-item-created.jpg)

### A-2. 협력업체 연결 — SCM-004 발주품목 관리

1. 메뉴 **SCM → 발주품목 관리(SCM-004)** 에서 품번을 검색합니다(MATERIAL 품목만 보입니다).
2. 「연결 업체」 가 **—** 이면 그 행 오른쪽 **업체 매칭** 을 누릅니다.

![발주품목 — 아직 연결 업체 없음](images/scm-portal-manual/a05-purchase-items.jpg)

3. 「연결할 업체 선택」 에서 **0단계의 연결 협력업체** 를 체크 → **업체 연결 (1)**.

![업체 선택](images/scm-portal-manual/a06-vendor-check.jpg)

- ☐ 「업체 연결 정보를 저장했습니다.」, 아래 「연결된 업체」 에 그 업체가 생긴다. **닫기**.

![업체 연결 완료](images/scm-portal-manual/a07-vendor-linked.jpg)

> 이 연결이 없으면 발주 작성에서 그 업체를 골랐을 때 품목이 나오지 않습니다.

### A-3. 구매발주 작성·발행 — SCM-001 구매발주 관리

1. 메뉴 **SCM → 구매발주 관리(SCM-001)** → **신규 발주**.
2. 「발주 작성」 창에 입력합니다.
   - **협력업체**: 0단계의 연결 협력업체(먼저 골라야 품목이 열립니다)
   - 납기일, 납품장소
   - 발주 품목(MATERIAL): A-1 품번, **수량**(예: `100`), **단가**(예: `1.00`)
3. **저장**. 발주번호는 저장할 때 자동으로 매겨집니다.

![발주 작성](images/scm-portal-manual/a08-po-editor.jpg)

4. 「발주서를 저장했습니다.」 와 함께 상세 창이 열리고 상태가 **작성중** 입니다. **발행** 을 누릅니다.

![저장 — 작성중](images/scm-portal-manual/a09-po-saved.jpg)

- ☐ 「발주서를 발행했습니다.」, 상태 **발행**, 수주 확인 **확인 대기**.
- ☐ 발주번호: ____________________

![발행 — 수주 확인 대기](images/scm-portal-manual/a10-po-published.jpg)

---

## B. 외부 포탈 — 수주 확인·납품서 생성

### B-1. 포탈 로그인

1. <https://desktop-poedibs.tail526f68.ts.net/portal> 에 접속합니다(새 탭 권장).
2. 이메일 `yongkook@seoyonehga.com`, **내부 사이트와 같은 비밀번호** → **로그인**.

![외부 포탈 로그인](images/scm-portal-manual/b01-login.jpg)

- ☐ 오른쪽 위에 `yongkook@seoyonehga.com` 과 **「내부 사용자 · <연결 업체>」** 가 보인다.
- ☐ 오른쪽 위에 **열쇠(비밀번호 변경) 버튼이 없다** — 비밀번호는 내부 사이트에서 바꿉니다.
- ☐ 「발주 건수」·「확인 대기」 에 A-3 발주가 포함돼 있다(연결 업체의 발주만 보입니다).

![포탈 홈](images/scm-portal-manual/b02-home.jpg)

### B-2. 수주 확인 — 발주 조회·수주 확인

1. 왼쪽 메뉴 **발주 조회·수주 확인** → A-3 발주 행을 누릅니다.

![포탈 발주 목록](images/scm-portal-manual/b03-orders.jpg)

2. 「발주 상세」 의 **수주 확인** → 확인창 **예**.

![발주 상세 — 확인 대기](images/scm-portal-manual/b04-order-detail.jpg)

![수주 확인 확인창](images/scm-portal-manual/b05-confirm-dialog.jpg)

- ☐ 「수주 확인을 완료했습니다.」, 수주 확인 **확인 완료**, **확인자 = yongkook 의 사번**(그림에서는 `GEO-001`).

![수주 확인 완료 — 확인자는 내부 사번](images/scm-portal-manual/b06-confirmed.jpg)

### B-3. 납품서 생성 — 납품 생성

1. 왼쪽 메뉴 **납품 생성** → 오른쪽 위 **납품서 생성**.

![납품 생성 목록](images/scm-portal-manual/b07-due-orders.jpg)

2. **Shipping Unit 추가** → 발주 목록에서 A-3 발주를 눌러 펼칩니다.

![납품서 생성 창](images/scm-portal-manual/b08-create-dialog.jpg)

3. **① 수량 입력**
   - 「UNIT ITEMS NEEDED」 가 **미등록** 이면 「Unit Item당 수량」(박스당 수량, 예: `20`)을 넣고 **Unit Item 수량 저장**. 한 번 저장하면 다음부터는 등록돼 있습니다.
   - **총 납품 수량**(예: `100`)을 넣으면 필요한 Unit Item 수(예: 5)가 계산됩니다.

![수량 입력 — Unit Item 미등록](images/scm-portal-manual/b09-step1.jpg)

![수량 입력 완료 — 필요 Unit Item 5](images/scm-portal-manual/b10-step1-qty.jpg)

4. **② Select Unit Items** — **전체 선택** → 오른쪽 아래 **완료**.

![Unit Item 전체 선택](images/scm-portal-manual/b11-step2-select.jpg)

5. 「Shipping Unit 1 · Unit Item 5개」 와 아래 「납품서 확인」 을 보고, 납품일을 확인한 뒤 **납품서 생성**.

![Shipping Unit 구성 완료](images/scm-portal-manual/b12-shipping-unit.jpg)

- ☐ 「DLN-… 납품서를 만들었습니다.」 와 **납품서 인쇄 · DLN-…** 링크.
- ☐ 납품서 번호: ____________________

![납품서 생성 완료](images/scm-portal-manual/b13-note-created.jpg)

> 일부만 보내려면 총 납품 수량을 발주 수량보다 작게 넣습니다. 남은 수량은 「납품 생성」 에 다시 남습니다.

### B-4. 납품서 확인·인쇄 — 납품서 관리

1. 왼쪽 메뉴 **납품서 관리**.

- ☐ 새 납품서가 보이고 위 카드 **「출하 완료」** 에 들어 있다 — 납품서를 만들면 바로 출하 처리됩니다.
- ☐ 입고수량 0, 잔량 = 납품수량(창고 입고 전).

![납품서 관리](images/scm-portal-manual/b14-deliveries.jpg)

2. 행의 **인쇄** → 납품서·Shipping Unit 바코드·Unit Item 바코드를 각각 인쇄할 수 있습니다. 하나 골라 **인쇄** 해 미리보기를 확인합니다.

![인쇄](images/scm-portal-manual/b15-print.jpg)

3. (참고) 왼쪽 메뉴 **적입량 관리** 에 B-3 에서 저장한 Unit Item당 수량(적입량)이 보입니다.

![적입량 관리](images/scm-portal-manual/b16-packing.jpg)

---

## C. 내부 사이트 — 결과 확인

내부 사이트 탭으로 돌아갑니다(로그인 그대로).

### C-1. SCM-001 구매발주 관리

A-3 발주 행을 눌러 상세를 엽니다.

- ☐ 수주 확인 **확인 완료**, **확인자 = yongkook 의 사번**, 확인일시.
- ☐ 「관련 납품서」 에 B-3 납품이 **출하 완료** 로 보인다.

![SCM-001 — 수주 확인·관련 납품서](images/scm-portal-manual/c01-scm001-detail.jpg)

### C-2. SCM-002 발주 진행 현황

발주번호로 조회합니다.

- ☐ **출하수량 = 납품 수량**, 입고수량 0, 미납수량 = 발주 − 입고.

![SCM-002 — 출하수량 반영](images/scm-portal-manual/c02-scm002.jpg)

### C-3. SCM-003 납품 진행 현황

납품서 번호로 조회합니다.

- ☐ 출하일·수량이 맞고 **UNIT ITEMS 0 / 5**(입고 전).

![SCM-003 — 납품서 진행](images/scm-portal-manual/c03-scm003.jpg)

---

## D. 로그아웃

- 포탈: 오른쪽 위 **⏻** → 포탈 로그인 화면(내부 로그인은 유지).
- 내부: 오른쪽 위 **⏻** → 내부 로그인 화면.

- ☐ 각각 로그인 화면으로 돌아간다.

---

## 막혔을 때

| 증상 | 확인할 것 |
|---|---|
| SCM-005 에 내 계정 행이 없다 | 관리자가 SYS-001 에서 「외부 포탈 사용」 + 협력업체를 연결해야 한다 |
| 발주 작성에서 품목이 안 나온다 | A-1 유형이 MATERIAL 인지, A-2 에서 **같은 업체** 를 연결했는지 |
| 발주 상세에 **발행** 버튼이 없다 | 이미 발행됐거나, 발주 승인(A) 권한이 없는 역할 |
| 포탈 「이메일 또는 비밀번호가 올바르지 않습니다.」 | 내부 사이트와 같은 비밀번호인지, 외부 포탈 사용이 켜져 있는지 |
| 포탈 「계정이 잠겨 있습니다.」 | 포탈에서 비밀번호를 5번 틀리면 **내부 계정도 잠긴다** — 관리자가 SYS-001 에서 잠금 해제 |
| 포탈 발주 목록에 발주가 없다 | A-3 를 **발행** 했는지, 발주 업체 = 0단계 연결 업체인지, 화면 위쪽 납기 기간 |
| 「납품 생성」 에 발주가 없다 | B-2 **수주 확인** 을 했는지 |
| 「Unit Item당 수량」 을 저장할 수 없다 | 안내 문구 확인 → A-2 업체 연결 다시 확인 |
