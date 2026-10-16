/* ------------------------------------------------------------------
   migrate_aspnetuserroles_pk.sql — AspNetUserRoles 복합 기본키(10-08, 사용자 결정)

   AspNetUserRoles 는 DB 전체에서 기본키가 없는 유일한 테이블이었다(두 컬럼 NULL 허용, 인덱스 0개).
   ASP.NET Identity 모델과 같게 (UserId, RoleId) 복합 기본키와 RoleId 보조 인덱스를 건다.
   **외래키는 걸지 않는다** — 생산라인이 프로그램 문제로 멈추면 안 된다는 원칙(FK 없음이 정상).
   이 테이블에 쓰는 곳은 웹 관리 화면(Identity)과 시드뿐이고 POP·PDA 는 읽기만 하므로 키가 현장 저장을 막지 않는다.

   순서: ① NULL 행 삭제 ② 같은 (UserId, RoleId) 중복은 1행만 남김 ③ 두 컬럼 NOT NULL ④ PK ⑤ IX_RoleId.
   재실행 안전(이미 있으면 건너뜀). nvarchar(450) 두 개라 "키 최대 길이 900바이트 초과" 경고가 한 번 나올 수 있다 —
   실제 값은 GUID(36자)라 문제없다(Identity 기본 구성과 같은 모양).
   적용: sqlcmd -S <서버> -d AMES_DEV ... -f 65001 -I -b -i dist\migrate_aspnetuserroles_pk.sql
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AspNetUserRoles') AND is_primary_key = 1)
BEGIN
    BEGIN TRAN;

    DELETE FROM dbo.AspNetUserRoles WHERE UserId IS NULL OR RoleId IS NULL;
    PRINT CONCAT(N'· NULL 행 삭제: ', @@ROWCOUNT, N'건');

    ;WITH d AS (SELECT ROW_NUMBER() OVER (PARTITION BY UserId, RoleId ORDER BY (SELECT 0)) AS rn FROM dbo.AspNetUserRoles)
    DELETE FROM d WHERE rn > 1;
    PRINT CONCAT(N'· 중복 행 삭제: ', @@ROWCOUNT, N'건');

    ALTER TABLE dbo.AspNetUserRoles ALTER COLUMN UserId nvarchar(450) COLLATE Korean_Wansung_CI_AS NOT NULL;
    ALTER TABLE dbo.AspNetUserRoles ALTER COLUMN RoleId nvarchar(450) COLLATE Korean_Wansung_CI_AS NOT NULL;

    -- 같은 배치의 NOT NULL 변경이 컴파일 시점에는 보이지 않아(Msg 8111) 동적 SQL 로 실행 시점에 만든다
    EXEC (N'ALTER TABLE dbo.AspNetUserRoles ADD CONSTRAINT PK_AspNetUserRoles PRIMARY KEY CLUSTERED (UserId, RoleId);');
    PRINT N'· PK_AspNetUserRoles (UserId, RoleId) 추가';

    COMMIT;
END
ELSE
    PRINT N'· PK 이미 있음 — 건너뜀';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AspNetUserRoles') AND name = 'IX_AspNetUserRoles_RoleId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_AspNetUserRoles_RoleId ON dbo.AspNetUserRoles (RoleId);
    PRINT N'· IX_AspNetUserRoles_RoleId 추가';
END
GO

SELECT i.name, i.type_desc, i.is_primary_key,
       STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS cols
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.object_id = OBJECT_ID('dbo.AspNetUserRoles')
GROUP  BY i.name, i.type_desc, i.is_primary_key;
SELECT name, is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AspNetUserRoles');
SELECT COUNT(*) AS Rows FROM dbo.AspNetUserRoles;
GO
