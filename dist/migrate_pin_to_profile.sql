-- =====================================================================
--  migrate_pin_to_profile.sql
--  POP PIN 저장소 분리: AspNetUsers.PasswordHash -> SYS_UserProfile.PinHash
--
--  배경: 기존에는 POP 4자리 PIN과 Web 비밀번호(>=6자, ASP.NET Identity)가
--        AspNetUsers.PasswordHash 컬럼 하나를 공유했다. Web 비번 리셋이 PIN을
--        덮어써 POP 로그인이 깨지는 충돌을 없애기 위해 PIN을 전용 컬럼으로 분리.
--
--  적용:  sqlcmd -S localhost -d AMES_DEV -U ames_app -P !Dev2026 -f 65001 \
--             -i dist/migrate_pin_to_profile.sql
--         (-f 65001 = UTF-8; 없으면 한글 주석이 깨짐)
--
--  멱등(idempotent): 여러 번 실행해도 안전.
-- =====================================================================
SET NOCOUNT ON;

-- 1) PinHash 컬럼 추가 (없을 때만)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.SYS_UserProfile') AND name = N'PinHash')
BEGIN
    ALTER TABLE dbo.SYS_UserProfile ADD [PinHash] NVARCHAR(200) NULL;
    PRINT '  [+] SYS_UserProfile.PinHash 컬럼 추가됨';
END
ELSE
    PRINT '  [=] SYS_UserProfile.PinHash 이미 존재';
GO

-- 2) 기존 seed 운영자의 PIN 해시를 PasswordHash -> PinHash 로 복사
--    seed_pop_users 가 만든 행(CreatedBy='seed')만 대상. 아직 PinHash 가
--    비어있고 PasswordHash 에 값이 있는 경우에만 옮긴다.
UPDATE p
SET    p.PinHash = u.PasswordHash
FROM   dbo.SYS_UserProfile p
JOIN   dbo.AspNetUsers     u ON u.Id = p.UserID
WHERE  p.PinHash IS NULL
  AND  p.CreatedBy = 'seed'
  AND  u.PasswordHash IS NOT NULL;

PRINT '  [*] PIN 해시 이관 완료: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' 건';
GO

-- 3) (선택) 이관된 seed 운영자의 AspNetUsers.PasswordHash 를 비운다.
--    이들은 Web 포탈을 쓰지 않으므로 PasswordHash 를 PIN 용도로 남겨둘 이유가 없다.
--    Web 로그인도 병행하는 계정이 있다면 이 블록을 실행하지 말 것.
--    필요 시 아래 주석을 해제해 실행:
--
-- UPDATE u
-- SET    u.PasswordHash = NULL
-- FROM   dbo.AspNetUsers     u
-- JOIN   dbo.SYS_UserProfile p ON p.UserID = u.Id
-- WHERE  p.CreatedBy = 'seed'
--   AND  p.PinHash IS NOT NULL
--   AND  p.PinHash = u.PasswordHash;   -- PIN 과 동일한 값일 때만 (Web 비번 보호)
-- PRINT '  [*] seed 운영자 PasswordHash 정리 완료: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' 건';
GO
