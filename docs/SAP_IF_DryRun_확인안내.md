# SAP → AMES 수신 DryRun 확인 안내 (SAP·웅진 담당자용)

## 1. DryRun 이란

AMES 가 EOS_IF 의 `IF_STATUS='N'` 행을 읽어 **AMES 에 반영하면 어떻게 되는지 시험만 하고 실제로는 반영하지 않는 상태**입니다.

- AMES 마스터·발주 데이터는 바뀌지 않습니다(반영을 취소합니다).
- EOS_IF 의 `IF_STATUS` 는 `N` 그대로 남습니다. `Y`/`E` 로 바뀌지 않습니다.
- 대신 **각 행의 `IF_MSG` 에 시험 결과를 적어 둡니다.** 이것으로 적재 내용이 AMES 에 어떻게 들어갈지 확인할 수 있습니다.
- 약 5분마다 다시 읽습니다. 같은 결과면 `IF_MSG` 를 다시 쓰지 않습니다.

## 2. 확인하는 방법 (EOS_IF)

```sql
-- 마스터 (IF_MD_UOM / ITEM / CUSTOMER / VENDOR / LOCATION / BOM_HDR)
SELECT IF_SEQ, IF_ACTION, IF_STATUS, IF_MSG
FROM   dbo.IF_MD_UOM
WHERE  IF_STATUS = 'N'
ORDER  BY IF_SEQ;

-- 구매 PO·MRP
SELECT IF_SEQ, IF_ACTION, IF_STATUS, IF_MSG FROM dbo.IF_TX_PO_HDR        WHERE IF_STATUS = 'N' ORDER BY IF_SEQ;
SELECT IF_SEQ, IF_ACTION, IF_STATUS, IF_MSG FROM dbo.IF_TX_MRP_PLAN_HDR  WHERE IF_STATUS = 'N' ORDER BY IF_SEQ;
```

`IF_MSG` 가 `[DRYRUN]` 으로 시작하면 DryRun 결과입니다. 행이 아직 읽히지 않았으면 `IF_MSG` 가 비어 있습니다(최대 5분 대기).

## 3. 결과 문구의 뜻

| `IF_MSG` | 뜻 | 조치 |
|---|---|---|
| `[DRYRUN] AMES 반영 예정 결과 — 신규 등록(SAP 값)` | AMES 에 없는 키라 새로 등록됩니다. | 키·값이 맞는지 확인 |
| `… 변경(SAP 우선) 컬럼: 기존 → 새값; …` | AMES 에 같은 키가 있고, 표시된 컬럼이 **SAP 값으로 덮어써집니다.** `기존` 이 AMES 현재 값입니다. | 덮어써도 되는 값인지 확인 |
| `… 동일(변경 없음)` | AMES 값과 같습니다. | 없음 |
| `… ※ MES 관리 행 → SAP 행으로 전환` | AMES 에서 직접 관리하던 행이 SAP 관리 행으로 바뀝니다. | 의도한 건지 확인 |
| `… 비활성 처리(…)` | `IF_ACTION='D'` — 삭제가 아니라 비활성 처리됩니다. | 없음 |
| `[DRYRUN] AMES 반영 시 오류 — …` | AMES 에 넣다가 오류가 났습니다(길이 초과, 필수값 없음 등). | **B1 에서 값을 고치면 웅진이 새 행(N)을 적재합니다.** 반영 시 이 행은 `E` 가 됩니다. |

BOM·PO·MRP 는 한 행에 라인 요약이 같이 나옵니다.
- BOM: `라인 n건: 신규 a·변경 b·동일 c·IF 에 없어 비활성 d` — IF 라인에 없는 기존 AMES 라인은 비활성됩니다.
- PO: `라인 n건: 신규 a·변경 b·동일 c` (취소 시 `취소 처리 n`) — AMES 구매발주의 수량·입고 누계·납기·상태가 B1 값으로 덮어써집니다.
- MRP: `MRP 스냅샷 교체(기준일 …, 버킷 D/W): 기존 n행 삭제 → m행 등록` — 같은 버킷의 이전 계획이 통째로 교체됩니다.

## 4. 반영 정책 (합의된 내용)

1. SAP 와 AMES 의 값이 다르면 **SAP 값이 우선**입니다(덮어씀).
2. SAP 에만 있는 데이터는 **AMES 에 새로 등록**됩니다.
3. AMES 에만 있는 데이터는 **우선 그대로 둡니다**(삭제·비활성 없음). SAP 에서 사라진 것을 AMES 가 지우지 않습니다.

## 5. DryRun 을 끝내고 실제 반영으로 전환

DryRun 결과를 확인한 뒤 AMES 담당자에게 알려 주세요. AMES 가 설정(`SW_SAPIN.DRYRUN`)을 `0` 으로 바꾸면 다음 주기(최대 5분)부터 **실제로 반영하고 `IF_STATUS` 를 `Y`(성공) 또는 `E`(오류, `IF_MSG` 에 사유)로 되씁니다.** 이때 `[DRYRUN]` 문구는 지워집니다.

전환 전에 확인해 주세요.
- 변경(SAP 우선) 행의 `기존 → 새값` 이 모두 의도한 값인지(특히 단위 환산계수: G, KG).
- 오류 행이 없는지(있으면 B1 에서 수정 후 재적재).
- 적재 순서: UoM → 품목 → BOM → 고객 → 공급사 → 창고 → (PO·MRP). 선행 테이블에 `E` 가 남아 있으면 후행은 다음 주기로 밀립니다.

## 6. 참고 — AMES 쪽 상세 로그

AMES DB 의 `SAP_IfLog`(행별 이력)와 `SYS_InterfaceMonitor`(IF 별 마지막 실행 요약, `SAPIN-IF-xxx`)에도 같은 내용이 남습니다. EOS_IF 의 `IF_MSG` 로 충분하지만, "AMES 에만 있는 키 목록" 같은 요약은 AMES 쪽 로그(`KeyValue='AMES-ONLY'`)에만 있습니다. 필요하면 AMES 담당자에게 요청해 주세요.

## 7. 아직 없는 것

- AMES → SAP(B1) 송신(생산오더·실적·입고·출하 등)은 아직 구현되지 않았습니다. 이 안내는 SAP → AMES 수신만 다룹니다.
