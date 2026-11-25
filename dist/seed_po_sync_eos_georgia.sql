-- PO 자동 수집(PP-002 「API 가져오기」·PO Sync Worker) — Seoyon E-Hwa Georgia EOS 조회 API 소스 추가 (11-16)
--
-- 기존 Savannah 소스(SEMS)는 그대로 두고 소스 키 GEO 를 더한다.
--   소스   SW_POSYNC_SOURCE / GEO : 고객 CUS-GEO, TYPE=EOS;VENDCD=309224,312598;WINDOW=-7,60
--   URL    SW_POSYNC_URL    / GEO : http://207.243.80.162/api/data/Query
--   인증   없음(EOS 조회 API 는 인증이 없다 — 인증 행이 없으면 인증 없이 부른다)
-- 협력사 코드(쉼표로 여러 개): 309224(LQ2 완제품 83335·83345·M83371 …) + 312598(11-25 실발주 4100187089, NQ5A M0211·M0221-P1010WK). Savannah 는 310471,312598.
-- 응답 루트가 OUT_CURSOR, 항번이 10.0 같은 소수라 같은 날 EosPoMapper 수정(신 AMES.Api)이 함께 필요하다.
--
-- 재실행 안전 — 없으면 넣고 있으면 값만 맞춘다. 적용: sqlcmd -C -f 65001 -I -b -d <DB> -i dist\seed_po_sync_eos_georgia.sql
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM MD_Customer WHERE CustomerID = 'CUS-GEO')
    THROW 50001, N'고객 CUS-GEO 가 없습니다 — dist/seed_md_customer_seoyon.sql 을 먼저 적용하세요.', 1;

BEGIN TRAN;

MERGE MD_CodeItem AS t
USING (VALUES
    ('SW_POSYNC_SOURCE_GEO', 'SW_POSYNC_SOURCE', 'GEO', N'Seoyon E-Hwa Manufacturing Georgia', N'Seoyon E-Hwa Manufacturing Georgia',
     'CUS-GEO', N'TYPE=EOS;VENDCD=309224,312598;WINDOW=-7,60'),
    ('SW_POSYNC_URL_GEO',    'SW_POSYNC_URL',    'GEO', N'Seoyon E-Hwa Manufacturing Georgia PO API', N'Seoyon E-Hwa Manufacturing Georgia PO API',
     NULL, N'http://207.243.80.162/api/data/Query')
) AS s (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, Attribute1, Description)
ON t.CodeID = s.CodeID
WHEN MATCHED THEN UPDATE SET
    t.CodeName = s.CodeName, t.CodeNameEn = s.CodeNameEn, t.Attribute1 = s.Attribute1, t.Description = s.Description,
    t.UseFlag = 1, t.ModifiedBy = 'seed', t.ModifiedTS = SYSDATETIME()
WHEN NOT MATCHED THEN INSERT (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, SortOrder, Attribute1, UseFlag, Description, CreatedBy, CreatedTS)
    VALUES (s.CodeID, s.GroupCode, s.CodeValue, s.CodeName, s.CodeNameEn, 2, s.Attribute1, 1, s.Description, 'seed', SYSDATETIME());

COMMIT;

SELECT CodeID, Attribute1, Description, UseFlag
FROM MD_CodeItem
WHERE GroupCode IN ('SW_POSYNC_SOURCE', 'SW_POSYNC_URL', 'SW_POSYNC_AUTH') AND CodeValue IN ('SEMS', 'GEO')
ORDER BY CodeValue, GroupCode;
