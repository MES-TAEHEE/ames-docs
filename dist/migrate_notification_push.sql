/* ------------------------------------------------------------------
   migrate_notification_push.sql — 알림 채널 PUSH 전환(10-08, 사용자 결정)

   메일·SMS 발송이 불가능해 알림은 내부 앱 PUSH 로 간다. 단 나중에 메일·SMS 를 다시 쓸 수 있으므로
   **지우지 않고 사용 여부(UseFlag)로만 끈다**:
     ① 공통코드 NOTIFICATION_CHANNEL — PUSH 를 사용(없으면 추가, 이전 이름 APP 이면 PUSH 로 개명), EMAIL·SMS 는 행을 남긴 채 사용 안 함
     ② SYS_NotificationRule.ChannelsJSON — 기존 채널(EMAIL·SMS)은 그대로 두고 PUSH 가 없으면 끝에 더한다
   발송 원칙: 규칙에 적힌 채널 중 **사용 중인 채널로만** 보낸다(앞으로 만들 발송기). 메일·SMS 를 다시 쓰려면 MD-030 에서 UseFlag 만 켠다.
   건드리지 않는 것: SYS_NotificationChannel(사용자별 주소)·SYS_NotificationHistory(과거 기록)·PR_AndonPush(안돈 기록).

   재실행 안전. 적용: sqlcmd -S <서버> -d AMES_DEV ... -f 65001 -I -b -i dist\migrate_notification_push.sql
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeGroup WHERE GroupCode = 'NOTIFICATION_CHANNEL')
    INSERT dbo.MD_CodeGroup (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy)
    VALUES ('NOTIFICATION_CHANNEL', N'알림 채널', N'Notification Channels', NULL, 1, 'PUSH-1008');

BEGIN TRAN;

-- ① 이전 이름 APP 을 PUSH 로(PUSH 행이 아직 없을 때만)
IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue = 'PUSH')
   AND EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue = 'APP')
    UPDATE dbo.MD_CodeItem
    SET    CodeID = 'NOTIFICATION_CHANNEL_PUSH', CodeValue = 'PUSH', CodeName = N'PUSH', CodeNameEn = N'PUSH',
           ModifiedBy = 'PUSH-1008', ModifiedTS = SYSDATETIME()
    WHERE  GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue = 'APP';

IF NOT EXISTS (SELECT 1 FROM dbo.MD_CodeItem WHERE GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue = 'PUSH')
    INSERT dbo.MD_CodeItem (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, UseFlag, Description, CreatedBy)
    VALUES ('NOTIFICATION_CHANNEL_PUSH', 'NOTIFICATION_CHANNEL', 'PUSH', N'PUSH', N'PUSH', NULL, 30, NULL, 1, NULL, 'PUSH-1008');

UPDATE dbo.MD_CodeItem SET UseFlag = 1, ModifiedBy = 'PUSH-1008', ModifiedTS = SYSDATETIME()
WHERE  GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue = 'PUSH' AND ISNULL(UseFlag, 0) = 0;

UPDATE dbo.MD_CodeItem SET UseFlag = 0, ModifiedBy = 'PUSH-1008', ModifiedTS = SYSDATETIME()
WHERE  GroupCode = 'NOTIFICATION_CHANNEL' AND CodeValue IN ('EMAIL', 'SMS') AND ISNULL(UseFlag, 1) = 1;

-- ② 규칙에 PUSH 추가 — 비어 있으면 ["PUSH"], 배열이면 끝에 더한다. JSON 이 아닌 값은 손대지 않고 아래 확인에서 보인다
UPDATE dbo.SYS_NotificationRule
SET    ChannelsJSON = N'["PUSH"]', ModifiedBy = 'PUSH-1008', ModifiedTS = SYSDATETIME()
WHERE  ChannelsJSON IS NULL OR LTRIM(RTRIM(ChannelsJSON)) IN (N'', N'[]');

UPDATE r
SET    ChannelsJSON = JSON_MODIFY(r.ChannelsJSON, 'append $', 'PUSH'), ModifiedBy = 'PUSH-1008', ModifiedTS = SYSDATETIME()
FROM   dbo.SYS_NotificationRule r
WHERE  ISJSON(r.ChannelsJSON) = 1 AND LEFT(LTRIM(r.ChannelsJSON), 1) = N'['
  AND  NOT EXISTS (SELECT 1 FROM OPENJSON(CASE WHEN ISJSON(r.ChannelsJSON) = 1 THEN r.ChannelsJSON ELSE N'[]' END) j WHERE j.[value] = N'PUSH');

COMMIT;
GO

SELECT CodeValue, CodeName, UseFlag, SortOrder FROM dbo.MD_CodeItem WHERE GroupCode = 'NOTIFICATION_CHANNEL' ORDER BY SortOrder;
SELECT NotificationRuleID, EventTypeCode, IsEnabled, ChannelsJSON FROM dbo.SYS_NotificationRule ORDER BY NotificationRuleID;
GO
