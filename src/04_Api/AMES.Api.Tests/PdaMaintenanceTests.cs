using System.Net;
using System.Net.Http.Json;
using AMES.Api.Endpoints;
using AMES.Contracts.Dto;
using AMES.Data.Services;
using AMES.Data.Connection;
using AMES.Pda.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AMES.Api.Tests;

public class PdaMaintenanceTests
{
    [Theory]
    [InlineData("", "", "", false, true)]
    [InlineData("MONTHLY", "TIME", "OVERDUE", true, true)]
    [InlineData("monthly", "time", "overdue", true, true)]
    [InlineData("WEEKLY", "TIME", "OVERDUE", true, false)]
    [InlineData("MONTHLY", "CYCLE", "OVERDUE", true, false)]
    [InlineData("MONTHLY", "TIME", "DUE", true, false)]
    [InlineData("MONTHLY", "TIME", "OVERDUE", false, true)]
    public void Schedule_filters_combine_type_basis_status_and_assignment(string type,string basis,string status,bool onlyMine,bool expected)
    {
        var row=new PdaMntSchedule(1,"PM1","EQ1","Equipment","EQUIP","MONTHLY","TIME",30,new(2026,10,15),null,"USER-1","OVERDUE",null,false,null);
        var access=new PdaMntAccess("USER-1",true,true,true,true,true,true,true);
        Assert.Equal(expected,MaintenanceChecks.MatchesSchedule(row,access,type,basis,status,onlyMine));
    }

    [Theory]
    [InlineData("USER-1", true, true)]
    [InlineData("USER-2", true, false)]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("USER-1", false, true)]
    [InlineData("USER-2", false, true)]
    [InlineData(null, false, true)]
    public void My_work_checkbox_filters_only_when_checked(string? technician,bool onlyMine,bool expected)
    {
        var access=new PdaMntAccess("USER-1",true,true,true,true,true,true,true);
        Assert.Equal(expected,MaintenanceChecks.MatchesAssignment(access,technician,onlyMine));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Schedule_status_options_do_not_depend_on_loaded_rows(bool includeCompleted)
        => Assert.Equal(new[]{"OK","DUE","OVERDUE"},MaintenanceChecks.StatusOptions(true,includeCompleted));

    [Theory]
    [InlineData(false,3)]
    [InlineData(true,5)]
    public void Work_status_options_match_the_include_completed_scope(bool includeCompleted,int count)
    {
        var statuses=MaintenanceChecks.StatusOptions(false,includeCompleted);
        Assert.Equal(count,statuses.Length);
        Assert.Contains(MwoStatusCodes.Issued,statuses);
        Assert.Contains(MwoStatusCodes.Assigned,statuses);
        Assert.Contains(MwoStatusCodes.InProgress,statuses);
        Assert.Equal(includeCompleted,statuses.Contains(MwoStatusCodes.Completed));
        Assert.Equal(includeCompleted,statuses.Contains(MwoStatusCodes.Closed));
    }

    [Theory]
    [InlineData(null, "__NONE", true)]
    [InlineData(" ", "__NONE", true)]
    [InlineData("TIME", "__NONE", false)]
    [InlineData(null, "TIME", false)]
    [InlineData(null, "", true)]
    public void Basis_filter_handles_unset_values(string? value,string filter,bool expected)
        =>Assert.Equal(expected,MaintenanceChecks.MatchesValue(value,filter));

    [Fact]
    public void Filter_controls_update_the_lists_and_preserve_owner_permissions()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        foreach(var field in new[]{"type","basis","status","assignment"})Assert.Contains($"id=\"mnt-filter-{field}\"",page);
        Assert.Contains("@foreach (var p in FilteredSchedules)",page);
        Assert.Contains("@foreach (var w in FilteredWorks)",page);
        Assert.Contains("IsAssignedTo(detail.Work.TechId)",page);
        Assert.Contains("@onclick=\"ClearFilters\"",page);
        Assert.Contains("type=\"checkbox\" @bind=\"onlyMine\"",page);
        Assert.Contains("My work only",page);
        Assert.DoesNotContain("AssignmentChoices",page);
        Assert.Contains("MaintenanceChecks.StatusOptions(IsSchedule, closed)",page);
        var css=ReadPda("Components/Pages/Maintenance.razor.css");
        Assert.Contains(".mnt-filter-row.paired { grid-template-columns: repeat(2, minmax(0, 1fr)); }",css);
        Assert.Contains("class=\"mnt-filter-row",page);
    }

    [Fact]
    public void Maintenance_keeps_filter_dialog_while_transactions_show_dates_without_filter()
    {
        var maintenance=ReadPda("Components/Pages/Maintenance.razor");
        var history=ReadPda("Components/Pages/Wh/Wh08TransactionHistory.razor");
        Assert.Contains("<PdaFilterPanel Title=\"DUE DATE\"",maintenance);
        Assert.Contains("TRANSACTION DATE",history);
        Assert.DoesNotContain("<PdaFilterPanel",history);
        Assert.DoesNotContain("WORKER ID",history);
        Assert.DoesNotContain("ADJ REASON",history);
        foreach(var page in new[]{maintenance})
        {
            Assert.DoesNotContain("<details",page);
            Assert.DoesNotContain("<select",page);
            Assert.Contains("<RadzenDropDown",page);
            Assert.Contains("class=\"pda-filter-select\"",page);
            Assert.Contains("PopupStyle=\"z-index: 2100;",page);
        }
        var panel=ReadPda("Components/PdaFilterPanel.razor");
        Assert.Contains("pda-date-filter-head",panel);
        Assert.Contains("Icon=\"filter_alt\"",panel);
        Assert.Contains("role=\"dialog\"",panel);
        Assert.Contains("e.Key == \"Escape\"",panel);
        Assert.Contains("await panel.FocusAsync()",panel);
        Assert.Contains("await toggle.FocusAsync()",panel);
        Assert.Contains("if (_detailRow is not null || _dcrRow",history);
        var css=ReadPda("wwwroot/css/pda.css");
        Assert.Contains("justify-content: space-between",css);
        Assert.Contains(".pda-filter-select.rz-dropdown",css);
        Assert.Contains(".rz-dropdown-label { display: flex !important; align-items: center !important; height: 100% !important;",css);
        Assert.Contains("html body .rz-dropdown-panel",css);
        Assert.Contains("z-index: 1800;",css);
        Assert.Contains("z-index: 1801;",css);
    }

    [Fact]
    public void Maintenance_home_distinguishes_schedule_and_work_order_colors()
    {
        var home=ReadPda("Components/Pages/MaintenanceHome.razor");
        Assert.Contains("wh-home-tile mnt-schedule",home);
        Assert.Contains("wh-home-tile mnt-work",home);
        var css=ReadPda("Components/Pages/MaintenanceHome.razor.css");
        Assert.Contains(".mnt-schedule { border-top-color: #38bdf8",css);
        Assert.Contains(".mnt-work { border-top-color: #fb923c",css);
    }

    [Fact]
    public void Opening_non_owned_work_shows_alert_but_refresh_does_not_repeat_it()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        Assert.Contains("OpenWork(int id, bool showNotice = true)",page);
        Assert.Contains("if (showNotice && !CanOwn)",page);
        Assert.Contains("ShowAlert(ReadOnlyTitle, ReadOnlyNotice)",page);
        Assert.Contains("OpenWork(detail.Work.Id, showNotice: false)",page);
        Assert.Contains("@ReadOnlyNotice",page);
    }

    [Theory]
    [InlineData(false,"USER-1","USER-1",true)]
    [InlineData(true,"USER-1","USER-1",true)]
    [InlineData(false,"USER-1","USER-2",false)]
    [InlineData(true,"USER-1","USER-2",false)]
    [InlineData(false,"USER-1",null,false)]
    [InlineData(true,"USER-1",null,false)]
    [InlineData(true,null,null,false)]
    [InlineData(true,"","",false)]
    public void Only_the_assigned_user_can_execute_even_when_admin(bool admin,string? userId,string? assigned,bool allowed)
    {
        var access=new PdaMntAccess(userId,admin,true,true,true,true,true,true);
        Assert.Equal(allowed,access.IsAssignedTo(assigned));
    }

    [Fact]
    public void Schedules_have_separate_equipment_and_maintenance_tabs_and_work_is_owner_gated()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        Assert.Contains("role=\"tablist\"",page);
        Assert.Contains("Equipment PM",page);
        Assert.Contains("Maintenance PM",page);
        Assert.Contains("p.PmClass == scheduleClass",page);
        Assert.Contains("@foreach (var p in FilteredSchedules)",page);
        Assert.Contains("IsAssignedTo(detail.Work.TechId)",page);
        Assert.DoesNotContain("board?.Access.IsAdmin == true",page);
    }

    [Theory]
    [InlineData("PENDING", false)]
    [InlineData("PASS", true)]
    [InlineData("FAIL", true)]
    [InlineData("NA", true)]
    public void Review_is_available_only_after_every_step_is_saved(string lastResult,bool allowed)
    {
        Assert.False(MaintenanceChecks.AllSaved([]));
        Assert.Equal(allowed,MaintenanceChecks.AllSaved([Check(1,"PASS"),Check(2,lastResult)]));
    }

    [Fact]
    public void Steps_distinguish_saved_current_and_not_yet_available()
    {
        var tasks=new[]{Check(1,"PASS"),Check(2,"PENDING"),Check(3,"PENDING")};
        Assert.Equal("done",MaintenanceChecks.StepState(tasks,tasks[0]));
        Assert.Equal("active",MaintenanceChecks.StepState(tasks,tasks[1]));
        Assert.Equal("upcoming",MaintenanceChecks.StepState(tasks,tasks[2]));
        tasks[1]=Check(2,"FAIL");
        Assert.Equal("done",MaintenanceChecks.StepState(tasks,tasks[1]));
        Assert.Equal("active",MaintenanceChecks.StepState(tasks,tasks[2]));
    }

    [Theory]
    [InlineData("FAIL", null, true)]
    [InlineData("FAIL", " ", true)]
    [InlineData("FAIL", "Oil leak", false)]
    [InlineData("PASS", null, false)]
    [InlineData("NA", null, false)]
    public void Fail_without_reason_requires_immediate_note_alert(string result,string? note,bool required)
        =>Assert.Equal(required,MaintenanceChecks.RequiresNote(result,note));

    [Theory]
    [InlineData(0,0,false)]
    [InlineData(3,1,false)]
    [InlineData(3,0,true)]
    public void Server_rejects_completion_with_unsaved_steps(int total,int pending,bool allowed)
    {
        var ex=Record.Exception(()=>MwoChecklistRules.ValidatePdaCompletion(total,pending));
        if(allowed)Assert.Null(ex);else Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public void Workflow_wires_completion_gate_and_inline_note_feedback()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        Assert.Contains("disabled=\"@(!CanReview)\"",page);
        Assert.Contains("ShowAlert(\"Failure Reason Required\"",page);
        Assert.Contains("await noteInput.FocusAsync()",page);
        Assert.Contains("noteRequired ? \"invalid\"",page);
        Assert.Contains("state == \"upcoming\"",page);
    }

    [Fact]
    public void Maintenance_alerts_reuse_WH_FG_modal_and_explicit_assignment_titles()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        var fg=ReadPda("Components/Pages/Fg/Fg04FifoPicking.razor");
        foreach(var css in new[]{"wh-modal-backdrop","wh-modal-card","wh-modal-mark","wh-modal-title","wh-modal-message","wh-modal-actions","wh-modal-btn primary"})
        {
            Assert.Contains(css,page);
            Assert.Contains(css,fg);
        }
        Assert.Contains("Not Your Work Order",page);
        Assert.Contains("Work Order Not Assigned",page);
        Assert.DoesNotContain("Dialogs.Alert",page);
        Assert.DoesNotContain("\"View only\"",page);
        Assert.Contains("role=\"alertdialog\"",page);
        var cssSource=ReadPda("wwwroot/css/pda.css");
        Assert.Contains(".pda-shell.wh .wh-modal-card",cssSource);
        Assert.Contains("background: #0f1d33 !important",cssSource);
        Assert.Contains(".pda-shell.wh .wh-modal-title",cssSource);
        Assert.Contains("color: #ffffff !important",cssSource);
    }

    private static PdaMntTask Check(int id,string result)=>new(id,1,id,"Step",null,null,null,null,null,result,null,null,null,null);

    [Fact]
    public void Pda_schedule_is_view_only_and_home_tile_has_its_own_color_and_screen_count()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        foreach(var edit in new[]{"NewSchedule","EditSchedule","SaveSchedule","scheduleOpen"})
            Assert.DoesNotContain(edit,page);
        Assert.Contains("Open work order",page);
        Assert.Contains("Save &amp; next",page);
        var home=ReadPda("Components/Pages/Home.razor");
        Assert.Contains("pda-tile maintenance",home);
        Assert.Contains("<div class=\"pda-tile-sub\">2 screens</div>",home);
        Assert.Contains(".pda-tile.maintenance { border-top: 4px solid #a78bfa; }",ReadPda("wwwroot/css/pda.css"));
    }

    [Fact]
    public async Task Pda_exposes_no_schedule_create_edit_or_delete_routes()
    {
        var builder=WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app=builder.Build();
        app.MapPdaMaintenance(new AmesConnectionFactory("Server=unused"));
        await app.StartAsync();
        using var http=new HttpClient {BaseAddress=new Uri(app.Urls.Single())};
        foreach(var method in new[]{HttpMethod.Post,HttpMethod.Put,HttpMethod.Delete})
        {
            using var request=new HttpRequestMessage(method,method==HttpMethod.Post?"/api/mnt/pda/schedules":"/api/mnt/pda/schedules/1");
            using var response=await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        }
        await app.StopAsync();
    }

    [Fact]
    public void Maintenance_has_module_menu_and_separate_function_routes()
    {
        var home=ReadPda("Components/Pages/MaintenanceHome.razor");
        var page=ReadPda("Components/Pages/Maintenance.razor");
        Assert.Contains("@page \"/mnt\"",home);
        Assert.Contains("wh-home-shell",home);
        foreach(var route in new[]{"/mnt/schedules","/mnt/work-orders"})
        {
            Assert.Contains(route,home);
            Assert.Contains($"@page \"{route}\"",page);
        }
        Assert.DoesNotContain("<nav>",page);
    }

    [Fact]
    public void Maintenance_content_scrolls_inside_bounded_common_shell()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        var css=ReadPda("Components/Pages/Maintenance.razor.css");
        Assert.Contains("pda-shell wh wh08-shell mnt",page);
        Assert.Contains("pda-bd mnt-body",page);
        Assert.Contains("height: 100%; min-height: 0",css);
        Assert.Contains("overflow-y: auto",css);
        Assert.Contains("touch-action: pan-y",css);
        Assert.Contains(".mnt-body > * { flex-shrink: 0; }",css);
        Assert.DoesNotContain("min-height: 100vh",css);
    }

    [Fact]
    public void Maintenance_reuses_warehouse_dates_and_aligned_actions()
    {
        var page=ReadPda("Components/Pages/Maintenance.razor");
        var css=ReadPda("Components/Pages/Maintenance.razor.css");
        Assert.Contains("<RadzenDatePicker",page);
        Assert.Contains("wh08-filter-date",page);
        Assert.Contains("wh08-date-apply",page);
        Assert.Contains("wh08-refresh",page);
        Assert.DoesNotContain("type=\"date\"",page);
        Assert.Contains("grid-auto-columns: minmax(0, 1fr)",css);
        Assert.Contains(".actions button { width: 100%; height: 44px",css);
    }

    private static string ReadPda(string relative,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../05_Pda/AMES.Pda",relative)));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Board_omits_missing_dates_instead_of_sending_empty_date_values(bool hasFrom,bool hasTo)
    {
        string? query=null;
        using var http=Client(r=>
        {
            query=r.RequestUri!.Query;
            return new(HttpStatusCode.OK){Content=new StringContent("{}",System.Text.Encoding.UTF8,"application/json")};
        });
        await new MaintenanceApi(http,new()).Board(hasFrom?new DateTime(2026,10,15):null,hasTo?new DateTime(2026,10,22):null,false,false);
        Assert.NotNull(query);
        Assert.Equal(hasFrom,query.Contains("from=2026-10-15"));
        Assert.Equal(hasTo,query.Contains("to=2026-10-22"));
        if(!hasFrom) Assert.DoesNotContain("from=",query);
        if(!hasTo) Assert.DoesNotContain("to=",query);
    }

    [Theory]
    [InlineData("PASS", null, true)]
    [InlineData("NA", null, true)]
    [InlineData("FAIL", "Leaking", true)]
    [InlineData("FAIL", " ", false)]
    [InlineData("PENDING", null, false)]
    public void Check_results_require_a_valid_result_and_failure_reason(string result,string? note,bool valid)
        => Assert.Equal(valid,PdaMaintenanceEndpoints.ValidateTask(new(result,note,null)) is null);

    [Fact]
    public void Check_notes_have_database_length_limit()
        => Assert.NotNull(PdaMaintenanceEndpoints.ValidateTask(new("PASS",new string('a',501),null)));

    [Theory]
    [InlineData(0, true, "TECH", "TECH", true)]
    [InlineData(1, true, "TECH", "TECH", false)]
    [InlineData(0, false, "TECH", "TECH", false)]
    [InlineData(0, true, "OTHER", "TECH", false)]
    [InlineData(0, true, "OTHER", null, true)]
    public void Step_guard_checks_order_version_and_ownership(int preceding,bool sameVersion,string assigned,string? required,bool allowed)
    {
        var at=new DateTime(2026,10,15,12,0,0);
        var ex=Record.Exception(()=>MwoChecklistRules.ValidatePdaStep(preceding,at,sameVersion?at:at.AddSeconds(-1),assigned,required));
        if(allowed)Assert.Null(ex);else Assert.IsType<InvalidOperationException>(ex);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,"Session expired")]
    [InlineData(HttpStatusCode.Forbidden,"Access denied")]
    [InlineData(HttpStatusCode.Conflict,"Complete preceding steps")]
    public async Task Client_preserves_session_permission_and_business_errors(HttpStatusCode status,string expected)
    {
        using var http=Client(_=>new(status){Content=JsonContent.Create(new {message=expected})});
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>new MaintenanceApi(http,new()).Detail(1));
        Assert.Contains(expected,ex.Message);
    }

    [Fact]
    public async Task Client_sends_step_version_and_result_to_its_work_order()
    {
        string? payload=null,path=null;
        using var http=Client(r=>
        {
            path=r.RequestUri!.AbsolutePath;payload=r.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new(HttpStatusCode.Conflict){Content=JsonContent.Create(new {message="Changed"})};
        });
        var at=new DateTime(2026,10,15,12,0,0);
        var t=new PdaMntTask(7,3,1,"Check",null,null,null,null,null,"PENDING",null,null,null,at);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MaintenanceApi(http,new()).SaveTask(3,t,"PASS","Checked"));
        Assert.Equal("/api/mnt/pda/work-orders/3/tasks/7",path);
        Assert.Contains("expectedModifiedAt",payload);Assert.Contains("PASS",payload);Assert.Contains("2026-10-15T12:00:00",payload);
    }

    [Fact]
    public async Task Lost_write_response_warns_against_blind_retry()
    {
        using var http=Client(_=>throw new HttpRequestException());
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>new MaintenanceApi(http,new()).Start(1,false));
        Assert.Contains("may already be saved",ex.Message);
    }
    private static HttpClient Client(Func<HttpRequestMessage,HttpResponseMessage> response)
        =>new(new Handler(response)){BaseAddress=new Uri("https://example.test")};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(response(r));
    }
}
