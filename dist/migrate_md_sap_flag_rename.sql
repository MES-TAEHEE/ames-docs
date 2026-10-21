-- ════════════════════════════════════════════════════════════════════════
-- migrate_md_sap_flag_rename.sql  (10-21)
-- SAPTransFlag → SAPFlag (10-21 회의 결정) — 이 컬럼이 있는 마스터는 MES 가 SAP 로 "보내는" 데이터가 아니라
-- SAP 에서 "받는" 데이터라서, 뜻에 맞게 이름을 SAPFlag(1 = SAP 에서 받은 행)로 바꾼다.
--   대상: MD_Item · MD_Vendor · MD_Location · MD_Customer · MD_BomVersion
--   컬럼 이름·기본값 제약 이름(DF_{테이블}_SAPTransFlag → DF_{테이블}_SAPFlag)·컬럼 설명만 바꾼다. 값·형식·자리는 그대로.
--   MES 화면·엑셀은 이 값을 쓰지 않는다(목록 「SAP」 열·수정 창 헤더 ID 옆 SAP 표시만) — 값은 앞으로 만들 SAP 수신이 쓴다.
--
-- 재실행 안전 — 이미 SAPFlag 인 테이블은 건너뛰고, 제약·설명도 지금 이름·값이 다를 때만 바꾼다.
-- **신 Web 과 함께 적용한다** — 적용 뒤 구 Web 은 MD-003·MD-004·MD-009·MD-010·MD-018 과 ListItems·ListBomVersions 를 쓰는 화면이
-- `Invalid column name 'SAPTransFlag'`, 적용 전 신 Web 은 같은 곳이 `Invalid column name 'SAPFlag'` 로 예외다.
-- 구 Pop·Api·InjAgent 는 이 컬럼을 읽지 않는다.
--
-- 적용: sqlcmd -S <서버> -d AMES_DEV -U ames_app -P <비밀번호> -C -f 65001 -I -b -i dist\migrate_md_sap_flag_rename.sql
-- ════════════════════════════════════════════════════════════════════════
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @d nvarchar(200) = N'SAP 에서 받은 데이터 여부 — 1 이면 SAP 에서 받은 행(MES 화면은 표시만) · bit';
DECLARE @tt TABLE (Name sysname COLLATE DATABASE_DEFAULT PRIMARY KEY);
INSERT @tt VALUES (N'MD_BomVersion'), (N'MD_Customer'), (N'MD_Item'), (N'MD_Location'), (N'MD_Vendor');
DECLARE @t sysname, @obj nvarchar(300), @df sysname, @want sysname, @cur sql_variant, @msg nvarchar(400);

BEGIN TRANSACTION;
WHILE EXISTS (SELECT 1 FROM @tt)
BEGIN
    SELECT TOP (1) @t = Name FROM @tt ORDER BY Name;
    SET @obj = N'dbo.' + @t;

    IF OBJECT_ID(@obj, N'U') IS NULL
        PRINT N'- ' + @t + N': 테이블 없음 — 건너뜀';
    ELSE IF COL_LENGTH(@obj, N'SAPTransFlag') IS NOT NULL AND COL_LENGTH(@obj, N'SAPFlag') IS NOT NULL
    BEGIN
        SET @msg = @t + N': SAPTransFlag 와 SAPFlag 가 둘 다 있다 — 수동 처리 필요';
        THROW 50001, @msg, 1;
    END
    ELSE
    BEGIN
        IF COL_LENGTH(@obj, N'SAPTransFlag') IS NOT NULL
        BEGIN
            DECLARE @col nvarchar(400) = @obj + N'.SAPTransFlag';
            EXEC sys.sp_rename @col, N'SAPFlag', N'COLUMN';
            PRINT N'✓ ' + @t + N'.SAPTransFlag → SAPFlag';
        END;

        IF COL_LENGTH(@obj, N'SAPFlag') IS NOT NULL
        BEGIN
            -- 기본값 제약 이름
            SELECT @df = dc.name FROM sys.default_constraints dc
            JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
            WHERE dc.parent_object_id = OBJECT_ID(@obj) AND c.name = N'SAPFlag';
            SET @want = N'DF_' + @t + N'_SAPFlag';
            IF @df IS NOT NULL AND @df <> @want AND OBJECT_ID(N'dbo.' + QUOTENAME(@want)) IS NULL
            BEGIN
                DECLARE @dfq nvarchar(400) = N'dbo.' + QUOTENAME(@df);
                EXEC sys.sp_rename @dfq, @want, N'OBJECT';
                PRINT N'✓ ' + @t + N': 기본값 제약 ' + @df + N' → ' + @want;
            END;

            -- 컬럼 설명
            SET @cur = NULL;
            SELECT @cur = ep.value FROM sys.extended_properties ep
            WHERE ep.class = 1 AND ep.name = N'MS_Description' AND ep.major_id = OBJECT_ID(@obj)
              AND ep.minor_id = COLUMNPROPERTY(OBJECT_ID(@obj), N'SAPFlag', 'ColumnId');
            IF @cur IS NULL
                EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @d,
                     @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = @t,
                     @level2type = N'COLUMN', @level2name = N'SAPFlag';
            ELSE IF CAST(@cur AS nvarchar(400)) <> @d
                EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = @d,
                     @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = @t,
                     @level2type = N'COLUMN', @level2name = N'SAPFlag';
        END;
    END;
    SET @df = NULL;
    DELETE @tt WHERE Name = @t;
END;
COMMIT TRANSACTION;
GO

-- 확인 — 다섯 테이블 모두 SAPFlag(bit NOT NULL, DF_{테이블}_SAPFlag = 0) 이고 SAPTransFlag 는 없어야 한다
SELECT t.name AS TableName, c.name AS ColumnName, TYPE_NAME(c.user_type_id) AS TypeName, c.is_nullable, dc.name AS DefaultName, dc.definition,
       CAST(ep.value AS nvarchar(200)) AS Description
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id AND c.name IN (N'SAPFlag', N'SAPTransFlag')
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
LEFT JOIN sys.extended_properties ep ON ep.class = 1 AND ep.name = N'MS_Description' AND ep.major_id = c.object_id AND ep.minor_id = c.column_id
WHERE t.name IN (N'MD_BomVersion', N'MD_Customer', N'MD_Item', N'MD_Location', N'MD_Vendor')
ORDER BY t.name;
GO
