-- ════════════════════════════════════════════════════════════════════════
--  seed_md_mold_master.sql
--  금형 마스터 — MD_Item 의 사출 성형 SUB(코어 35 · 레일 21 = 56 품번, 금형 28종) 기준으로 금형 4테이블을 전부 재적재한다.
--  PNL ASSY(감싸기 결과)·MODULE·S-서브는 조립품이라 대상이 아니다.
--
--  참고 자료: 서연이화 사바나 Mold Master(2026-10-02, SIS APM2120 추출, 134행·금형 61종). 엑셀은 정본이 아니라
--  양식·작명의 참고다 — 우리 MD_Item 에 있는 품번만 대상으로 하고, 엑셀에 같은 품번이 있는 금형 8종은 엑셀의
--  금형번호·금형명을 그대로 쓰며(LQ2RLGDU·LQ2RLSTD·LQ2RLOPT·MEADTFUP·MEADTRUP·MEADTRUC·NEAFUC--·NEARUC--)
--  나머지는 같은 규칙으로 작명했다. HL(헤드라이닝) 금형은 우리 공장에 공정이 없어 넣지 않는다(사용자 결정 10-02).
--
--  규칙
--  · 금형번호 = 차종 3자(LQ2·MEA·NEA·LXA·MVA·NQA·NXA) + 부위(DTRU·FUC·RUC·RL…) + 옵션(S/C/O), '-' 로 8자 채움.
--    하이픈을 뺀 MoldCodeClean 이 PLC 금형코드와 맞는 구조이고 28종 모두 하이픈 제거 후에도 유일하다.
--  · 엑셀처럼 LH/RH 는 한 금형(CavitySeq LH 1 · RH 2), 옵션(STD/CUR/IMS)과 색은 품번 행으로 구분.
--  · MD_Mold      : Status AVAILABLE(공통코드 MOLD_STATUS) · RatedShots 500000 · CavityCount = LH/RH 합 ·
--                   Tonnage 코어 650 / 레일 450 · MoldChangeMin 코어 30 / 레일 20 · CarType = 품목 차종.
--  · MD_MoldItem  : Color = 품번 11자 뒤 색 접미(LX3A 레일 NNB/YGU), 접미 없으면 BK(BOM Master 규칙 "BK 는 무접미") ·
--                   Usage 1 · ResinItemNo/ResinUsage = 그 품번 BOM 의 RESIN 자식·QtyPer(G) (NQ5A 레일만 없음) ·
--                   CavityCount 1 · MoldCategory INJECTION.
--  · MD_MoldColor : MoldItem 색 파생.   MD_MoldLine : 차종 → INJ 라인(NE1A 01 · LQ2 02 · LX3A 03 · ME1A 04 ·
--                   MV1A 05 · NQ5A/NX5A 06, seed_inj_img_master_dev 와 같은 규칙), UPH = 60 × 캐비티, PrepTime = MoldChangeMin.
--  · 적재 전에 MD_MoldLine·MD_MoldColor·MD_MoldItem·MD_Mold 를 전부 비운다. 사라지는 금형을 가리키는
--    PP_LineSchedule WO 슬롯의 MoldID 는 NULL, 금형교체(MC) 행은 삭제, MNT_EquipmentStatus.MountedMoldID 는 NULL.
--    PR_InjLot·PR_ShotCount·MNT_MoldShotCount·PR_MoldChange 의 구 MoldID 는 이력이라 건드리지 않는다(FK 없음).
--  · 대상 품번이 MD_Item 에 없으면 THROW — seed_md_item_bom_master_list 가 바뀌면 여기도 맞춘다.
--  · 재실행 안전(같은 결과로 다시 적재). 운영 DB 는 MD-007 에서 실측값으로 고친 뒤 이 스크립트를 다시 돌리지 말 것.
--  적용: sqlcmd -f 65001 -I -b -i dist/seed_md_mold_master.sql   (seed_md_item_bom_master_list · migrate_mold_master · migrate_mold_change_plan 뒤)
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRAN;

-- 1) 금형 × 품번 정의 (금형번호, 금형명, 차종, 종류 C=코어 R=레일, 품번, LH/RH)
IF OBJECT_ID('tempdb..#def') IS NOT NULL DROP TABLE #def;
CREATE TABLE #def (
    MoldID    varchar(20)  COLLATE DATABASE_DEFAULT NOT NULL,
    MoldName  nvarchar(50) COLLATE DATABASE_DEFAULT NOT NULL,
    CarType   varchar(10)  COLLATE DATABASE_DEFAULT NOT NULL,
    Kind      char(1)      NOT NULL,
    ItemNo    varchar(20)  COLLATE DATABASE_DEFAULT NOT NULL,
    Pos       varchar(4)   COLLATE DATABASE_DEFAULT NOT NULL,
    PRIMARY KEY (MoldID, ItemNo));
INSERT INTO #def (MoldID, MoldName, CarType, Kind, ItemNo, Pos) VALUES
-- LQ2 ─ 엑셀 금형 3종 그대로 + 코어 2종
('LQ2RLSTD', N'LQ2 RAIL STD',                'LQ2',  'R', '83314-P8000',    'LH'),
('LQ2RLSTD', N'LQ2 RAIL STD',                'LQ2',  'R', '83324-P8000',    'RH'),
('LQ2RLOPT', N'LQ2 RAIL OPT',                'LQ2',  'R', '83314-P8010',    'LH'),
('LQ2RLOPT', N'LQ2 RAIL OPT',                'LQ2',  'R', '83324-P8010',    'RH'),
('LQ2RLGDU', N'LQ2 GARNISH DR UPR LH/RH',    'LQ2',  'C', 'D3133-P8000',    'LH'),
('LQ2RLGDU', N'LQ2 GARNISH DR UPR LH/RH',    'LQ2',  'C', 'D3143-P8000',    'RH'),
('LQ2DTRUS', N'LQ2 DOOR RR UPR CORE STD',    'LQ2',  'C', 'D0133-P8000',    'LH'),
('LQ2DTRUS', N'LQ2 DOOR RR UPR CORE STD',    'LQ2',  'C', 'D0143-P8000',    'RH'),
('LQ2DTRUC', N'LQ2 DOOR RR UPR CORE CURT',   'LQ2',  'C', 'D0133-P8010',    'LH'),
('LQ2DTRUC', N'LQ2 DOOR RR UPR CORE CURT',   'LQ2',  'C', 'D0143-P8010',    'RH'),
-- LX3A ─ 레일 3종(색 NNB/YGU 각 행) + 코어 4종
('LXARLF--', N'LXA RAIL FRT UPR',            'LX3A', 'R', '82311-FC000NNB', 'LH'),
('LXARLF--', N'LXA RAIL FRT UPR',            'LX3A', 'R', '82311-FC000YGU', 'LH'),
('LXARLF--', N'LXA RAIL FRT UPR',            'LX3A', 'R', '82321-FC000NNB', 'RH'),
('LXARLF--', N'LXA RAIL FRT UPR',            'LX3A', 'R', '82321-FC000YGU', 'RH'),
('LXARLRS-', N'LXA RAIL RR UPR STD',         'LX3A', 'R', '83311-FC000NNB', 'LH'),
('LXARLRS-', N'LXA RAIL RR UPR STD',         'LX3A', 'R', '83311-FC000YGU', 'LH'),
('LXARLRS-', N'LXA RAIL RR UPR STD',         'LX3A', 'R', '83321-FC000NNB', 'RH'),
('LXARLRS-', N'LXA RAIL RR UPR STD',         'LX3A', 'R', '83321-FC000YGU', 'RH'),
('LXARLRC-', N'LXA RAIL RR UPR CURT',        'LX3A', 'R', '83311-FC010NNB', 'LH'),
('LXARLRC-', N'LXA RAIL RR UPR CURT',        'LX3A', 'R', '83311-FC010YGU', 'LH'),
('LXARLRC-', N'LXA RAIL RR UPR CURT',        'LX3A', 'R', '83321-FC010NNB', 'RH'),
('LXARLRC-', N'LXA RAIL RR UPR CURT',        'LX3A', 'R', '83321-FC010YGU', 'RH'),
('LXAFUS--', N'LXA FRT UPR CORE STD',        'LX3A', 'C', 'D0101-FC000',    'LH'),
('LXAFUS--', N'LXA FRT UPR CORE STD',        'LX3A', 'C', 'D0201-FC000',    'RH'),
('LXAFUO--', N'LXA FRT UPR CORE OPT(IMS)',   'LX3A', 'C', 'D0101-FC010',    'LH'),
('LXARUS--', N'LXA RR UPR CORE STD',         'LX3A', 'C', 'D4101-FC000',    'LH'),
('LXARUS--', N'LXA RR UPR CORE STD',         'LX3A', 'C', 'D4201-FC000',    'RH'),
('LXARUC--', N'LXA RR UPR CORE CURT',        'LX3A', 'C', 'D4101-FC010',    'LH'),
('LXARUC--', N'LXA RR UPR CORE CURT',        'LX3A', 'C', 'D4201-FC010',    'RH'),
-- ME1A ─ 엑셀 금형 3종 그대로 (C2311 RH 짝 C2321 은 우리 BOM 에 없다)
('MEADTFUP', N'MEA DOOR FRT UPR CORE',       'ME1A', 'C', 'C2311-TD000',    'LH'),
('MEADTRUP', N'MEA DOOR RR UPR CORE N',      'ME1A', 'C', 'C3311-TD000',    'LH'),
('MEADTRUP', N'MEA DOOR RR UPR CORE N',      'ME1A', 'C', 'C3321-TD000',    'RH'),
('MEADTRUC', N'MEA DOOR RR UPR CORE C',      'ME1A', 'C', 'C3311-TD100',    'LH'),
('MEADTRUC', N'MEA DOOR RR UPR CORE C',      'ME1A', 'C', 'C3321-TD100',    'RH'),
-- MV1A ─ 레일 3종 + 코어 3종 (D0121-XA000 은 품명이 LH 지만 품번 규칙상 RH 짝)
('MVARLF--', N'MVA RAIL FRT UPR',            'MV1A', 'R', '82314-XA000',    'LH'),
('MVARLRS-', N'MVA RAIL RR UPR STD',         'MV1A', 'R', '83314-XA000',    'LH'),
('MVARLRC-', N'MVA RAIL RR UPR CURT',        'MV1A', 'R', '83314-XA010',    'LH'),
('MVARLRC-', N'MVA RAIL RR UPR CURT',        'MV1A', 'R', '83324-XA010',    'RH'),
('MVAFUC--', N'MVA FRT UPR CORE',            'MV1A', 'C', 'D0111-XA000',    'LH'),
('MVAFUC--', N'MVA FRT UPR CORE',            'MV1A', 'C', 'D0121-XA000',    'RH'),
('MVARUS--', N'MVA RR UPR CORE STD',         'MV1A', 'C', 'D0131-XA000',    'LH'),
('MVARUS--', N'MVA RR UPR CORE STD',         'MV1A', 'C', 'D0141-XA000',    'RH'),
('MVARUC--', N'MVA RR UPR CORE CURT',        'MV1A', 'C', 'D0131-XA010',    'LH'),
('MVARUC--', N'MVA RR UPR CORE CURT',        'MV1A', 'C', 'D0141-XA010',    'RH'),
-- NE1A ─ 엑셀 금형 2종 그대로 + NE1A W(QI) 코어 2종
('NEAFUC--', N'NEA FRT UPR CORE',            'NE1A', 'C', 'D0111-PI010',    'LH'),
('NEAFUC--', N'NEA FRT UPR CORE',            'NE1A', 'C', 'D0121-PI010',    'RH'),
('NEARUC--', N'NEA RR UPR CORE(CURTAIN)',    'NE1A', 'C', 'D0131-PI020',    'LH'),
('NEARUC--', N'NEA RR UPR CORE(CURTAIN)',    'NE1A', 'C', 'D0141-PI020',    'RH'),
('NEAWFUC-', N'NEA W FRT UPR CORE',          'NE1A', 'C', 'D0101-QI000',    'LH'),
('NEAWFUC-', N'NEA W FRT UPR CORE',          'NE1A', 'C', 'D0201-QI000',    'RH'),
('NEAWRUC-', N'NEA W RR UPR CORE',           'NE1A', 'C', 'D4101-QI000',    'LH'),
('NEAWRUC-', N'NEA W RR UPR CORE',           'NE1A', 'C', 'D4201-QI000',    'RH'),
-- NQ5A ─ 코어 1종 + 레일 1종 (82324-DW010 은 품명이 LH 지만 품번 규칙상 RH 짝)
('NQAFUC--', N'NQA FRT UPR CORE',            'NQ5A', 'C', '82311-DW010',    'LH'),
('NQAFUC--', N'NQA FRT UPR CORE',            'NQ5A', 'C', '82324-DW010',    'RH'),
('NQARLF--', N'NQA RAIL FRT UPR',            'NQ5A', 'R', '82314-DW010',    'LH'),
-- NX5A ─ 코어 1종
('NXAGSO--', N'NXA GARNISH SIDE OTR',        'NX5A', 'C', 'D0103-NV010',    'LH');

-- 2) 가드
DECLARE @missing nvarchar(max) = (SELECT STRING_AGG(d.ItemNo, ', ') FROM (SELECT DISTINCT ItemNo FROM #def) d
                                  WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Item i WHERE i.ItemNo = d.ItemNo));
IF @missing IS NOT NULL THROW 50140, @missing, 1;   -- MD_Item 에 없는 품번 — seed_md_item_bom_master_list 확인
IF EXISTS (SELECT 1 FROM #def GROUP BY MoldID HAVING COUNT(DISTINCT MoldName) > 1 OR COUNT(DISTINCT CarType) > 1 OR COUNT(DISTINCT Kind) > 1)
    THROW 50141, N'같은 금형번호에 금형명·차종·종류가 다르다', 1;
IF EXISTS (SELECT 1 FROM (SELECT DISTINCT MoldID FROM #def) m GROUP BY REPLACE(m.MoldID, '-', '') HAVING COUNT(*) > 1)
    THROW 50142, N'하이픈을 뺀 금형코드(MoldCodeClean)가 겹친다', 1;
IF EXISTS (SELECT 1 FROM #def GROUP BY MoldID, Pos HAVING COUNT(DISTINCT LEFT(ItemNo, 11)) > 1)
    THROW 50143, N'한 금형의 같은 캐비티(LH/RH)에 색 변형이 아닌 다른 품번이 있다', 1;

IF OBJECT_ID('tempdb..#mold') IS NOT NULL DROP TABLE #mold;
SELECT d.MoldID, MIN(d.MoldName) AS MoldName, MIN(d.CarType) AS CarType, MIN(d.Kind) AS Kind,
       COUNT(DISTINCT d.Pos) AS CavityCount,
       CASE MIN(d.Kind) WHEN 'R' THEN 450 ELSE 650 END AS Tonnage,
       CASE MIN(d.Kind) WHEN 'R' THEN 20  ELSE 30  END AS MoldChangeMin
INTO   #mold
FROM   #def d GROUP BY d.MoldID;

IF OBJECT_ID('tempdb..#line') IS NOT NULL DROP TABLE #line;
CREATE TABLE #line (CarType varchar(10) COLLATE DATABASE_DEFAULT PRIMARY KEY, LineID varchar(20) COLLATE DATABASE_DEFAULT NOT NULL);
INSERT INTO #line VALUES ('NE1A','LINE-INJ-01'), ('LQ2','LINE-INJ-02'), ('LX3A','LINE-INJ-03'), ('ME1A','LINE-INJ-04'),
                         ('MV1A','LINE-INJ-05'), ('NQ5A','LINE-INJ-06'), ('NX5A','LINE-INJ-06');
IF EXISTS (SELECT 1 FROM #mold m WHERE NOT EXISTS (SELECT 1 FROM #line l WHERE l.CarType = m.CarType))
    THROW 50144, N'라인 매핑이 없는 차종이 있다', 1;
IF EXISTS (SELECT 1 FROM #line l WHERE NOT EXISTS (SELECT 1 FROM dbo.MD_Line x WHERE x.LineID = l.LineID))
    THROW 50145, N'INJ 라인이 없다 — MD_Line 확인', 1;

-- 3) 사라지는 금형 참조 정리 (재실행 때 같은 금형번호면 보존)
UPDATE dbo.PP_LineSchedule SET MoldID = NULL
WHERE  MoldID IS NOT NULL AND EntryType <> 'MC' AND NOT EXISTS (SELECT 1 FROM #mold m WHERE m.MoldID = PP_LineSchedule.MoldID);
PRINT CONCAT(N'§3 PP_LineSchedule WO 슬롯 MoldID NULL: ', @@ROWCOUNT);
DELETE FROM dbo.PP_LineSchedule
WHERE  EntryType = 'MC' AND (MoldID IS NULL OR NOT EXISTS (SELECT 1 FROM #mold m WHERE m.MoldID = PP_LineSchedule.MoldID));
PRINT CONCAT(N'§3 PP_LineSchedule MC 행 삭제: ', @@ROWCOUNT);
UPDATE dbo.MNT_EquipmentStatus SET MountedMoldID = NULL
WHERE  MountedMoldID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #mold m WHERE m.MoldID = MNT_EquipmentStatus.MountedMoldID);
PRINT CONCAT(N'§3 MNT_EquipmentStatus.MountedMoldID NULL: ', @@ROWCOUNT);

-- 4) 전부 비우고 재적재
DELETE FROM dbo.MD_MoldLine;
DELETE FROM dbo.MD_MoldColor;
DELETE FROM dbo.MD_MoldItem;
DELETE FROM dbo.MD_Mold;

INSERT INTO dbo.MD_Mold (MoldID, MoldName, RatedShots, CurrentShots, CavityCount, Tonnage, Status, CarType,
                         MoldChangeMin, CumulativeShots, AssyInjResultFlag, CreatedBy, CreatedTS)
SELECT m.MoldID, m.MoldName, 500000, 0, m.CavityCount, m.Tonnage, 'AVAILABLE', m.CarType,
       m.MoldChangeMin, 0, 0, 'MOLD-SEED', SYSDATETIME()
FROM   #mold m;
PRINT CONCAT(N'§4 MD_Mold: ', @@ROWCOUNT);

INSERT INTO dbo.MD_MoldItem (MoldID, ItemNo, Color, CavitySeq, CavityPos, [Usage], ResinItemNo, ResinUsage,
                             CavityCount, MoldCategory, ActiveFlag, CreatedBy, CreatedTS)
SELECT d.MoldID, d.ItemNo,
       CASE WHEN LEN(d.ItemNo) > 11 THEN SUBSTRING(d.ItemNo, 12, 10) ELSE 'BK' END,
       CASE d.Pos WHEN 'RH' THEN 2 ELSE 1 END, d.Pos, 1,
       r.ItemNo, r.QtyPer, 1, 'INJECTION', 1, 'MOLD-SEED', SYSDATETIME()
FROM   #def d
OUTER APPLY (SELECT TOP 1 c.ItemNo, CASE WHEN b.UOM = 'G' THEN b.QtyPer END AS QtyPer
             FROM   dbo.MD_Bom b JOIN dbo.MD_Item c ON c.ItemNo = b.CompItemNo
             WHERE  b.ParentItemNo = d.ItemNo AND c.ItemCategory = 'RESIN' AND ISNULL(b.ActiveFlag, 1) = 1
             ORDER  BY b.QtyPer DESC) r;
PRINT CONCAT(N'§4 MD_MoldItem: ', @@ROWCOUNT);

INSERT INTO dbo.MD_MoldColor (MoldID, Color, CreatedBy, CreatedTS)
SELECT DISTINCT i.MoldID, i.Color, 'MOLD-SEED', SYSDATETIME()
FROM   dbo.MD_MoldItem i;
PRINT CONCAT(N'§4 MD_MoldColor: ', @@ROWCOUNT);

INSERT INTO dbo.MD_MoldLine (LineCode, MoldID, UPH, PrepTime, CreatedBy, CreatedTS)
SELECT l.LineID, m.MoldID, 60 * m.CavityCount, m.MoldChangeMin, 'MOLD-SEED', SYSDATETIME()
FROM   #mold m JOIN #line l ON l.CarType = m.CarType;
PRINT CONCAT(N'§4 MD_MoldLine: ', @@ROWCOUNT);

COMMIT;

-- 확인
SELECT m.MoldID, m.MoldCodeClean, m.MoldName, m.CarType, m.CavityCount, m.Tonnage, m.MoldChangeMin,
       (SELECT STRING_AGG(i.ItemNo + '/' + i.Color + '/' + i.CavityPos, ' ') FROM dbo.MD_MoldItem i WHERE i.MoldID = m.MoldID) AS Items,
       (SELECT COUNT(*) FROM dbo.MD_MoldItem i WHERE i.MoldID = m.MoldID AND i.ResinItemNo IS NULL) AS NoResin,
       (SELECT STRING_AGG(ml.LineCode, ' ') FROM dbo.MD_MoldLine ml WHERE ml.MoldID = m.MoldID) AS Lines
FROM   dbo.MD_Mold m ORDER BY m.CarType, m.MoldID;
GO
