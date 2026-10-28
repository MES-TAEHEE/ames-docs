/* ------------------------------------------------------------------
   migrate_md_paint_fabric_lotno.sql  (2026-10-15)

   MD_PaintFabric.LotNo  varchar(24) → nvarchar(50)  (공급사 LOT 번호 — 길이·문자 제한 완화, 사용자 결정)
     · ALTER COLUMN 이라 컬럼 위치는 그대로다. 기존 값은 그대로 옮겨진다(varchar → nvarchar 손실 없음)
     · LotNo 에는 인덱스·제약이 없다. NULL 허용 그대로
     · 같이 바뀐 곳: MasterDataRepository.Insert/UpdatePaintFabric 매개변수(NVarChar 50),
       MD-008 도료·원단 화면(Md/Rm/PaintFabric.razor) LOT 번호 입력 최대 50자

   · 재실행 안전(이미 nvarchar(50) 이면 건너뛴다). 적용: sqlcmd -f 65001 -I -b
   · 구 Web 과 함께 써도 예외는 없다 — 구 화면은 varchar(24) 매개변수로 넣을 뿐이다
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.MD_PaintFabric') AND name = 'LotNo'
             AND (TYPE_NAME(user_type_id) <> 'nvarchar' OR max_length <> 100))
BEGIN
    ALTER TABLE dbo.MD_PaintFabric ALTER COLUMN LotNo nvarchar(50) COLLATE DATABASE_DEFAULT NULL;
END

IF OBJECT_ID('dbo.MD_PaintFabric') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.MD_PaintFabric') AND name = N'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.MD_PaintFabric'), 'LotNo', 'ColumnId'))
        EXEC sys.sp_updateextendedproperty @name=N'MS_Description', @value=N'Lot No · nvarchar(50)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'MD_PaintFabric', @level2type=N'COLUMN',@level2name=N'LotNo';
    ELSE
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Lot No · nvarchar(50)', @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'MD_PaintFabric', @level2type=N'COLUMN',@level2name=N'LotNo';
END
GO

-- 확인
SELECT c.name, TYPE_NAME(c.user_type_id) AS type_name, c.max_length / 2 AS char_len, c.is_nullable, c.collation_name, c.column_id
FROM   sys.columns c
WHERE  c.object_id = OBJECT_ID('dbo.MD_PaintFabric') AND c.name = 'LotNo';
