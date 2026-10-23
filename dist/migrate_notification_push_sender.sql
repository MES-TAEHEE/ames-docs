-- ════════════════════════════════════════════════════════════════════════
--  migrate_notification_push_sender.sql
--  PUSH 알림 발송기(AMES.Api 「NOTIFY」 Worker) — 설정 공통코드 SW_NOTIFY · 조회 인덱스 · 수신자 키를 사번으로 (10-24)
--
--  10-24 사용자 결정: 알림은 PUSH(FCM)로 안드로이드 앱에 보낸다. 수신자는 사번 — 규칙의 수신 역할을 가진 활성 웹 사용자,
--  규칙에 Operator(ROLE-OPERATOR)가 있으면 PIN 이 있는 활성 현장 작업자 전원. 앱은 POST /api/auth/login(사번 + PIN)으로만 로그인하고
--  한 번 등록한 기기는 세션이 끝나도 받는다. 규칙 정본 AMES.Data.Services.Notify.NotifyRules, 저장소 NotificationRepository.
--
--  ① 공통코드 SW_NOTIFY(그룹 + INTERVAL 1 · LOOKBACK_MIN 60 · MAX_RETRY 3 · BATCH 100) — 없는 행만 만든다(MD-26 에서 고친 값을 덮지 않음)
--  ② 인덱스 — 앱 목록(수신자), 중복 방지(이벤트·원천), 발송 대상 잡기(채널·상태), 기기(사번·토큰). 없을 때만
--  ③ SYS_NotificationChannel.UserID · SYS_NotificationHistory.RecipientUserID 의 웹 사용자 ID(GUID)를 그 사람의 사번으로
--     (SYS_UserProfile.EmployeeNo 가 있는 행만 — 10-15 「사람 칸은 사번」 규칙과 같음). 이미 사번인 행은 그대로라 재실행 안전
--  테이블·컬럼 변경 없음. 순서 무관, 재실행 안전.
--  FCM 서비스 계정 키는 DB 가 아니라 Api 서버의 파일이다(appsettings Notify:FcmServiceAccountPath — 비우면 알림은 PENDING 으로만 쌓인다).
--  적용:  sqlcmd -S <서버> -U <계정> -P <pw> -d AMES_DEV -C -f 65001 -I -b -l 30 -i dist\migrate_notification_push_sender.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- ── ① 공통코드 SW_NOTIFY ──────────────────────────────────────────────
MERGE dbo.MD_CodeGroup AS tgt
USING (VALUES
    ('SW_NOTIFY', N'PUSH 알림 발송 설정', N'PUSH Notify Settings',
     N'Attribute1 = INTERVAL 주기(분, 0=중지) · LOOKBACK_MIN 새 이벤트 시간(분) · MAX_RETRY · BATCH. FCM 키는 Api appsettings Notify:FcmServiceAccountPath')
) AS src(GroupCode, GroupName, GroupNameEn, Description)
ON tgt.GroupCode = src.GroupCode
WHEN NOT MATCHED THEN
    INSERT (GroupCode, GroupName, GroupNameEn, Description, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.GroupCode, src.GroupName, src.GroupNameEn, src.Description, 1, 'migrate', SYSDATETIME());

MERGE dbo.MD_CodeItem AS tgt
USING (VALUES
    ('SW_NOTIFY_INTERVAL',     'SW_NOTIFY', 'INTERVAL',     N'감지·발송 주기(분)',        N'Interval (min)',          N'1',   1),
    ('SW_NOTIFY_LOOKBACK_MIN', 'SW_NOTIFY', 'LOOKBACK_MIN', N'새 이벤트로 볼 시간(분)',    N'Lookback (min)',          N'60',  2),
    ('SW_NOTIFY_MAX_RETRY',    'SW_NOTIFY', 'MAX_RETRY',    N'실패 재시도 횟수',           N'Max retry',               N'3',   3),
    ('SW_NOTIFY_BATCH',        'SW_NOTIFY', 'BATCH',        N'한 번에 보내는 최대 건수',   N'Batch size',              N'100', 4)
) AS src(CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder)
ON tgt.CodeID = src.CodeID
WHEN NOT MATCHED THEN
    INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, SortOrder, UseFlag, CreatedBy, CreatedTS)
    VALUES (src.CodeID, src.GroupCode, src.CodeValue, src.CodeName, src.CodeNameEn, src.Attribute1, src.SortOrder, 1, 'migrate', SYSDATETIME());
PRINT 'SW_NOTIFY merged';
GO

-- ── ② 인덱스 ──────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SYS_NotificationHistory') AND name = 'IX_SYS_NotificationHistory_Recipient')
    CREATE NONCLUSTERED INDEX IX_SYS_NotificationHistory_Recipient ON dbo.SYS_NotificationHistory (RecipientUserID, Channel, NotificationHistoryID)
        INCLUDE (EventTypeCode, SourceRefType, SourceRefID, Status, ReadAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SYS_NotificationHistory') AND name = 'IX_SYS_NotificationHistory_Source')
    CREATE NONCLUSTERED INDEX IX_SYS_NotificationHistory_Source ON dbo.SYS_NotificationHistory (EventTypeCode, SourceRefType, SourceRefID)
        INCLUDE (RecipientUserID, Channel, Address);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SYS_NotificationHistory') AND name = 'IX_SYS_NotificationHistory_Status')
    CREATE NONCLUSTERED INDEX IX_SYS_NotificationHistory_Status ON dbo.SYS_NotificationHistory (Channel, Status)
        INCLUDE (CreatedTS, ModifiedTS, RetryCount, Address);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SYS_NotificationChannel') AND name = 'IX_SYS_NotificationChannel_User')
    CREATE NONCLUSTERED INDEX IX_SYS_NotificationChannel_User ON dbo.SYS_NotificationChannel (UserID, Channel)
        INCLUDE (Address, IsEnabled, QuietHoursStart, QuietHoursEnd);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SYS_NotificationChannel') AND name = 'IX_SYS_NotificationChannel_Address')
    CREATE NONCLUSTERED INDEX IX_SYS_NotificationChannel_Address ON dbo.SYS_NotificationChannel (Channel, Address);
PRINT 'notification indexes ensured';
GO

-- ── ③ 수신자 키 → 사번 ─────────────────────────────────────────────────
UPDATE c SET UserID = p.EmployeeNo, ModifiedBy = 'migrate', ModifiedTS = SYSDATETIME()
FROM   dbo.SYS_NotificationChannel c
JOIN   dbo.SYS_UserProfile p ON p.UserID = c.UserID
WHERE  NULLIF(LTRIM(RTRIM(p.EmployeeNo)), '') IS NOT NULL;
PRINT CONCAT('SYS_NotificationChannel UserID -> EmployeeNo: ', @@ROWCOUNT);

UPDATE h SET RecipientUserID = p.EmployeeNo
FROM   dbo.SYS_NotificationHistory h
JOIN   dbo.SYS_UserProfile p ON p.UserID = h.RecipientUserID
WHERE  NULLIF(LTRIM(RTRIM(p.EmployeeNo)), '') IS NOT NULL;
PRINT CONCAT('SYS_NotificationHistory RecipientUserID -> EmployeeNo: ', @@ROWCOUNT);
GO

SELECT (SELECT COUNT(*) FROM dbo.MD_CodeItem WHERE GroupCode = 'SW_NOTIFY') AS NotifyCodes,
       (SELECT COUNT(*) FROM sys.indexes WHERE name IN ('IX_SYS_NotificationHistory_Recipient', 'IX_SYS_NotificationHistory_Source',
            'IX_SYS_NotificationHistory_Status', 'IX_SYS_NotificationChannel_User', 'IX_SYS_NotificationChannel_Address')) AS Indexes,
       (SELECT COUNT(*) FROM dbo.SYS_NotificationChannel c JOIN dbo.SYS_UserProfile p ON p.UserID = c.UserID) AS ChannelGuidLeft;
GO
