/* ------------------------------------------------------------------
   migrate_by_columns_employee_no.sql  (2026-10-15)

   사람을 기록하는 "...By" 컬럼 중 varchar(20) 이 아니던 33개를 varchar(20)(사번)으로 맞추고,
   들어 있는 값을 그 사람의 사번으로 바꾼다(사용자 결정 10-15).
     · 09-23 migrate_audit_actor_varchar20.sql 이 CreatedBy·ModifiedBy·ApprovedBy·RequestedBy 를 맞췄고,
       이번에는 나머지 사람 컬럼(사용자 ID 를 담던 nvarchar(450) 32개 + PR_DefectDetail.DispositionBy varchar(50))
     · 값 변환(앞에서부터 처음 맞는 규칙):
         ① 이미 사번 — SYS_UserProfile.EmployeeNo 또는 MD_Worker.EmployeeNo 와 같으면 그대로
         ② 웹 사용자 ID(AspNetUsers.Id = SYS_UserProfile.UserID) → 그 프로필의 사번
         ③ 로그인 이메일·사용자명(AspNetUsers.Email / UserName) → 그 계정 프로필의 사번
         ④ SYS_AuditActorMap 의 EMPLOYEE 매핑(09-23 이 남긴 원문 → 사번)
         ⑤ 그 밖(사람이 아닌 표식 IT-TEST 등) — 20바이트 안이면 그대로, 넘으면 아무것도 바꾸지 않고 중단
     · ②~④ 로 바꾼 원래 값은 SYS_AuditActorMap(MappingKind EMPLOYEE)에 남긴다(이미 있으면 그대로) —
       ActorNames 가 옛 값도 이름으로 보인다
     · ALTER COLUMN 이라 컬럼 위치는 그대로. 33개 컬럼에는 인덱스·기본값·제약·NOT NULL 이 없다(10-15 개발·로컬 확인)
     · 컬럼 설명(MS_Description)의 "· nvarchar(450)" / "· varchar(50)" 는 "· varchar(20)" 으로 고친다
     · 같이 바뀐 코드: 이 컬럼에 쓰는 저장소 매개변수 18곳을 VarChar(20) 으로(값은 이미 사번 — POP 은 세션 사번,
       Web 은 ActorCode.Of), PP-001 업로드 이력의 등록자 표시를 ActorNames 로, tools 데모 시드의 user-* 값을 사번으로

   · 한 트랜잭션, 재실행 안전(이미 varchar(20) 인 컬럼은 건너뛴다). 적용: sqlcmd -f 65001 -I -b
   · 구 Web·Pop·Api 와 함께 써도 예외는 없다 — 구 바이너리도 같은 사번 값을 넣고, NVarChar 매개변수는 varchar 컬럼에 암시적 변환된다
   ------------------------------------------------------------------ */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_WARNINGS ON;

BEGIN TRY
    BEGIN TRANSACTION;

    CREATE TABLE #Targets (ID int IDENTITY PRIMARY KEY, TableName sysname COLLATE DATABASE_DEFAULT NOT NULL, ColumnName sysname COLLATE DATABASE_DEFAULT NOT NULL);  -- tempdb 콜레이션이 DB 와 다른 서버(복제본 Docker)에서 sys.columns 비교가 468 로 실패하지 않게
    INSERT #Targets (TableName, ColumnName) VALUES
        ('PR_AndonCall','AckedBy'),            ('QC_NCR_Action','ActionBy'),          ('PNT_LotLabel','AppliedBy'),
        ('PR_FabricIssueAttempt','AttemptedBy'),('PNT_ShiftReportAudit','ChangedBy'),  ('PP_ForecastHistory','ChangedBy'),
        ('PR_BondSetupAudit','ChangedBy'),     ('PR_MoldChange','ChangedBy'),         ('MNT_WorkOrderTask','CompletedBy'),
        ('PP_SupplyPlan','ConfirmedBy'),       ('PR_ImgLot','ConfirmedBy'),           ('PR_InjLot','ConfirmedBy'),
        ('PR_DefectDetail','DispositionBy'),   ('QC_InspectionStd','DraftedBy'),      ('QC_Hold','HeldBy'),
        ('PP_Forecast','ImportedBy'),          ('PNT_VirtualLot','IssuedBy'),         ('PR_BondSetup','LoadedBy'),
        ('PNT_PartLossLog','LoggedBy'),        ('PP_LineDowntimeLog','LoggedBy'),     ('PNT_LotLabel','PrintedBy'),
        ('PR_ShiftHandover','ReceivedBy'),     ('PR_DefectDetail','RegisteredBy'),    ('PP_WorkOrder','ReleasedBy'),
        ('QC_HoldRelease','ReleasedBy'),       ('MNT_FailureRegister','ReportedBy'),  ('QC_NCR','ReportedBy'),
        ('PNT_TagFailureLog','ResolvedBy'),    ('QC_Inspection','ResumeBy'),          ('PP_MRPLog','RunBy'),
        ('PNT_LabelScanLog','ScannedBy'),      ('PNT_ShiftReport','SignedBy'),        ('PR_ShiftHandover','SignedBy');

    -- 없는 컬럼·이미 varchar(20) 인 컬럼은 대상에서 뺀다(재실행 안전)
    DELETE t FROM #Targets t
    WHERE NOT EXISTS (SELECT 1 FROM sys.columns c
                      WHERE c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(t.TableName)) AND c.name = t.ColumnName
                        AND NOT (TYPE_NAME(c.user_type_id) = 'varchar' AND c.max_length = 20));

    IF EXISTS (SELECT 1 FROM #Targets t JOIN sys.columns c
                 ON c.object_id = OBJECT_ID(N'dbo.' + QUOTENAME(t.TableName)) AND c.name = t.ColumnName
               WHERE TYPE_NAME(c.user_type_id) NOT IN ('varchar','nvarchar') OR c.is_computed = 1)
        THROW 51000, 'Unsupported column type; nothing changed.', 1;

    -- 대상 컬럼의 서로 다른 값
    CREATE TABLE #Values (Val nvarchar(450) COLLATE Korean_Wansung_CI_AS NOT NULL PRIMARY KEY, EmpNo varchar(20) COLLATE Korean_Wansung_CI_AS NULL, Kind varchar(20) NULL);
    DECLARE @i int = (SELECT MIN(ID) FROM #Targets), @t nvarchar(300), @c nvarchar(300), @sql nvarchar(max);
    WHILE @i IS NOT NULL
    BEGIN
        SELECT @t = N'dbo.' + QUOTENAME(TableName), @c = QUOTENAME(ColumnName) FROM #Targets WHERE ID = @i;
        SET @sql = N'INSERT #Values (Val) SELECT DISTINCT CONVERT(nvarchar(450), x.' + @c + N') FROM ' + @t + N' x WITH (TABLOCKX, HOLDLOCK)
                     WHERE x.' + @c + N' IS NOT NULL AND NOT EXISTS (SELECT 1 FROM #Values v WHERE v.Val = CONVERT(nvarchar(450), x.' + @c + N'));';
        EXEC sys.sp_executesql @sql;
        SET @i = (SELECT MIN(ID) FROM #Targets WHERE ID > @i);
    END;

    -- ① 이미 사번
    UPDATE v SET Kind = 'EMPLOYEE_NO' FROM #Values v
    WHERE EXISTS (SELECT 1 FROM dbo.SYS_UserProfile p WHERE p.EmployeeNo = v.Val)
       OR EXISTS (SELECT 1 FROM dbo.MD_Worker w WHERE w.EmployeeNo = v.Val);
    -- ② 웹 사용자 ID
    UPDATE v SET EmpNo = p.EmployeeNo, Kind = 'USER_ID' FROM #Values v
    JOIN dbo.SYS_UserProfile p ON p.UserID = v.Val
    WHERE v.Kind IS NULL AND NULLIF(p.EmployeeNo, '') IS NOT NULL;
    -- ③ 로그인 이메일·사용자명
    UPDATE v SET EmpNo = x.EmployeeNo, Kind = 'LOGIN' FROM #Values v
    CROSS APPLY (SELECT TOP 1 p.EmployeeNo FROM dbo.AspNetUsers u JOIN dbo.SYS_UserProfile p ON p.UserID = u.Id
                 WHERE (u.Email = v.Val OR u.UserName = v.Val) AND NULLIF(p.EmployeeNo, '') IS NOT NULL
                 ORDER BY CASE WHEN u.Email = v.Val THEN 0 ELSE 1 END) x
    WHERE v.Kind IS NULL;
    -- ④ 09-23 별칭 표의 사번 매핑
    IF OBJECT_ID(N'dbo.SYS_AuditActorMap', N'U') IS NOT NULL
        UPDATE v SET EmpNo = m.ActorCode, Kind = 'ACTOR_MAP' FROM #Values v
        JOIN dbo.SYS_AuditActorMap m ON m.OriginalValue = v.Val AND m.MappingKind = 'EMPLOYEE'
        WHERE v.Kind IS NULL;

    -- ⑤ 매칭 안 된 값이 20바이트를 넘거나 varchar 로 바꾸면 깨지면 중단
    IF EXISTS (SELECT 1 FROM #Values
               WHERE Kind IS NULL
                 AND (DATALENGTH(CONVERT(varchar(450), Val)) > 20
                      OR CONVERT(varbinary(900), Val) <> CONVERT(varbinary(900), CONVERT(nvarchar(450), CONVERT(varchar(450), Val)))))
    BEGIN
        SELECT Val AS UnmatchedLongValue FROM #Values
        WHERE Kind IS NULL AND DATALENGTH(CONVERT(varchar(450), Val)) > 20;
        THROW 51000, 'Unmatched value does not fit varchar(20); nothing changed.', 1;
    END;

    -- 바꾼 원래 값 보존
    IF OBJECT_ID(N'dbo.SYS_AuditActorMap', N'U') IS NULL
        CREATE TABLE dbo.SYS_AuditActorMap (
            OriginalHash  binary(32)    NOT NULL CONSTRAINT PK_SYS_AuditActorMap PRIMARY KEY,
            OriginalValue nvarchar(900) NOT NULL,
            ActorCode     varchar(20)   NOT NULL,
            MappingKind   varchar(20)   NOT NULL,
            RecordedAt    datetime2(7)  NOT NULL CONSTRAINT DF_SYS_AuditActorMap_RecordedAt DEFAULT SYSUTCDATETIME()
        );
    INSERT dbo.SYS_AuditActorMap (OriginalHash, OriginalValue, ActorCode, MappingKind)
    SELECT HASHBYTES('SHA2_256', CONVERT(nvarchar(900), v.Val)), v.Val, v.EmpNo, 'EMPLOYEE'
    FROM #Values v
    WHERE v.Kind IN ('USER_ID', 'LOGIN')
      AND NOT EXISTS (SELECT 1 FROM dbo.SYS_AuditActorMap m WHERE m.OriginalHash = HASHBYTES('SHA2_256', CONVERT(nvarchar(900), v.Val)));

    -- 값 변환 → 형식 변경 → 설명
    CREATE TABLE #Results (TableName sysname COLLATE DATABASE_DEFAULT, ColumnName sysname COLLATE DATABASE_DEFAULT, ConvertedRows int);
    SET @i = (SELECT MIN(ID) FROM #Targets);
    WHILE @i IS NOT NULL
    BEGIN
        DECLARE @tn sysname, @cn sysname, @desc nvarchar(4000);
        SELECT @tn = TableName, @cn = ColumnName, @t = N'dbo.' + QUOTENAME(TableName), @c = QUOTENAME(ColumnName) FROM #Targets WHERE ID = @i;
        SET @sql = N'UPDATE x SET ' + @c + N' = v.EmpNo FROM ' + @t + N' x
                       JOIN #Values v ON v.Val = CONVERT(nvarchar(450), x.' + @c + N')
                     WHERE v.EmpNo IS NOT NULL AND CONVERT(nvarchar(450), x.' + @c + N') <> v.EmpNo;
                     INSERT #Results VALUES (@tn, @cn, @@ROWCOUNT);
                     IF EXISTS (SELECT 1 FROM ' + @t + N' WHERE DATALENGTH(CONVERT(varchar(max), ' + @c + N')) > 20)
                         THROW 51000, ''Conversion would truncate data; nothing changed.'', 1;
                     ALTER TABLE ' + @t + N' ALTER COLUMN ' + @c + N' varchar(20) COLLATE Korean_Wansung_CI_AS '
                     + CASE WHEN COLUMNPROPERTY(OBJECT_ID(@t), @cn, 'AllowsNull') = 1 THEN N'NULL' ELSE N'NOT NULL' END + N';';
        EXEC sys.sp_executesql @sql, N'@tn sysname, @cn sysname', @tn, @cn;

        SET @desc = (SELECT CONVERT(nvarchar(4000), ep.value) FROM sys.extended_properties ep
                     WHERE ep.major_id = OBJECT_ID(@t) AND ep.name = N'MS_Description'
                       AND ep.minor_id = COLUMNPROPERTY(OBJECT_ID(@t), @cn, 'ColumnId'));
        IF @desc IS NOT NULL AND (@desc LIKE N'%· nvarchar(450)%' OR @desc LIKE N'%· varchar(50)%')
        BEGIN
            SET @desc = REPLACE(REPLACE(@desc, N'· nvarchar(450)', N'· varchar(20)'), N'· varchar(50)', N'· varchar(20)');
            EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = @desc,
                 @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = @tn,
                 @level2type = N'COLUMN', @level2name = @cn;
        END;
        SET @i = (SELECT MIN(ID) FROM #Targets WHERE ID > @i);
    END;

    COMMIT;

    SELECT COUNT(*) AS ChangedColumns, ISNULL(SUM(ConvertedRows), 0) AS ConvertedCells FROM #Results;
    SELECT TableName, ColumnName, ConvertedRows FROM #Results WHERE ConvertedRows > 0 ORDER BY TableName, ColumnName;
    SELECT Val AS OriginalValue, EmpNo AS EmployeeNo, ISNULL(Kind, 'UNMATCHED(kept)') AS Rule_ FROM #Values ORDER BY Kind, Val;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK;
    THROW;
END CATCH;
GO

-- 확인 — "...By" 컬럼 중 varchar(20) 이 아닌 것(TEST_* 제외)
SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH
FROM   INFORMATION_SCHEMA.COLUMNS c
JOIN   INFORMATION_SCHEMA.TABLES t ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_TYPE = 'BASE TABLE'
WHERE  c.TABLE_SCHEMA = 'dbo' AND c.COLUMN_NAME LIKE '%By' AND c.TABLE_NAME NOT LIKE 'TEST[_]%' AND c.TABLE_NAME <> 'sysdiagrams'
  AND  NOT (c.DATA_TYPE = 'varchar' AND c.CHARACTER_MAXIMUM_LENGTH = 20)
ORDER BY c.TABLE_NAME, c.COLUMN_NAME;
