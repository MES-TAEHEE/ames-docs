using AMES.Data.Services;

namespace AMES.Data.Repositories;

// Parameterized batches owned by the application; no database procedure installation is required.
public static class PdaSparePartsSql
{
    public const string OpenWorkOrders = $$"""
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        SELECT w.WoNumber,w.EquipID,e.EquipName,w.ActionDesc,w.Status
        FROM dbo.MNT_WorkOrder w
        LEFT JOIN dbo.MD_Equipment e ON e.EquipID=w.EquipID
        WHERE w.Status IN ('ISSUED','ASSIGNED','IN_PROGRESS')
          AND NOT {{PmRules.PlannedWoSql}}
        ORDER BY w.IssuedAt DESC,w.WorkOrderID DESC;
        """;

    public const string SerialCreate = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @SpNo varchar(16)=UPPER(LTRIM(RTRIM(ISNULL(@SparePartNo,''))));
            DECLARE @User nvarchar(450)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA');
            DECLARE @Key varchar(15)=CASE WHEN @SpNo LIKE 'EOS-SP-%' THEN SUBSTRING(@SpNo,8,16) ELSE RIGHT(@SpNo,15) END;
            DECLARE @Prefix varchar(20), @Balance int, @i int=0, @Serial varchar(24), @ItemId bigint, @Seq int;
            DECLARE @Created table (SerialNo varchar(24) NOT NULL);

            IF @Count NOT BETWEEN 1 AND 50 THROW 51820, 'Label count must be between 1 and 50.', 1;
            SET @Key=REPLACE(@Key,' ','');
            IF @Key='' THROW 51821, 'Spare Part No cannot be converted to a serial prefix.', 1;
            SET @Prefix='SPI-'+@Key+'-';
            SELECT @Balance=OnHandQty FROM dbo.MD_SparePart WHERE SparePartNo=@SpNo AND COALESCE(ActiveFlag,1)=1;
            IF @Balance IS NULL THROW 51815, 'EOS SP No was not found.', 1;

            BEGIN TRANSACTION;
            SELECT @Seq=ISNULL(MAX(TRY_CONVERT(int,SUBSTRING(SerialNo,LEN(@Prefix)+1,4))),0)
            FROM dbo.MNT_SparePartItem WITH (UPDLOCK,HOLDLOCK)
            WHERE SparePartNo=@SpNo AND SerialNo LIKE @Prefix+'[0-9][0-9][0-9][0-9]';
            IF @Seq+@Count>9999 THROW 51822, 'The serial number range for this spare part is exhausted.', 1;
            WHILE @i < @Count
            BEGIN
                SET @Seq+=1;
                SET @Serial=@Prefix+RIGHT('0000'+CONVERT(varchar(4),@Seq),4);
                INSERT dbo.MNT_SparePartItem(SerialNo,SparePartNo,StatusCode,CreatedBy,CreatedTS)
                VALUES(@Serial,@SpNo,'CREATED',LEFT(@User,20),SYSDATETIME());
                SET @ItemId=SCOPE_IDENTITY();
                INSERT dbo.MNT_SparePartsTxn
                    (SparePartNo,SparePartItemID,MoveType,Qty,BalanceBefore,BalanceAfter,RefType,RefID,Note,TxnAt,ActorID,CreatedBy,CreatedTS)
                VALUES(@SpNo,@ItemId,'LABEL',0,@Balance,@Balance,'PDA_LABEL',@Serial,N'Label serial created',SYSDATETIME(),@User,LEFT(@User,20),SYSDATETIME());
                INSERT @Created VALUES(@Serial);
                SET @i+=1;
            END;
            COMMIT;
            SELECT SerialNo FROM @Created ORDER BY SerialNo;
        END;
        """;

    public const string Move = $$"""
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @Serial varchar(24) = UPPER(LTRIM(RTRIM(ISNULL(@SerialNo, ''))));
            DECLARE @SpNo varchar(16);
            DECLARE @ItemId bigint;
            DECLARE @Status varchar(15);
            DECLARE @Move varchar(10) = UPPER(LTRIM(RTRIM(ISNULL(@MoveType, ''))));
            DECLARE @Location varchar(20) = NULLIF(UPPER(LTRIM(RTRIM(@LocationId))), '');
            DECLARE @User nvarchar(450) = COALESCE(NULLIF(LTRIM(RTRIM(@UserId)), N''), N'PDA');
            DECLARE @Before int;
            DECLARE @After int;
            DECLARE @ZoneCode varchar(20);
            DECLARE @Slot varchar(5);
            DECLARE @StoredLocation varchar(20);
            DECLARE @WorkOrder varchar(28)=NULLIF(LTRIM(RTRIM(@WorkOrderNo)),'');
            DECLARE @WorkStatus varchar(15),@Planned bit;
            DECLARE @MoveNote nvarchar(500)=@Note;
            IF @WorkOrder IS NOT NULL SET @MoveNote=LEFT(CONCAT(N'MWO ',@WorkOrder,N' · ',@Note),500);

            IF @Serial = '' THROW 51810, 'Serial No is required.', 1;
            IF @Move NOT IN ('IN', 'OUT') THROW 51811, 'Move type must be IN or OUT.', 1;
            IF COALESCE(@Qty, 0) <> 1 THROW 51812, 'Serialized spare parts must move one item at a time.', 1;
            IF @WorkOrder IS NOT NULL AND @Move<>'OUT'
                THROW 51825, 'A maintenance work order can only be linked to spare parts release.', 1;
            BEGIN TRANSACTION;

            -- Same lock order as maintenance completion: work order before spare-part stock.
            IF @WorkOrder IS NOT NULL
            BEGIN
                SELECT @WorkStatus=w.Status,@Planned=CASE WHEN {{PmRules.PlannedWoSql}} THEN 1 ELSE 0 END
                FROM dbo.MNT_WorkOrder w WITH (UPDLOCK,HOLDLOCK) WHERE w.WoNumber=@WorkOrder;
                IF @WorkStatus IS NULL THROW 51826, 'Maintenance work order was not found.', 1;
                IF @WorkStatus NOT IN ('ISSUED','ASSIGNED','IN_PROGRESS')
                    THROW 51827, 'This work order is completed or closed. Select an open work order.', 1;
                IF @Planned=1 THROW 51828, 'This PM work order is not due yet. Select a due or in-progress work order.', 1;
            END;

            SELECT @ItemId=I.SparePartItemID,@SpNo=I.SparePartNo,@Status=I.StatusCode,
                   @StoredLocation=I.LocationID,@ZoneCode=P.ZoneCode,@Slot=P.Slot
            FROM dbo.MNT_SparePartItem I WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.MD_SparePart P WITH (UPDLOCK,HOLDLOCK) ON P.SparePartNo=I.SparePartNo AND COALESCE(P.ActiveFlag,1)=1
            WHERE I.SerialNo=@Serial;
            IF @ItemId IS NULL THROW 51815, 'Serial No was not found.', 1;
            IF @Move='IN' AND @Status<>'CREATED' THROW 51818, 'This serial has already been received.', 1;
            IF @Move='OUT' AND @Status<>'IN_STOCK' THROW 51817, 'This serial has not been received or is no longer in stock.', 1;
            IF @Move='IN' AND NULLIF(@ZoneCode,'') IS NULL
                THROW 51813, 'Storage location is not configured in Spare Part Master.', 1;

            SET @Location = CASE
                WHEN @Move='OUT' THEN @StoredLocation
                WHEN @ZoneCode='SP_EXTRA' THEN 'SP-EXTRA'
                WHEN NULLIF(@Slot,'') IS NULL OR @Slot='EX' THEN REPLACE(@ZoneCode,'_','-')
                ELSE CONCAT(REPLACE(@ZoneCode,'_','-'),'-',@Slot)
            END;

            SELECT @Before=OnHandQty FROM dbo.MD_SparePart WHERE SparePartNo=@SpNo;

            SET @After = @Before + CASE WHEN @Move = 'IN' THEN @Qty ELSE -@Qty END;
            IF @After < 0 THROW 51816, 'Spare part is out of stock.', 1;

            UPDATE dbo.MD_SparePart
               SET OnHandQty = @After,
                   ModifiedBy = @User,
                   ModifiedTS = SYSDATETIME()
             WHERE SparePartNo = @SpNo;

            UPDATE dbo.MNT_SparePartItem
               SET StatusCode=CASE WHEN @Move='IN' THEN 'IN_STOCK' ELSE 'RELEASED' END,
                   LocationID=CASE WHEN @Move='IN' THEN @Location ELSE LocationID END,
                   ModifiedBy=@User,ModifiedTS=SYSDATETIME()
             WHERE SparePartItemID=@ItemId;

            INSERT INTO dbo.MNT_SparePartsTxn
                (SparePartNo, SparePartItemID, MoveType, Qty, BalanceBefore, BalanceAfter, RefType, RefID,
                 Note, TxnAt, ActorID, CreatedBy, CreatedTS)
            VALUES
                (@SpNo, @ItemId, @Move, 1, @Before, @After, CASE WHEN @WorkOrder IS NULL THEN 'PDA' ELSE 'MWO' END,
                 COALESCE(@WorkOrder,@Location,(SELECT LocationID FROM dbo.MNT_SparePartItem WHERE SparePartItemID=@ItemId)),
                 @MoveNote, SYSDATETIME(), @User, LEFT(@User, 20), SYSDATETIME());

            IF OBJECT_ID(N'dbo.SYS_AuditLog',N'U') IS NOT NULL
                INSERT dbo.SYS_AuditLog(ActorUserID,ModuleCode,ProcessCode,ScreenCode,ActionType,TargetEntity,TargetID,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
                VALUES(@User,'SP','PDA',CASE WHEN @Move='IN' THEN 'SP-02' ELSE 'SP-07' END,@Move,'MNT_SparePartItem',@Serial,
                       CONCAT(N'{"status":"',CASE WHEN @Move='IN' THEN 'IN_STOCK' ELSE 'RELEASED' END,N'","location":"',COALESCE(@Location,N''),N'"}'),
                       'SUCCESS',@MoveNote,LEFT(@User,20),SYSDATETIME());

            COMMIT TRANSACTION;
        END;
        """;

    public const string Cancel = """
        SET QUOTED_IDENTIFIER ON;
        SET ANSI_NULLS ON;
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @Serial varchar(24)=UPPER(LTRIM(RTRIM(ISNULL(@SerialNo,''))));
            DECLARE @Move varchar(10)=UPPER(LTRIM(RTRIM(ISNULL(@MoveType,''))));
            DECLARE @User nvarchar(450)=COALESCE(NULLIF(LTRIM(RTRIM(@UserId)),N''),N'PDA');
            DECLARE @ItemId bigint,@SpNo varchar(16),@Status varchar(15),@Location varchar(20),@OriginalTxn int,@Before int,@After int;
            DECLARE @ExpectedTxn int,@RefType varchar(20),@WorkOrder varchar(28),@WorkStatus varchar(15);
            DECLARE @CancelNote nvarchar(500)=@Note;
            IF @Move NOT IN ('IN','OUT') THROW 51821, 'Cancellation type must be IN or OUT.', 1;

            BEGIN TRANSACTION;
            -- Discover the reference without locking inventory, then lock WO first and recheck the transaction.
            SELECT TOP (1) @ExpectedTxn=T.SparePartsTxnID,@RefType=T.RefType,@WorkOrder=T.RefID
            FROM dbo.MNT_SparePartsTxn T JOIN dbo.MNT_SparePartItem I ON I.SparePartItemID=T.SparePartItemID
            WHERE I.SerialNo=@Serial AND T.MoveType=@Move
              AND NOT EXISTS(SELECT 1 FROM dbo.MNT_SparePartsTxn R WHERE R.ReversalOfTxnID=T.SparePartsTxnID)
            ORDER BY T.SparePartsTxnID DESC;
            IF @TransactionId IS NOT NULL AND (@ExpectedTxn IS NULL OR @ExpectedTxn<>@TransactionId)
                THROW 51830, 'This transaction is no longer cancellable. Refresh the list and select the latest transaction.', 1;
            IF @RefType='MWO'
            BEGIN
                SET @CancelNote=LEFT(CONCAT(N'MWO ',@WorkOrder,N' · ',@Note),500);
                SELECT @WorkStatus=Status FROM dbo.MNT_WorkOrder WITH (UPDLOCK,HOLDLOCK) WHERE WoNumber=@WorkOrder;
                IF @WorkStatus IS NULL OR @WorkStatus NOT IN ('ISSUED','ASSIGNED','IN_PROGRESS')
                    THROW 51829, 'Release cannot be cancelled after its maintenance work order is completed or closed.', 1;
            END;

            SELECT @ItemId=SparePartItemID,@SpNo=SparePartNo,@Status=StatusCode,@Location=LocationID
            FROM dbo.MNT_SparePartItem WITH (UPDLOCK,HOLDLOCK) WHERE SerialNo=@Serial;
            IF @ItemId IS NULL THROW 51815, 'Serial No was not found.', 1;
            IF @Move='IN' AND @Status<>'IN_STOCK' THROW 51822, 'Only an in-stock serial can cancel inbound.', 1;
            IF @Move='OUT' AND @Status<>'RELEASED' THROW 51823, 'Only a released serial can cancel release.', 1;

            SELECT TOP (1) @OriginalTxn=T.SparePartsTxnID
            FROM dbo.MNT_SparePartsTxn T
            WHERE T.SparePartItemID=@ItemId AND T.MoveType=@Move
              AND NOT EXISTS(SELECT 1 FROM dbo.MNT_SparePartsTxn R WHERE R.ReversalOfTxnID=T.SparePartsTxnID)
            ORDER BY T.SparePartsTxnID DESC;
            IF @OriginalTxn IS NULL THROW 51824, 'A cancellable transaction was not found.', 1;
            IF @OriginalTxn<>@ExpectedTxn OR @ExpectedTxn IS NULL
                THROW 51830, 'This serial transaction changed. Scan it again before cancelling.', 1;

            SELECT @Before=OnHandQty FROM dbo.MD_SparePart WITH (UPDLOCK,HOLDLOCK) WHERE SparePartNo=@SpNo;
            SET @After=@Before+CASE WHEN @Move='IN' THEN -1 ELSE 1 END;
            IF @After<0 THROW 51816, 'Spare part stock would become negative.', 1;
            UPDATE dbo.MD_SparePart SET OnHandQty=@After,ModifiedBy=@User,ModifiedTS=SYSDATETIME() WHERE SparePartNo=@SpNo;
            UPDATE dbo.MNT_SparePartItem
               SET StatusCode=CASE WHEN @Move='IN' THEN 'CREATED' ELSE 'IN_STOCK' END,
                   LocationID=CASE WHEN @Move='IN' THEN NULL ELSE LocationID END,
                   ModifiedBy=@User,ModifiedTS=SYSDATETIME()
             WHERE SparePartItemID=@ItemId;

            INSERT dbo.MNT_SparePartsTxn
                (SparePartNo,SparePartItemID,MoveType,Qty,BalanceBefore,BalanceAfter,RefType,RefID,Note,TxnAt,ActorID,CreatedBy,CreatedTS,ReversalOfTxnID)
            VALUES(@SpNo,@ItemId,CONCAT(@Move,'_CANCEL'),1,@Before,@After,'PDA_CANCEL',@Location,@CancelNote,SYSDATETIME(),@User,LEFT(@User,20),SYSDATETIME(),@OriginalTxn);

            IF OBJECT_ID(N'dbo.SYS_AuditLog',N'U') IS NOT NULL
                INSERT dbo.SYS_AuditLog(ActorUserID,ModuleCode,ProcessCode,ScreenCode,ActionType,TargetEntity,TargetID,AfterValueJSON,Result,Note,CreatedBy,CreatedTS)
                VALUES(@User,'SP','PDA',CASE WHEN @Move='IN' THEN 'SP-02' ELSE 'SP-07' END,CONCAT(@Move,'_CANCEL'),'MNT_SparePartItem',@Serial,
                       CONCAT(N'{"status":"',CASE WHEN @Move='IN' THEN 'CREATED' ELSE 'IN_STOCK' END,N'","reversalOf":',@OriginalTxn,N'}'),
                       'SUCCESS',@CancelNote,LEFT(@User,20),SYSDATETIME());
            COMMIT;
        END;
        """;

}
