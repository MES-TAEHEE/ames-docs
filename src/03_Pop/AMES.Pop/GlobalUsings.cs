// DB 서버 시각 기준의 Now/Today — 화면·라벨·"오늘" 필터에서 DateTime.Now/Today 대신 쓴다 (AMES.Data.Services.DbClock).
// 왜: PP-003·PP-LSB 는 DB 날짜로 WO·슬롯을 놓는데 POP 이 호스트 날짜로 "오늘 슬롯" 을 읽으면 DB 와 터미널의 시간대가
// 다를 때(EDT 터미널 + UTC DB) 저녁마다 계획이 안 보인다. 유휴 만료·로그 파일명·토스트처럼 DB 와 비교하지 않는 호스트 내부 시각은 DateTime.Now 그대로.
global using DbClock = AMES.Data.Services.DbClock;
// 생산 현황의 "오늘"·LOT 의 날짜는 전기일(07시 경계 — PopDay.Today·PopDay.Of), 달력 날짜가 필요한 곳(LotNo 헤더·CAPA 기한)은 DbClock.Today.
global using PopDay = AMES.Data.Services.PopDay;
