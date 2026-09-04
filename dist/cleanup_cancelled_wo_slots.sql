-- ════════════════════════════════════════════════════════════════════════
-- cleanup_cancelled_wo_slots.sql — 취소된 WO 가 남긴 라인 스케줄 슬롯 정리
--   · PP_WorkOrder.Status = 'Cancelled' 인 WO 의 PP_LineSchedule 행 삭제
--
-- WorkOrderRepository.CancelWo 가 헤더 상태만 바꾸고 슬롯은 그대로 두던
-- 시절에 쌓인 행을 걷어낸다. 남겨두면 두 곳에서 계속 새는 값이 된다.
--   · LineScheduleRepository.ReadDayCapacity 가 그 시간대를 점유로 세어
--     PP-003 / PP-LSB 의 새 WO 배치가 뒤로 밀린다
--   · POP 당일 판(Inj/ImgLotRepository.GetDailyItemSummary)의 PLAN 합계에 더해진다
-- 신버전 CancelWo 는 취소와 같은 트랜잭션에서 슬롯을 지우므로 이 스크립트는
-- 그 이전 데이터에만 필요하다.
--
-- idempotent(멱등). 적용: sqlcmd(ODBC17) -f 65001 -b -i dist/cleanup_cancelled_wo_slots.sql
-- ════════════════════════════════════════════════════════════════════════
USE AMES_DEV;
GO
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @n int;

SELECT @n = COUNT(*)
FROM   dbo.PP_LineSchedule s
JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
WHERE  w.Status = 'Cancelled';

IF @n = 0
    PRINT N'· 취소 WO 슬롯 없음 — 정리할 행이 없습니다.';
ELSE
BEGIN
    -- 지우기 전 내역을 남긴다 — 보드에서 사라지는 자리를 나중에 확인할 수 있게.
    SELECT w.WoNumber, w.ItemNo, s.LineID, s.ScheduleDate, s.StartMin, s.EndMin, s.PlannedQty, s.Status
    FROM   dbo.PP_LineSchedule s
    JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
    WHERE  w.Status = 'Cancelled'
    ORDER  BY s.ScheduleDate, s.LineID, s.StartMin;

    DELETE s
    FROM   dbo.PP_LineSchedule s
    JOIN   dbo.PP_WorkOrder    w ON w.WoID = s.WoID
    WHERE  w.Status = 'Cancelled';

    PRINT CONCAT(N'✓ 취소 WO 슬롯 삭제: ', @n, N'행');
END;
GO
