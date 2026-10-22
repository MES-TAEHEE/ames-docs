using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    // WH_AREA Attribute1 = location prefix, Attribute2 = GRID / OPEN, fixed at creation.
    public void CreateLocationMapArea(string whCode, string areaCode, string? name, string mode)
    {
        if (string.IsNullOrWhiteSpace(whCode) || whCode.Length > 20
            || !LocationMapNaming.ValidPrefix(areaCode) || name?.Length > 60 || mode is not ("GRID" or "OPEN"))
            throw new ArgumentException("Select a warehouse, a two-letter area code and a valid storage mode.");
        name = string.IsNullOrWhiteSpace(name) ? areaCode : name.Trim();

        using var conn = _factory.OpenConnection();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);
        using var cmd = new SqlCommand("""
            DECLARE @WarehouseID nvarchar(50);
            SELECT @WarehouseID=CodeID FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK)
            WHERE GroupCode='WH_CODE' AND CodeValue=@Wh AND COALESCE(UseFlag,1)=1;
            IF @WarehouseID IS NULL THROW 51000, 'Warehouse does not exist.', 1;
            IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK) WHERE GroupCode='WH_AREA' AND CodeValue=@Area)
                THROW 51002, 'Area already exists. Storage mode can only be set when creating a new area.', 1;
            IF EXISTS (SELECT 1 FROM dbo.MD_CodeItem WITH (UPDLOCK,HOLDLOCK) WHERE GroupCode='WH_AREA' AND Attribute1=@Area)
                THROW 51001, 'Area prefix is already in use.', 1;
            IF EXISTS (SELECT 1 FROM dbo.MD_Location WITH (UPDLOCK,HOLDLOCK) WHERE AreaCode=@Area)
                THROW 51002, 'Locations already use this area code. Choose a new area code.', 1;

            INSERT dbo.MD_CodeItem
                (CodeID,GroupCode,CodeValue,CodeName,ParentCodeID,Attribute1,Attribute2,SortOrder,UseFlag,CreatedBy,CreatedTS)
            VALUES (CONCAT('WH_AREA_',@Area),'WH_AREA',@Area,@Name,@WarehouseID,@Area,@Mode,100,1,@Actor,SYSDATETIME());

            DECLARE @Parent varchar(20)=CONCAT(@Area,'AREA'),
                    @Location varchar(20)=CASE WHEN @Mode='OPEN' THEN CONCAT(@Area,'OPEN') ELSE @FirstLocation END;
            IF EXISTS (SELECT 1 FROM dbo.MD_Location WITH (UPDLOCK,HOLDLOCK) WHERE LocationID IN (@Parent,@Location))
                THROW 51000, 'The generated location code is already in use.', 1;
            INSERT dbo.MD_Location
                (LocationID,LocationName,WhCode,AreaCode,LocationLevel,ActiveFlag,CreatedBy)
            VALUES (@Parent,@Name,@Wh,@Area,1,1,@Actor);
            IF @Mode='OPEN'
            BEGIN
                INSERT dbo.MD_Location
                    (LocationID,LocationName,WhCode,AreaCode,LocationLevel,ParentLocationID,ActiveFlag,CreatedBy)
                VALUES (@Location,LEFT(CONCAT(@Name,' - Open Storage'),60),@Wh,@Area,2,@Parent,1,@Actor);
            END;
            IF @Mode='GRID'
            BEGIN
                INSERT dbo.MD_Location
                    (LocationID,LocationName,WhCode,AreaCode,Aisle,Bay,Slot,LocationLevel,ParentLocationID,ActiveFlag,CreatedBy)
                VALUES (@FirstLocation,@FirstLocation,@Wh,@Area,'A1','01','F1',2,@Parent,1,@Actor);
            END;
            """, conn, tx);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
        cmd.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
        cmd.Parameters.Add("@Mode", SqlDbType.NVarChar, 200).Value = mode;
        cmd.Parameters.Add("@FirstLocation", SqlDbType.VarChar, 20).Value = LocationMapNaming.LocationNo(areaCode, "01", "A1", "F1");
        ColumnParams.Add(cmd, "@Name", "MD_CodeItem.CodeName", name);
        ColumnParams.Add(cmd, "@Actor", "MD_Location.CreatedBy", _actor());
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void RenameLocationMapArea(string whCode, string areaCode, string name)
    {
        if (string.IsNullOrWhiteSpace(whCode) || whCode.Length > 20
            || string.IsNullOrWhiteSpace(areaCode) || areaCode.Length > 20
            || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60)
            throw new ArgumentException("Enter an area name containing 1 to 60 characters.");

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE A SET CodeName=@Name,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            FROM dbo.MD_CodeItem A
            JOIN dbo.MD_CodeItem W ON W.CodeID=A.ParentCodeID AND W.GroupCode='WH_CODE'
            WHERE A.GroupCode='WH_AREA' AND A.CodeValue=@Area AND W.CodeValue=@Wh
              AND COALESCE(A.UseFlag,1)=1 AND COALESCE(W.UseFlag,1)=1;
            IF @@ROWCOUNT=0 THROW 51003, 'Area is no longer available in this warehouse. Refresh the list.', 1;
            """, conn);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
        cmd.Parameters.Add("@Area", SqlDbType.VarChar, 20).Value = areaCode;
        ColumnParams.Add(cmd, "@Name", "MD_CodeItem.CodeName", name.Trim());
        ColumnParams.Add(cmd, "@Actor", "MD_Location.CreatedBy", _actor());
        cmd.ExecuteNonQuery();
    }

    public void RenameLocationMapWarehouse(string whCode, string name)
    {
        if (string.IsNullOrWhiteSpace(whCode) || whCode.Length > 20
            || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60)
            throw new ArgumentException("Enter a warehouse name containing 1 to 60 characters.");

        using var conn = _factory.OpenConnection();
        using var cmd = new SqlCommand("""
            UPDATE dbo.MD_CodeItem SET CodeName=@Name,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            WHERE GroupCode='WH_CODE' AND CodeValue=@Wh AND COALESCE(UseFlag,1)=1;
            IF @@ROWCOUNT=0 THROW 51000, 'Warehouse is no longer available. Refresh the list.', 1;
            """, conn);
        cmd.Parameters.Add("@Wh", SqlDbType.VarChar, 20).Value = whCode;
        ColumnParams.Add(cmd, "@Name", "MD_CodeItem.CodeName", name.Trim());
        ColumnParams.Add(cmd, "@Actor", "MD_Location.CreatedBy", _actor());
        cmd.ExecuteNonQuery();
    }
}
