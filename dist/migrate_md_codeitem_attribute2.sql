/* ------------------------------------------------------------------
   migrate_md_codeitem_attribute2.sql  (2026-10-02) — migrate_md_codeitem_widen.sql 다음에 적용

   MD_CodeItem 에 Attribute2 NVARCHAR(200) NULL 을 Attribute1 바로 다음에 추가한다(공통코드 MD-026 화면에서 입력).
   컬럼 순서를 지키려고 테이블을 재생성한다 — 행·PK·기본값(UseFlag 1·CreatedTS SYSDATETIME)·컬럼/테이블 설명(MS_Description)을
   DB 에서 읽어 되살린다. MD_CodeItem 을 참조하는 FK 는 없다(있으면 중단).

   · 가드형: Attribute2 가 없거나 Attribute1 바로 다음이 아닐 때만 재생성. 재실행 안전.
   · 적용: sqlcmd -f 65001 -I -b
   · 구 Web·Pop·Api 는 컬럼을 이름으로 읽으므로 먼저 적용해도 안전하다. 신 Web·Pop·Api 를 마이그레이션 없이 올리면
     공통코드를 읽는 모든 화면이 `Invalid column name 'Attribute2'` 로 예외다.
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF COL_LENGTH('dbo.MD_CodeItem', 'Attribute2') IS NULL
   OR COLUMNPROPERTY(OBJECT_ID('dbo.MD_CodeItem'), 'Attribute2', 'ColumnId')
      <> COLUMNPROPERTY(OBJECT_ID('dbo.MD_CodeItem'), 'Attribute1', 'ColumnId') + 1
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('dbo.MD_CodeItem'))
        THROW 50001, 'MD_CodeItem 을 참조하는 FK 가 있습니다 — 재생성 전에 확인하세요.', 1;
    IF COL_LENGTH('dbo.MD_CodeItem', 'Attribute1') < 400
        THROW 50002, 'migrate_md_codeitem_widen.sql 을 먼저 적용하세요 (Attribute1 nvarchar(200) 아님).', 1;

    -- 설명 보관(컬럼 이름 기준, 테이블 설명은 ColumnName NULL)
    DECLARE @desc TABLE (ColumnName sysname NULL, Value sql_variant);
    INSERT INTO @desc (ColumnName, Value)
    SELECT CASE WHEN ep.minor_id = 0 THEN NULL ELSE COL_NAME(ep.major_id, ep.minor_id) END, ep.value
    FROM   sys.extended_properties ep
    WHERE  ep.major_id = OBJECT_ID('dbo.MD_CodeItem') AND ep.class = 1 AND ep.name = N'MS_Description';

    CREATE TABLE dbo.MD_CodeItem_new (
      [CodeID]        VARCHAR(41)    NOT NULL,
      [GroupCode]     VARCHAR(20)        NULL,
      [CodeValue]     VARCHAR(20)        NULL,
      [CodeName]      NVARCHAR(60)       NULL,
      [CodeNameEn]    NVARCHAR(60)       NULL,
      [ParentCodeID]  VARCHAR(41)        NULL,
      [SortOrder]     INT                NULL,
      [Attribute1]    NVARCHAR(200)      NULL,
      [Attribute2]    NVARCHAR(200)      NULL,
      [UseFlag]       BIT                NULL CONSTRAINT DF_MD_CodeItem_UseFlag_new DEFAULT 1,
      [Description]   NVARCHAR(500)      NULL,
      [CreatedBy]     VARCHAR(20)    NOT NULL,
      [CreatedTS]     DATETIME2          NULL CONSTRAINT DF_MD_CodeItem_CreatedTS_new DEFAULT SYSDATETIME(),
      [ModifiedBy]    VARCHAR(20)        NULL,
      [ModifiedTS]    DATETIME2          NULL,
      CONSTRAINT PK_MD_CodeItem_new PRIMARY KEY CLUSTERED ([CodeID])
    );

    -- 이전 판에서 Attribute2 가 끝에 붙어 있었으면 그 값도 옮긴다
    DECLARE @a2 nvarchar(40) = CASE WHEN COL_LENGTH('dbo.MD_CodeItem', 'Attribute2') IS NULL THEN N'NULL' ELSE N'Attribute2' END;
    DECLARE @sql nvarchar(max) = N'
        INSERT INTO dbo.MD_CodeItem_new
            (CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, Attribute2,
             UseFlag, Description, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS)
        SELECT CodeID, GroupCode, CodeValue, CodeName, CodeNameEn, ParentCodeID, SortOrder, Attribute1, ' + @a2 + N',
               UseFlag, Description, CreatedBy, CreatedTS, ModifiedBy, ModifiedTS
        FROM   dbo.MD_CodeItem;';
    EXEC sp_executesql @sql;
    PRINT CONCAT('MD_CodeItem 이관 행수: ', @@ROWCOUNT);

    DROP TABLE dbo.MD_CodeItem;
    EXEC sp_rename 'dbo.MD_CodeItem_new', 'MD_CodeItem';
    EXEC sp_rename 'dbo.PK_MD_CodeItem_new', 'PK_MD_CodeItem', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_CodeItem_UseFlag_new', 'DF_MD_CodeItem_UseFlag', 'OBJECT';
    EXEC sp_rename 'dbo.DF_MD_CodeItem_CreatedTS_new', 'DF_MD_CodeItem_CreatedTS', 'OBJECT';

    -- 설명 복원 + 새 컬럼 설명
    DECLARE @col sysname, @val sql_variant;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT ColumnName, Value FROM @desc;
    OPEN c; FETCH NEXT FROM c INTO @col, @val;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        IF @col IS NULL
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @val,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_CodeItem';
        ELSE IF COL_LENGTH('dbo.MD_CodeItem', @col) IS NOT NULL
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @val,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_CodeItem',
                 @level2type = N'COLUMN', @level2name = @col;
        FETCH NEXT FROM c INTO @col, @val;
    END
    CLOSE c; DEALLOCATE c;

    IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_CodeItem') AND name = N'MS_Description'
                   AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_CodeItem'), 'Attribute2', 'ColumnId'))
        EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = N'Attribute2 · nvarchar(200)',
             @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'MD_CodeItem',
             @level2type = N'COLUMN', @level2name = N'Attribute2';

    PRINT 'MD_CodeItem: Attribute2(Attribute1 다음) 추가 — 재생성 완료';
END
ELSE
    PRINT 'MD_CodeItem: 이미 Attribute2 가 Attribute1 다음에 있음';

COMMIT;
GO

-- 확인(재생성 뒤 별도 배치)
SELECT ORDINAL_POSITION, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM   INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'MD_CodeItem' ORDER BY ORDINAL_POSITION;
SELECT COUNT(*) AS Rows_, (SELECT COUNT(*) FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.MD_CodeItem')) AS Descriptions
FROM   dbo.MD_CodeItem;
GO
