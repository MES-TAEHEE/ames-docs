using System.Data;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

public sealed class PdaMaintenanceRepository(AmesConnectionFactory factory)
{
    public PdaMntAccess Access(string employeeNo)
    {
        var rows=Query("""
            SELECT P.UserID,R.Name RoleName,S.ScreenCode,RP.PermissionLevel
            FROM dbo.SYS_UserProfile P JOIN dbo.AspNetUsers U ON U.Id=P.UserID
            LEFT JOIN dbo.AspNetUserRoles UR ON UR.UserId=P.UserID
            LEFT JOIN dbo.AspNetRoles R ON R.Id=UR.RoleId
            LEFT JOIN dbo.SYS_RolePermission RP ON RP.RoleID=R.Id
                OR (NULLIF(RP.RoleID,'') IS NULL AND RP.RoleName=R.Name)
            LEFT JOIN dbo.SYS_Screen S ON S.ScreenCode=RP.ScreenCode AND ISNULL(S.IsVisible,1)=1
            WHERE P.EmployeeNo=@Employee AND P.AccountStatus='ACTIVE'
              AND (U.LockoutEnd IS NULL OR U.LockoutEnd<=SYSDATETIME());
            """,r=>(Id:Convert.ToString(r["UserID"]),Role:Convert.ToString(r["RoleName"]),
                Screen:Convert.ToString(r["ScreenCode"]),Level:Convert.ToString(r["PermissionLevel"])),
            ("@Employee",SqlDbType.VarChar,20,employeeNo));
        var admin=rows.Any(r=>string.Equals(r.Role,"Admin",StringComparison.OrdinalIgnoreCase));
        bool Has(string screen,char letter)=>admin||rows.Any(r=>r.Screen==screen&&(r.Level??"").Contains(letter));
        return new(rows.FirstOrDefault().Id,admin,
            Has("MNT-007",'R')||Has("MNT-005",'R')||Has("MNT-010",'R'),
            Has("MNT-007",'E'),Has("MNT-007",'A'),
            Has("MNT-007",'R'),Has("MNT-005",'R'),Has("MNT-010",'R'));
    }

    public List<PdaMntSchedule> Schedules(DateTime from,DateTime to,DateTime today,int leadDays,string? techId)
        =>Query("""
            SELECT P.PMScheduleID,P.PMPlanNumber,P.EquipID,E.EquipName,P.PMClass,P.PMType,P.CycleBasis,P.CycleValue,
                P.NextDueDate,P.ChecklistID,P.AssignedTechID,P.Status,P.ActiveWoID,W.Status WorkStatus,P.ModifiedTS
            FROM dbo.MNT_PMSchedule P LEFT JOIN dbo.MD_Equipment E ON E.EquipID=P.EquipID
            LEFT JOIN dbo.MNT_WorkOrder W ON W.WorkOrderID=P.ActiveWoID
            WHERE ISNULL(P.Status,'OK')<>'CANCELLED'
              AND (P.NextDueDate BETWEEN @From AND @To OR P.NextDueDate<@From)
              AND (@Tech IS NULL OR P.AssignedTechID=@Tech)
            ORDER BY P.NextDueDate,P.PMScheduleID;
            """,r=>new PdaMntSchedule((int)r["PMScheduleID"],Text(r,"PMPlanNumber"),Text(r,"EquipID"),Text(r,"EquipName"),
                Text(r,"PMClass"),Text(r,"PMType"),NullableText(r,"CycleBasis"),r["CycleValue"] as int?,r["NextDueDate"] as DateTime?,
                NullableText(r,"ChecklistID"),NullableText(r,"AssignedTechID"),
                PmRules.DisplayStatus(NullableText(r,"Status"),r["NextDueDate"] as DateTime?,today,leadDays),
                r["ActiveWoID"] as int?,Text(r,"WorkStatus")=="IN_PROGRESS",r["ModifiedTS"] as DateTime?),
            ("@From",SqlDbType.Date,0,from),("@To",SqlDbType.Date,0,to),("@Tech",SqlDbType.NVarChar,450,techId));

    public bool RequiresLoto(string? checklistId)=>Query("SELECT SafetyLOTOFlag FROM dbo.MD_PmTemplate WHERE PMTemplateID=@Id;",
        r=>r[0] is true,("@Id",SqlDbType.VarChar,20,checklistId)).FirstOrDefault();

    public PdaMntWork Work(MntRepository.MwoRow w,bool? requiresLoto=null)=>new(w.WorkOrderId,w.WoNumber??$"MWO #{w.WorkOrderId}",w.EquipId,w.EquipName,
        w.AssignedTechId,w.Status,w.ActionDesc,w.PmClass,w.PmDueDate,w.TaskCount,w.TaskDone,w.TaskFail,w.PmNeedsNextDue,
        requiresLoto??RequiresLoto(w.ChecklistId),w.StartedAt);

    public PdaMntDetail Detail(MntRepository repo,MntRepository.MwoRow work)=>new(Work(work),repo.ListWorkOrderTasks(work.WorkOrderId)
        .Select(t=>new PdaMntTask(t.WorkOrderTaskId,t.WorkOrderId,t.TaskSeq,t.TaskName,t.AcceptanceCriteria,
            t.RequiredSparePartNo,t.PartName,t.RequiredQty,t.StepDurationMin,t.Result,t.Note,t.CompletedBy,t.CompletedAt,t.ModifiedAt)).ToList());

    private List<T> Query<T>(string sql,Func<SqlDataReader,T> map,params(string Name,SqlDbType Type,int Size,object? Value)[] args)
    {
        using var conn=factory.OpenConnection();
        using var cmd=new SqlCommand(sql,conn){CommandTimeout=15};
        foreach(var a in args)cmd.Parameters.Add(a.Name,a.Type,a.Size).Value=a.Value??DBNull.Value;
        using var r=cmd.ExecuteReader();
        var rows=new List<T>();while(r.Read())rows.Add(map(r));return rows;
    }
    private static string? NullableText(SqlDataReader r,string name)=>r[name] as string;
    private static string Text(SqlDataReader r,string name)=>NullableText(r,name)??"";
}
