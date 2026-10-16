/* ------------------------------------------------------------------
   migrate_aspnetusertokens_pk.sql — AspNetUserTokens 기본키에 UserId 포함(10-08, 사용자 결정)

   AspNetUserTokens 의 기본키가 (LoginProvider, Name) 이라 사람(UserId)이 빠져 있었다.
   2FA 인증 앱 키(`[AspNetUserStore]`/`AuthenticatorKey`)·복구 코드(`RecoveryCodes`)는 사람마다 같은
   LoginProvider·Name 으로 한 행씩 쓰므로, 두 번째 사람부터 2FA 를 켜면 기본키 중복으로 실패했다.
   ASP.NET Identity 모델과 같게 (UserId, LoginProvider, Name) 으로 다시 만들고 UserId 를 NOT NULL 로 둔다.
   **외래키는 걸지 않는다** — 생산라인이 프로그램 문제로 멈추면 안 된다는 원칙(FK 없음이 정상).
   이 테이블은 웹 Identity(2FA)만 쓰고 POP·PDA 는 쓰지 않는다. 10-08 개발·로컬 DB 모두 0행.

   순서: ① UserId NULL 행 삭제(주인을 알 수 없는 토큰) ② 같은 (UserId, LoginProvider, Name) 중복은 1행만 남김
         ③ 기존 기본키 삭제 ④ UserId NOT NULL ⑤ 같은 이름(PK_AspNetUserTokens)으로 새 기본키.
   재실행 안전(기본키에 UserId 가 이미 있으면 건너뜀). nvarchar(450) 세 개라 "키 최대 길이 900바이트 초과" 경고가
   한 번 나올 수 있다 — 실제 값은 GUID(36자)·짧은 이름이라 문제없다(Identity 기본 구성과 같은 모양).
   적용: sqlcmd -S <서버> -d AMES_DEV ... -f 65001 -I -b -i dist\migrate_aspnetusertokens_pk.sql
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.indexes i
               JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
               JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE i.object_id = OBJECT_ID('dbo.AspNetUserTokens') AND i.is_primary_key = 1 AND c.name = 'UserId')
BEGIN
    BEGIN TRAN;

    DELETE FROM dbo.AspNetUserTokens WHERE UserId IS NULL;
    PRINT CONCAT(N'· UserId 없는 행 삭제: ', @@ROWCOUNT, N'건');

    ;WITH d AS (SELECT ROW_NUMBER() OVER (PARTITION BY UserId, LoginProvider, Name ORDER BY (SELECT 0)) AS rn FROM dbo.AspNetUserTokens)
    DELETE FROM d WHERE rn > 1;
    PRINT CONCAT(N'· 중복 행 삭제: ', @@ROWCOUNT, N'건');

    DECLARE @pk sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.AspNetUserTokens') AND type = 'PK');
    IF @pk IS NOT NULL
    BEGIN
        DECLARE @drop nvarchar(400) = N'ALTER TABLE dbo.AspNetUserTokens DROP CONSTRAINT ' + QUOTENAME(@pk) + N';';
        EXEC (@drop);
        PRINT N'· 기존 기본키 ' + @pk + N' 삭제';
    END;

    ALTER TABLE dbo.AspNetUserTokens ALTER COLUMN UserId nvarchar(450) COLLATE Korean_Wansung_CI_AS NOT NULL;

    -- 같은 배치의 NOT NULL 변경이 컴파일 시점에는 보이지 않아(Msg 8111) 동적 SQL 로 실행 시점에 만든다
    EXEC (N'ALTER TABLE dbo.AspNetUserTokens ADD CONSTRAINT PK_AspNetUserTokens PRIMARY KEY CLUSTERED (UserId, LoginProvider, Name);');
    PRINT N'· PK_AspNetUserTokens (UserId, LoginProvider, Name) 추가';

    COMMIT;
END
ELSE
    PRINT N'· 기본키에 UserId 가 이미 있음 — 건너뜀';
GO

SELECT i.name, i.type_desc, i.is_primary_key,
       STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS cols
FROM   sys.indexes i
JOIN   sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN   sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE  i.object_id = OBJECT_ID('dbo.AspNetUserTokens')
GROUP  BY i.name, i.type_desc, i.is_primary_key;
SELECT name, is_nullable FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AspNetUserTokens');
SELECT COUNT(*) AS Rows FROM dbo.AspNetUserTokens;
GO
