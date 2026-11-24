using System.Data;
using System.Text.Json;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed partial class WarehouseRepository
{
    public sealed record LocationGridSetup(string ParentId,string Name,string Warehouse,string Area,string Prefix,
        int Columns,int Rows,int Floors,string LocationType="RACK");
    public sealed record LocationGridCell(string Id,string X,string Y,string F);

    public static List<LocationGridCell> PlanLocationGrid(string prefix,int columns,int rows,int floors,bool single=false)
    {
        if (prefix is not {Length:2} || !prefix.All(c=>char.IsAsciiLetterUpper(c)||char.IsAsciiDigit(c))
            || columns is <1 or >99 || rows is <1 or >260 || floors is <1 or >99
            || !single && (long)columns*rows*floors>1000)
            throw new ArgumentException("Use a two-character prefix, X 1–99, Y 1–260, F 1–99 and at most 1,000 detail locations.");
        return (from f in single ? [floors] : Enumerable.Range(1,floors)
                from y in single ? [rows] : Enumerable.Range(1,rows)
                from x in single ? [columns] : Enumerable.Range(1,columns)
                select new LocationGridCell(LocationMapNaming.LocationNo(prefix,x.ToString("D2"),LocationMapNaming.YCode(y),$"F{f}"),
                    x.ToString("D2"),LocationMapNaming.YCode(y),$"F{f}")).ToList();
    }

    public void SaveLocationGrid(LocationGridSetup setup,string actor,bool create=false,bool single=false)
    {
        PlanLocationGrid(string.IsNullOrEmpty(setup.Prefix)?"AA":setup.Prefix,setup.Columns,setup.Rows,setup.Floors,single);
        if (string.IsNullOrWhiteSpace(setup.ParentId) || setup.ParentId.Length>20 || string.IsNullOrWhiteSpace(setup.Name)
            || setup.Name.Length>60 || string.IsNullOrWhiteSpace(setup.Warehouse) || setup.Warehouse.Length>20
            || string.IsNullOrWhiteSpace(setup.Area) || setup.Area.Length>20 || setup.LocationType.Length>20
            || create && setup.LocationType is not ("RACK" or "FG" or "LINE")
            || string.IsNullOrWhiteSpace(actor) || actor.Length>20 || create && single)
            throw new ArgumentException("Select a warehouse and area, and enter a parent location code and name.");
        using var conn=_factory.OpenConnection();
        using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
        LockLinePicking(conn,tx);
        if(string.IsNullOrEmpty(setup.Prefix)) setup=setup with {Prefix=AllocateLocationPrefix(conn,tx,setup.Area,setup.ParentId)};
        ExecuteLocationGrid(conn,tx,setup,actor,create,single);
        tx.Commit();
    }

    public LocationGridSetup CreateLocationGrid(string name,string warehouse,string area,int columns,int rows,int floors,string type,string actor)
    {
        PlanLocationGrid("AA",columns,rows,floors);
        if(string.IsNullOrWhiteSpace(name) || name.Length>60 || string.IsNullOrWhiteSpace(actor) || actor.Length>20
            || string.IsNullOrWhiteSpace(warehouse) || warehouse.Length>20 || string.IsNullOrWhiteSpace(area) || area.Length>20
            || type is not("RACK" or "FG" or "LINE")) throw new ArgumentException("Select a warehouse, area and location name.");
        using var conn=_factory.OpenConnection();
        using var tx=conn.BeginTransaction(IsolationLevel.Serializable);
        LockLinePicking(conn,tx);
        var prefix=AllocateLocationPrefix(conn,tx,area,null);
        var setup=new LocationGridSetup(prefix+"AREA",name,warehouse,area,prefix,columns,rows,floors,type);
        ExecuteLocationGrid(conn,tx,setup,actor,true,false);
        tx.Commit();
        return setup;
    }

    static string AllocateLocationPrefix(SqlConnection conn,SqlTransaction tx,string area,string? parent)
    {
        using var cmd=new SqlCommand("""
            SELECT LEFT(LocationID,2) FROM dbo.MD_Location WITH(UPDLOCK,HOLDLOCK) WHERE @Parent IS NULL OR LocationID<>@Parent
            UNION SELECT LotPrefix FROM dbo.MD_Line WITH(HOLDLOCK) WHERE LotPrefix IS NOT NULL AND LotPrefix<>@Area
            UNION SELECT Attribute1 FROM dbo.MD_CodeItem WITH(HOLDLOCK) WHERE GroupCode='WH_AREA' AND CodeValue<>@Area AND LEN(Attribute1)=2;
            SELECT COALESCE(NULLIF(Attribute1,''),LEFT(CodeValue,2)) FROM dbo.MD_CodeItem WITH(UPDLOCK,HOLDLOCK)
                WHERE GroupCode='WH_AREA' AND CodeValue=@Area;
            """,conn,tx);
        cmd.Parameters.Add("@Area",SqlDbType.VarChar,20).Value=area;
        cmd.Parameters.Add("@Parent",SqlDbType.VarChar,20).Value=(object?)parent??DBNull.Value;
        using var reader=cmd.ExecuteReader();
        var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(reader.Read()) if(!reader.IsDBNull(0)) used.Add(reader.GetString(0));
        reader.NextResult();
        var suggested=reader.Read() && !reader.IsDBNull(0) ? reader.GetString(0) : "";
        return AvailableLocationPrefix(suggested,used);
    }

    public static string AvailableLocationPrefix(string suggested,IReadOnlySet<string> used)
    {
        if(suggested.Length==2 && suggested.All(c=>char.IsAsciiLetterUpper(c)||char.IsAsciiDigit(c)) && !used.Contains(suggested)) return suggested;
        const string chars="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        foreach(var a in chars) foreach(var b in chars)
        {
            var code=$"{a}{b}";
            if(!used.Contains(code)) return code;
        }
        throw new InvalidOperationException("No location barcode prefix is available.");
    }

    static void ExecuteLocationGrid(SqlConnection conn,SqlTransaction tx,LocationGridSetup setup,string actor,bool create,bool single)
    {
        var cells=PlanLocationGrid(setup.Prefix,setup.Columns,setup.Rows,setup.Floors,single);
        using var cmd=new SqlCommand(LocationGridSaveSql,conn,tx);
        ColumnParams.Add(cmd,"@Parent","MD_Location.LocationID",setup.ParentId);
        ColumnParams.Add(cmd,"@Name","MD_Location.LocationName",setup.Name.Trim());
        ColumnParams.Add(cmd,"@Wh","MD_Location.WhCode",setup.Warehouse);
        ColumnParams.Add(cmd,"@Area","MD_Location.AreaCode",setup.Area);
        ColumnParams.Add(cmd,"@Actor","MD_Location.CreatedBy",actor);
        cmd.Parameters.Add("@Prefix",SqlDbType.VarChar,2).Value=setup.Prefix;
        cmd.Parameters.Add("@Type",SqlDbType.VarChar,20).Value=setup.LocationType;
        cmd.Parameters.Add("@WantedJson",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(cells);
        cmd.Parameters.Add("@Columns",SqlDbType.Int).Value=setup.Columns;
        cmd.Parameters.Add("@Rows",SqlDbType.Int).Value=setup.Rows;
        cmd.Parameters.Add("@Floors",SqlDbType.Int).Value=setup.Floors;
        cmd.Parameters.Add("@Create",SqlDbType.Bit).Value=create;
        cmd.Parameters.Add("@Single",SqlDbType.Bit).Value=single;
        cmd.ExecuteNonQuery();
    }

    internal const string LocationGridSaveSql="""
        SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; SET XACT_ABORT ON;
        DECLARE @Level tinyint,@Active bit,@ParentWh varchar(20),@ParentArea varchar(20),@Plant varchar(20),@Line varchar(20);
        SELECT @Level=LocationLevel,@Active=ActiveFlag,@ParentWh=WhCode,@ParentArea=AreaCode,@Plant=PlantCode,@Line=LineID
        FROM dbo.MD_Location WITH(UPDLOCK,HOLDLOCK) WHERE LocationID=@Parent;
        IF @Create=1 AND @Level IS NOT NULL THROW 52080,'The parent location code already exists.',1;
        IF @Create=0 AND (@Level IS NULL OR @Level<>1 OR @Active=0 OR @ParentWh<>@Wh OR @ParentArea<>@Area)
            THROW 52081,'The parent location changed or is inactive. Refresh the list.',1;
        IF NOT EXISTS(SELECT 1 FROM dbo.MD_CodeItem A WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.MD_CodeItem W WITH(HOLDLOCK) ON W.CodeID=A.ParentCodeID AND W.GroupCode='WH_CODE'
            WHERE A.GroupCode='WH_AREA' AND A.CodeValue=@Area AND W.CodeValue=@Wh AND COALESCE(A.UseFlag,1)=1
                AND COALESCE(W.UseFlag,1)=1 AND COALESCE(A.Attribute2,'GRID')<>'OPEN')
            THROW 52082,'Select an active Grid area in this warehouse. Open Storage does not use grid coordinates.',1;
        IF EXISTS(SELECT 1 FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK) WHERE LocationNo=@Parent AND Qty<>0)
            THROW 52075,'A location that holds stock cannot be a parent.',1;
        IF @Create=1
        BEGIN
            SELECT @Line=MIN(LineID) FROM dbo.MD_Line WITH(HOLDLOCK) WHERE LotPrefix=@Area AND @Wh='EOS';
            IF @Line IS NOT NULL AND @Prefix<>@Area THROW 52082,'Line locations must use the registered line prefix.',1;
            IF @Line IS NOT NULL SET @Type='LINE';
            IF UPPER(@Area)='FG_AREA' SET @Type='FG';
            INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationLevel,LocationType,LineID,ActiveFlag,CreatedBy)
            VALUES(@Parent,@Name,@Wh,@Area,1,@Type,@Line,1,@Actor);
        END;
        SELECT @Type=COALESCE(LocationType,CASE WHEN UPPER(AreaCode)='FG_AREA' THEN 'FG' ELSE @Type END)
            FROM dbo.MD_Location WHERE LocationID=@Parent;
        DECLARE @Wanted TABLE(Id varchar(20) PRIMARY KEY,X varchar(5),Y varchar(5),F varchar(5));
        INSERT @Wanted SELECT Id,X,Y,F FROM OPENJSON(@WantedJson)
            WITH(Id varchar(20),X varchar(5),Y varchar(5),F varchar(5));
        IF EXISTS(SELECT 1 FROM @Wanted W JOIN dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK) ON L.LocationID=W.Id
            WHERE ISNULL(L.ParentLocationID,'')<>@Parent OR L.LocationLevel<>2 OR L.WhCode<>@Wh OR L.AreaCode<>@Area
                OR ISNULL(L.Bay,'')<>W.X OR ISNULL(L.Aisle,'')<>W.Y OR ISNULL(L.Slot,'')<>W.F)
            THROW 52083,'A generated barcode belongs to another location. Use a different prefix; existing barcodes were not changed.',1;
        IF @Single=1 AND EXISTS(SELECT 1 FROM @Wanted W JOIN dbo.MD_Location L ON L.LocationID=W.Id AND L.ActiveFlag=1)
            THROW 52083,'This detail location already exists.',1;
        IF EXISTS(SELECT 1 FROM dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK) WHERE L.ParentLocationID=@Parent
            AND (L.LocationLevel<>2 OR L.WhCode<>@Wh OR L.AreaCode<>@Area
                OR ISNULL(L.LocationID,'')<>CONCAT(@Prefix,L.Bay,L.Aisle,L.Slot)
                OR NULLIF(L.Bay,'') IS NULL OR NULLIF(L.Aisle,'') IS NULL OR NULLIF(L.Slot,'') IS NULL))
            THROW 52084,'Some children use custom barcodes or coordinates. They must be reviewed before changing this grid.',1;
        DECLARE @Removing TABLE(Id varchar(20) PRIMARY KEY);
        IF @Single=0 INSERT @Removing SELECT L.LocationID FROM dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK)
            WHERE L.ParentLocationID=@Parent AND L.ActiveFlag=1 AND NOT EXISTS(SELECT 1 FROM @Wanted W WHERE W.Id=L.LocationID);
        IF EXISTS(SELECT 1 FROM @Removing R JOIN dbo.WH_Inventory I WITH(UPDLOCK,HOLDLOCK) ON I.LocationNo=R.Id WHERE I.Qty<>0)
            THROW 52085,'Cannot reduce the grid: a removed location contains stock. Move its stock first.',1;
        IF EXISTS(SELECT 1 FROM @Removing R JOIN dbo.MNT_SparePartItem S WITH(UPDLOCK,HOLDLOCK) ON S.LocationID=R.Id)
            THROW 52085,'Cannot reduce the grid: a removed location has linked spare parts.',1;
        IF EXISTS(SELECT 1 FROM @Removing R JOIN dbo.WH_PickSlip P WITH(UPDLOCK,HOLDLOCK) ON P.ReqLocation=R.Id
            WHERE P.CloseDate IS NULL AND UPPER(COALESCE(P.Status,'OPEN')) NOT IN('CLOSED','RELEASED','CANCELLED','CANCELED'))
            THROW 52086,'Cannot reduce the grid: a removed location has outstanding picking orders.',1;
        IF EXISTS(SELECT 1 FROM @Removing R JOIN dbo.MD_Location L ON L.LocationID=R.Id
            WHERE NULLIF(L.PartNo,'') IS NOT NULL OR COALESCE(L.MinimumQty,0)>0)
            THROW 52087,'Clear assigned parts and minimum quantities before reducing the grid.',1;
        IF OBJECT_ID(N'dbo.MD_LocationPart',N'U') IS NOT NULL
        BEGIN
            DECLARE @RemovedJson nvarchar(max)=(SELECT Id FROM @Removing FOR JSON PATH);
            EXEC sys.sp_executesql N'IF EXISTS(SELECT 1 FROM dbo.MD_LocationPart P WITH(UPDLOCK,HOLDLOCK)
                JOIN OPENJSON(@Ids) WITH(Id varchar(20)) R ON R.Id=P.LocationID)
                THROW 52087,''Clear assigned parts and minimum quantities before reducing the grid.'',1;',
                N'@Ids nvarchar(max)',@RemovedJson;
        END;
        -- Keep used barcodes for history; only empty, never-used locations can be physically removed.
        DELETE L FROM dbo.MD_Location L JOIN @Removing R ON R.Id=L.LocationID
            WHERE NOT EXISTS(SELECT 1 FROM dbo.WH_Inventory I WITH(HOLDLOCK) WHERE I.LocationNo=L.LocationID)
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_InventoryTransaction T WITH(HOLDLOCK) WHERE T.LocationNo=L.LocationID)
              AND NOT EXISTS(SELECT 1 FROM dbo.WH_PickSlip P WITH(HOLDLOCK) WHERE P.ReqLocation=L.LocationID);
        UPDATE L SET ActiveFlag=0,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            FROM dbo.MD_Location L JOIN @Removing R ON R.Id=L.LocationID;
        UPDATE L SET ActiveFlag=1,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()
            FROM dbo.MD_Location L JOIN @Wanted W ON W.Id=L.LocationID WHERE L.ActiveFlag=0;
        INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,Aisle,Bay,Slot,PlantCode,LocationType,LineID,
            ParentLocationID,LocationLevel,ActiveFlag,CreatedBy)
        SELECT W.Id,W.Id,@Wh,@Area,W.Y,W.X,W.F,@Plant,@Type,@Line,@Parent,2,1,@Actor
            FROM @Wanted W WHERE NOT EXISTS(SELECT 1 FROM dbo.MD_Location L WITH(UPDLOCK,HOLDLOCK) WHERE L.LocationID=W.Id);
        -- Child-only additions extend the parent's bounds without filling other empty coordinates.
        DECLARE @MaxX int=@Columns,@MaxY int=@Rows,@MaxF int=@Floors;
        IF @Single=1
        BEGIN
            SELECT @MaxX=MAX(TRY_CONVERT(int,Bay)),@MaxF=MAX(TRY_CONVERT(int,SUBSTRING(Slot,2,4))),
                @MaxY=MAX((TRY_CONVERT(int,SUBSTRING(Aisle,2,4))-1)*26+ASCII(LEFT(Aisle,1))-ASCII('A')+1)
            FROM dbo.MD_Location WHERE ParentLocationID=@Parent AND ActiveFlag=1;
        END;
        UPDATE dbo.MD_Location SET LocationName=@Name,Bay=RIGHT(CONCAT('0',@MaxX),2),
            Aisle=CONCAT(CHAR(ASCII('A')+(@MaxY-1)%26),(@MaxY-1)/26+1),Slot=CONCAT('F',@MaxF),
            ModifiedBy=@Actor,ModifiedTS=SYSDATETIME() WHERE LocationID=@Parent;
        """;
}
