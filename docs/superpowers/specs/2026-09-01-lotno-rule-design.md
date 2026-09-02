# LotNo 채번 신규칙 설계 — INJ 원천 Lot 9자리 코드

- 날짜: 2026-09-01
- 상태: 설계 확정 (사용자 승인)
- 적용 범위: AMES INJ 원천 Lot (InjAgent 자동 생성 + Pop 수동 실적 입력)

## 배경

현재 AMES 사출 원천 Lot 코드는 `InjLotRepository`가 C#에서 조립하는
`L{yyMMddHHmmssfff}-{lineId}-{cavityPos}` 타임스탬프 형식(최대 40자)이다.
수동입력 경로는 같은 ms 충돌을 "1ms씩 밀기 + UPDLOCK 사전 조회"로 회피한다.

레거시 SEMS(`AFN_ME_NEWLOTNO`)는 9자리 압축 코드
(년1 + 월1 + 일1 + ClientID2 + 순번4)를 쓰며, 2026년에 년도 기준을 리셋(A=2026)하면서
기존 Lot과의 헤더 충돌을 피하려고 월 인코딩을 D~O로 옮겼다.
이 시프트 트릭은 2052년 다음 리셋 때 재사용이 불가능하다
(남는 문자 P~Z는 11자라 12개월을 못 덮는다).

AMES는 레거시와 데이터가 섞이지 않는 완전 독립 시스템이므로,
9자리 스타일만 가져오고 인코딩은 지속 가능한 형태로 새로 설계한다.

## 결정 사항 (사용자 확인 완료)

| 항목 | 결정 |
|---|---|
| 대상 | AMES INJ 원천 Lot만 (WH/FG Lot 은 대상 아님) |
| 레거시 호환 | 불필요 — 완전 독립, 스타일만 차용 |
| 날짜 기준 | 달력 날짜 (업무일/교대 개념 없음) |
| 일 볼륨 | 라인당 하루 5,000 Lot 이하 → 순번 4자리 숫자로 충분 |
| 2052 문제 | 년도 26년 주기 순환으로 근본 해소 — 월 시프트 트릭 폐기 |

## 1. 포맷

```
[년 1][월 1][일 1][라인코드 2][순번 4] = 9자리
예: A91I10001  (2026-09-01, 라인코드 I1, 그날 첫 Lot)
```

| 자리 | 규칙 |
|---|---|
| 년 (1자) | `(연도 − 2026) mod 26` → `A`~`Z`. 26년 주기 순환 (A=2026, Z=2051, A=2052 …). 규칙 재변경 불필요 — Lot 실물·운영 데이터가 26년을 넘어 살지 않으므로 실무 모호성 없음 |
| 월 (1자) | 1~9월 = `1`~`9`, 10~12월 = `A`,`B`,`C` (레거시 원형 복귀 — D~O 시프트 없음) |
| 일 (1자) | 1~9일 = `1`~`9`, 10~31일 = `A`~`V` |
| 라인코드 (2자) | `MD_Line.LotPrefix` CHAR(2) — 라인별 유니크 등록 (예: `LINE-INJ-01` → `I1`) |
| 순번 (4자) | 헤더(앞 5자)별 1부터 증가, 4자리 0패딩. 9999 초과 시 예외 |

- 캐비티 정보는 코드에서 제외한다. `PR_InjLot.CavityPos` 컬럼과 ZPL 라벨 독립 필드로
  이미 보존되므로 추적성 손실이 없다.
- 기존 40자 코드(`L…` 접두)와는 길이 자체가 달라 충돌하지 않는다.

## 2. DB 변경 — `dist/migrate_lotno_rule.sql`

1. `MD_Line`에 `LotPrefix CHAR(2) NULL` 컬럼 추가 + NULL 제외 유니크 필터드 인덱스.
   기존 INJ 라인에 시드 값 부여 (예: `LINE-INJ-01` → `I1`).
2. 카운터 테이블 신설:
   ```sql
   CREATE TABLE dbo.SYS_LotSeq (
     Header     CHAR(5)   NOT NULL PRIMARY KEY,  -- 년월일+라인코드
     LastSeq    INT       NOT NULL,
     ModifiedTS DATETIME2 NOT NULL DEFAULT SYSDATETIME()
   );
   ```
3. `tbl_Lot.LotCode`에 NULL 제외 유니크 인덱스 추가 — 현재는 제약이 없어
   중복을 코드로만 막고 있으므로 DB 를 최종 방어선으로 만든다.
4. `AMES_Schema.sql` 동기 반영 + `dist/rebuild_db.ps1`의 `$scripts` 배열에 등록.

## 3. 채번 컴포넌트 — `AMES.Data` `LotNoGenerator`

- **인코딩은 순수 static 함수**: `EncodeYear` / `EncodeMonth` / `EncodeDay` /
  `BuildHeader(DateTime date, string linePrefix)` — DB 없이 단위 테스트 가능.
- **채번은 원자적 카운터 증가** (호출자의 트랜잭션에 참여):
  ```sql
  UPDATE dbo.SYS_LotSeq SET LastSeq += 1, ModifiedTS = SYSDATETIME()
  OUTPUT inserted.LastSeq WHERE Header = @H;
  ```
  행이 없으면 `INSERT (Header, 1)`, PK 충돌(동시 최초 채번) 시 UPDATE 1회 재시도.
  `MAX(LotCode)+1` 스캔은 쓰지 않는다.
- `LotPrefix`는 같은 트랜잭션에서 `MD_Line` 조회. **미등록이면 즉시 예외**
  (라인 ID 를 포함한 명확한 메시지).
- 순번 9999 초과 시 예외 — 조용히 잘못된 코드를 만들지 않는다.
- 커밋 전까지 해당 헤더의 카운터 행 잠금이 유지되어 같은 라인의 동시 생성이
  직렬화되지만, 하루 ≤5,000건 규모에서 무의미한 수준. 롤백 시 카운터도 같이
  롤백되므로 결번이 생기지 않는다.

## 4. 호출 경로 변경 — `InjLotRepository`

- `CreateRawLot` (src/02_Data/AMES.Data/Repositories/InjLotRepository.cs:65)의
  타임스탬프 조립을 `LotNoGenerator` 호출로 교체.
- `CreateManualRawLots` (211행 부근)의 "1ms씩 밀기" 루프와 UPDLOCK 사전 조회를
  삭제하고 같은 생성기를 호출 — 두 경로가 동일 코드를 탄다.
- 40자 길이 체크는 불필요해지므로 제거.

## 5. 기존 데이터 호환

- 기존 Lot 은 재발번하지 않는다. 조회·확정(`ConfirmByLotCode`)은 완전일치
  매칭이라 신·구 형식이 섞여도 무해하다.
- ZPL 라벨·PrintClaim·LabelDispatcher 흐름은 변경 없음 (코드가 짧아질 뿐).

## 6. 에러 처리

- `LotPrefix` 미등록·순번 초과 예외는 기존 경로를 그대로 탄다:
  에이전트 무인 루프 → dispatch 로그(`dispatch-YYYYMMDD.log`) 기록,
  Pop 수동입력 → 토스트 표시.

## 7. 테스트

- 인코딩 경계 단위 테스트:
  - 년: 2026→`A`, 2051→`Z`, 2052→`A`
  - 월: 9월→`9`, 10월→`A`, 12월→`C`
  - 일: 9일→`9`, 10일→`A`, 31일→`V`
- 리포지토리 테스트 (기존 `InjLotRepositoryTests` 패턴):
  - 같은 날짜+라인에서 순번 연속 증가 (`…0001` → `…0002`)
  - 날짜가 바뀌면 새 헤더로 1부터
  - 병렬 생성 시 중복·결번 없음 (유니크 인덱스 위반 0건)
  - `LotPrefix` 미등록 라인은 예외
