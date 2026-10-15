using System.Text.Json;
using AMES.Api.Auth;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;

namespace AMES.Api.Endpoints;

public static class PdaMaintenanceEndpoints
{
    public static void MapPdaMaintenance(this WebApplication app,AmesConnectionFactory factory)
    {
        var mnt=new MntRepository(factory);
        var pda=new PdaMaintenanceRepository(factory);
        var md=new MasterDataRepository(factory);
        var sys=new SysRepository(factory);
        var log=app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PdaMaintenance");
        var g=app.MapGroup("/api/mnt/pda").WithTags("Maintenance PDA");

        IResult Run(HttpContext ctx,Func<PopSessionDto,PdaMntAccess,IResult> action)
        {
            if(ctx.GetSession() is not { } session)return Results.Unauthorized();
            try
            {
                var access=pda.Access(session.EmployeeNo);
                if(!access.CanRead)return Results.StatusCode(403);
                return action(session,access);
            }
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
            catch(InvalidOperationException ex){return Results.Conflict(new {message=ex.Message});}
            catch(SqlException ex){log.LogError(ex,"Maintenance database request failed");return Results.Json(new {message="Maintenance service could not complete the request. Refresh before retrying."},statusCode:503);}
        }
        MntRepository.MwoRow Work(int id)
        {
            var work=mnt.FindWorkOrder(id)??throw new ArgumentException("Work order not found.");
            if(work.WoType!="PM"&&work.SourceType!="CHECK")throw new ArgumentException("Select a PM work order or its check follow-up.");
            return work;
        }
        static bool Mine(MntRepository.MwoRow w,PdaMntAccess a)=>a.IsAssignedTo(w.AssignedTechId);
        void Audit(PopSessionDto s,string operation,string entity,string id,object data)
        {
            try{sys.InsertAuditLog("MNT","MNT-007",operation,entity,id,null,JsonSerializer.Serialize(data),s.EmployeeNo);}
            catch(Exception ex){log.LogError(ex,"Maintenance saved but audit failed: {Operation} {Id}",operation,id);}
        }

        g.MapGet("/board",(HttpContext ctx,DateTime? from,DateTime? to,bool? mine,bool? includeClosed)=>Run(ctx,(s,a)=>
        {
            var today=DbClock.ReadNow(factory).Date;
            var lead=mnt.ReadPmLeadDays();var start=from?.Date??today;var end=to?.Date??today.AddDays(lead);
            if(start.Year<1900||end.Year>2100||start>end||(end-start).TotalDays>366)return Results.BadRequest(new {message="Select a date range of up to one year (1900–2100)."});
            var self=mine??!a.IsAdmin;
            if(self&&a.UserId is null)return Results.StatusCode(403);
            var templates=md.ListPmTemplates().Where(t=>t.ActiveFlag).ToList();
            var loto=templates.Where(t=>t.SafetyLOTOFlag).Select(t=>t.PMTemplateID).ToHashSet();
            var works=a.CanReadWork?mnt.ListPdaPmWorkOrders(start,end,self?a.UserId:null,includeClosed??false)
                .Select(w=>pda.Work(w,loto.Contains(w.ChecklistId??""))).ToList():[];
            var schedules=pda.Schedules(start,end,today,lead,self?a.UserId:null)
                .Where(p=>p.PmClass=="EQUIP"?a.CanReadEquip:a.CanReadMaint).ToList();
            return Results.Ok(new PdaMntBoard(today,lead,a,schedules,works));
        }));
        g.MapGet("/work-orders/{id:int}",(HttpContext ctx,int id)=>Run(ctx,(s,a)=>
            a.CanReadWork?Results.Ok(pda.Detail(mnt,Work(id))):Results.StatusCode(403)));
        g.MapPost("/work-orders/{id:int}/start",(HttpContext ctx,int id,PdaMntStart body)=>Run(ctx,(s,a)=>
        {
            if(!a.CanReadWork||!a.CanAct)return Results.StatusCode(403);
            var w=Work(id);
            if(!Mine(w,a))return Results.StatusCode(403);
            if(pda.RequiresLoto(w.ChecklistId)&&!body.LotoConfirmed)return Results.BadRequest(new {message="Confirm lockout/tagout before starting this work order."});
            if(w.WoType=="PM"&&w.TaskCount==0)return Results.BadRequest(new {message="This PM has no check steps. Ask an administrator to set its PM template on the web."});
            mnt.StartWorkOrder(id,s.EmployeeNo,a.UserId);
            Audit(s,"START","MNT_WorkOrder",w.WoNumber??id.ToString(),body);
            return Results.Ok(pda.Detail(mnt,Work(id)));
        }));
        g.MapPut("/work-orders/{id:int}/tasks/{taskId:int}",(HttpContext ctx,int id,int taskId,PdaMntTaskSave body)=>Run(ctx,(s,a)=>
        {
            var error=ValidateTask(body);if(error is not null)return Results.BadRequest(new {message=error});
            var w=Work(id);if(!a.CanReadWork||!a.CanEdit||!Mine(w,a))return Results.StatusCode(403);
            mnt.SetWorkOrderTaskResult(taskId,body.Result,body.Note,s.EmployeeNo,id,body.ExpectedModifiedAt,a.UserId);
            Audit(s,"CHECK","MNT_WorkOrderTask",taskId.ToString(),body);
            return Results.Ok(pda.Detail(mnt,Work(id)));
        }));
        g.MapPost("/work-orders/{id:int}/complete",(HttpContext ctx,int id,PdaMntComplete body)=>Run(ctx,(s,a)=>
        {
            var w=Work(id);if(!a.CanReadWork||!a.CanAct||!Mine(w,a))return Results.StatusCode(403);
            if(body.Result is not ("OK" or "PARTIAL" or "FOLLOWUP")||string.IsNullOrWhiteSpace(body.Action)||body.Action.Length>1000
                ||body.RootCause?.Length>1000||body.LaborMinutes is <0 or >525600||body.NextDue?.Year is <1900 or >2100)
                return Results.BadRequest(new {message="Enter a valid completion result, work summary and labor minutes."});
            if(w.WoType=="PM"&&w.TaskCount==0)return Results.BadRequest(new {message="A PM work order must contain check steps before completion."});
            var done=mnt.CompleteWorkOrder(id,body.Result,body.Action.Trim(),body.RootCause,body.LaborMinutes,
                DbClock.ReadNow(factory),null,s.EmployeeNo,body.NextDue,a.UserId,requireStarted:true);
            Audit(s,"COMPLETE","MNT_WorkOrder",done.WoNumber,new {body,done});
            return Results.Ok(new PdaMntSaved("Work order completed.",done.NextWoNumber,done.FollowUpWoNumber));
        }));

    }

    public static string? ValidateTask(PdaMntTaskSave body)
        =>body.Result is not ("PASS" or "FAIL" or "NA")?"Select Pass, Fail or N/A."
        :body.Note?.Length>500?"The check note must be 500 characters or less."
        :body.Result=="FAIL"&&string.IsNullOrWhiteSpace(body.Note)?"Enter a reason for the failed check.":null;

}
