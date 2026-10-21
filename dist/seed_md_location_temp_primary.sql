-- ════════════════════════════════════════════════════════════════════════
-- seed_md_location_temp_primary.sql  (10-22)
-- 임시 1차 로케이션 — SAP 저장위치(1차)를 받기 전까지 상세 로케이션을 묶을 1차를 임시로 만들고
--   상위가 없는 상세를 그 아래에 넣는다(사용자 요청 10-22).
--   묶음: 생산 라인 로케이션(LocationType 'LINE' 또는 LineID 있음 — 사출 I1~I9·감싸기 W1~W5·도장 P1·P2)은 전부 1차 하나
--         'TMP-LINE'(명칭 Production Lines, 구역 코드 'LINE') 아래, 그 밖은 구역(AreaCode)마다 'TMP-' + 구역 코드(예 TMP-MAT_AREA)
--         — 명칭은 공통코드 WH_AREA 명칭(없으면 구역 코드).
--   1차는 창고 = 그 묶음 상세의 WhCode, LocationLevel 1·SAPFlag 0·CreatedBy 'TMP-PRIMARY', 재고를 두지 않는다.
--   상위가 없는 상세와 다른 임시 1차(TMP-PRIMARY) 아래에 있는 상세만 옮긴다 — 사람이 다른 1차에 넣은 상세는 건드리지 않는다.
--   옮긴 뒤 하위가 없어진 임시 1차는 지운다(10-22 처음 판의 라인별 TMP-I1~TMP-W5 정리). 사용 안 함 상세도 묶는다.
--
-- 전제: migrate_md_location_parent.sql(ParentLocationID·LocationLevel) 적용 뒤. 재실행 안전.
-- 중단: 한 묶음의 상세가 창고 코드 둘 이상에 걸쳐 있거나, 1차 ID 가 20자를 넘거나, 그 ID 가 이미 상세 로케이션이면 THROW(변경 없음).
--
-- SAP 1차로 바꿀 때: SAP 1차를 받은 뒤 상세의 ParentLocationID 를 SAP 1차로 옮기고 TMP- 1차를 지운다. 이 시드를 되돌리려면
--   UPDATE dbo.MD_Location SET ParentLocationID = NULL WHERE ParentLocationID IN (SELECT LocationID FROM dbo.MD_Location WHERE CreatedBy = 'TMP-PRIMARY' AND LocationLevel = 1);
--   DELETE dbo.MD_Location WHERE CreatedBy = 'TMP-PRIMARY' AND LocationLevel = 1;
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\seed_md_location_temp_primary.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH(N'dbo.MD_Location', N'LocationLevel') IS NULL OR COL_LENGTH(N'dbo.MD_Location', N'ParentLocationID') IS NULL
    THROW 50001, N'MD_Location.LocationLevel·ParentLocationID 가 없다 — migrate_md_location_parent.sql 을 먼저 적용할 것', 1;
GO

BEGIN TRANSACTION;

-- 상세마다 들어갈 묶음
DECLARE @detail TABLE (LocationID varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, GroupKey varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
                       WhCode varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, ParentLocationID varchar(20) COLLATE DATABASE_DEFAULT NULL);
INSERT @detail
SELECT L.LocationID,
       CASE WHEN L.LocationType = 'LINE' OR L.LineID IS NOT NULL THEN 'LINE' ELSE L.AreaCode END,
       L.WhCode, L.ParentLocationID
FROM dbo.MD_Location L WITH (UPDLOCK, HOLDLOCK)
WHERE L.LocationLevel = 2;

DECLARE @groups TABLE (GroupKey varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, WhCode varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
                       PrimaryID varchar(24) COLLATE DATABASE_DEFAULT NOT NULL, WhCount int NOT NULL);
INSERT @groups
SELECT d.GroupKey, MIN(d.WhCode), 'TMP-' + d.GroupKey, COUNT(DISTINCT d.WhCode)
FROM @detail d GROUP BY d.GroupKey;

DECLARE @msg nvarchar(2048);
SELECT @msg = STRING_AGG(CAST(g.GroupKey AS nvarchar(max)), N', ') FROM @groups g WHERE g.WhCount > 1;
IF @msg IS NOT NULL BEGIN SET @msg = N'묶음의 상세가 창고 코드 둘 이상에 걸쳐 있다: ' + @msg; THROW 50002, @msg, 1; END;
SELECT @msg = STRING_AGG(CAST(g.PrimaryID AS nvarchar(max)), N', ') FROM @groups g WHERE LEN(g.PrimaryID) > 20;
IF @msg IS NOT NULL BEGIN SET @msg = N'1차 ID 가 20자를 넘는다: ' + @msg; THROW 50003, @msg, 1; END;
SELECT @msg = STRING_AGG(CAST(g.PrimaryID AS nvarchar(max)), N', ')
FROM @groups g JOIN dbo.MD_Location L ON L.LocationID = g.PrimaryID WHERE L.LocationLevel <> 1;
IF @msg IS NOT NULL BEGIN SET @msg = N'1차 ID 가 이미 상세 로케이션이다: ' + @msg; THROW 50004, @msg, 1; END;

INSERT dbo.MD_Location (LocationID, LocationName, WhCode, AreaCode, LocationLevel, SAPFlag, ActiveFlag, CreatedBy)
SELECT g.PrimaryID,
       CASE WHEN g.GroupKey = 'LINE' THEN N'Production Lines' ELSE LEFT(COALESCE(NULLIF(C.CodeName, N''), g.GroupKey), 60) END,
       g.WhCode, g.GroupKey, 1, 0, 1, 'TMP-PRIMARY'
FROM @groups g
LEFT JOIN dbo.MD_CodeItem C ON C.GroupCode = 'WH_AREA' AND C.CodeValue = g.GroupKey
WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Location L WHERE L.LocationID = g.PrimaryID);
PRINT N'✓ 임시 1차 등록 ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';

-- 상위 없음 또는 다른 임시 1차 아래 → 자기 묶음의 임시 1차로
UPDATE L SET ParentLocationID = g.PrimaryID, ModifiedBy = 'TMP-PRIMARY', ModifiedTS = SYSDATETIME()
FROM dbo.MD_Location L
JOIN @detail d ON d.LocationID = L.LocationID
JOIN @groups g ON g.GroupKey = d.GroupKey
WHERE (L.ParentLocationID IS NULL
       OR (L.ParentLocationID <> g.PrimaryID
           AND EXISTS (SELECT 1 FROM dbo.MD_Location P WHERE P.LocationID = L.ParentLocationID AND P.LocationLevel = 1 AND P.CreatedBy = 'TMP-PRIMARY')));
PRINT N'✓ 상세 상위 지정 ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';

-- 하위가 없어진 임시 1차 정리
DELETE P FROM dbo.MD_Location P
WHERE P.LocationLevel = 1 AND P.CreatedBy = 'TMP-PRIMARY'
  AND NOT EXISTS (SELECT 1 FROM dbo.MD_Location C WHERE C.ParentLocationID = P.LocationID)
  AND NOT EXISTS (SELECT 1 FROM @groups g WHERE g.PrimaryID = P.LocationID);
PRINT N'✓ 하위 없는 임시 1차 삭제 ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N'건';

COMMIT TRANSACTION;
GO

-- 확인 — 1차마다 하위 수, 상위 없는 상세는 0 이어야 한다
SELECT P.LocationID AS PrimaryID, P.LocationName, P.WhCode, P.AreaCode, P.SAPFlag,
       (SELECT COUNT(*) FROM dbo.MD_Location C WHERE C.ParentLocationID = P.LocationID) AS Children
FROM dbo.MD_Location P
WHERE P.LocationLevel = 1
ORDER BY P.LocationID;
SELECT COUNT(*) AS DetailsWithoutParent FROM dbo.MD_Location WHERE LocationLevel = 2 AND ParentLocationID IS NULL;
GO
