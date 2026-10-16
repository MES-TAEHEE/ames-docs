#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false

using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string p = "") => p;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
var bin = Path.Combine(root,"src/05_Pda/AMES.Pda/bin/Debug/net10.0-windows10.0.19041.0/win-x64");
AssemblyLoadContext.Default.Resolving += (_, n) => File.Exists(Path.Combine(bin,n.Name+".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,n.Name+".dll")) : null;
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,"AMES.Pda.dll"));
using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5210") };
void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
async Task<string> Login(string employee) {
    using var r=await client.PostAsJsonAsync("/api/auth/login",new {employeeNo=employee,pin="0000",terminalId="PDA-DEV-01",lineId="LINE-INJ-01",shiftCode="A"}); r.EnsureSuccessStatusCode();
    using var j=JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.GetProperty("token").GetString()!;
}
Check((await client.GetAsync("/api/fg/transactions")).StatusCode==HttpStatusCode.Unauthorized,"Anonymous history blocked");
Check((await client.PostAsync("/api/fg/test/ppt-reset/history",null)).StatusCode==HttpStatusCode.Unauthorized,"Anonymous reset blocked");
client.DefaultRequestHeaders.Authorization=new("Bearer",await Login("SCTEST2"));
Check((await client.PostAsync("/api/fg/test/ppt-reset/history",null)).IsSuccessStatusCode,"SCTEST2 must reset detailed history");
var token=await Login("SCTEST1"); client.DefaultRequestHeaders.Authorization=new("Bearer",token);
(await client.PostAsync("/api/fg/test/ppt-reset/history",null)).EnsureSuccessStatusCode();
Check((await client.GetAsync("/api/fg/transactions?dateFrom=2026-09-09&dateTo=2026-09-01")).StatusCode==HttpStatusCode.BadRequest,"Inverted date range blocked");
var authType=asm.GetType("AMES.Pda.Services.AuthState",true)!; var auth=Activator.CreateInstance(authType)!;
var session=JsonSerializer.Deserialize(await client.GetStringAsync("/api/auth/me"),authType.GetProperty("Session")!.PropertyType,new JsonSerializerOptions(JsonSerializerDefaults.Web));
authType.GetMethod("SignIn")!.Invoke(auth,[token,session]);
using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src/04_Api/AMES.Api/appsettings.json")),new JsonDocumentOptions {CommentHandling=JsonCommentHandling.Skip});
var dataAsm=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,"AMES.Data.dll"));
var factory=Activator.CreateInstance(dataAsm.GetType("AMES.Data.Connection.AmesConnectionFactory",true)!,[settings.RootElement.GetProperty("ConnectionStrings").GetProperty("AMES").GetString()!])!;
var apiType=asm.GetType("AMES.Pda.Services.PdaApi",true)!; var api=Activator.CreateInstance(apiType,[client,auth,factory])!;
var type=asm.GetType("AMES.Pda.Components.Pages.Wh.Wh08TransactionHistory",true)!;
var services=new ServiceCollection().AddLogging(); services.AddSingleton(authType,auth); services.AddSingleton(apiType,api);
services.AddSingleton<NavigationManager>(new TestNavigation()); services.AddSingleton<IJSRuntime>(new NoJs());
services.AddSingleton(AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bin,"Radzen.Blazor.dll")).GetType("Radzen.DialogService",true)!);
await using var provider=services.BuildServiceProvider(); await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
Capture.PageType=type;
var output=Path.Combine(root,".tmp/fg-history"); Directory.CreateDirectory(output);
await renderer.Dispatcher.InvokeAsync(async () => {
    var rendered=await renderer.RenderComponentAsync<Capture>(); var page=Capture.Page!;
    const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
    object? Field(string name)=>type.GetField(name,f)!.GetValue(page);
    object? Prop(string name)=>type.GetProperty(name,f)!.GetValue(page);
    int Count()=>((IEnumerable)Prop("FilteredRows")!).Cast<object>().Count();
    async Task Act(string name,params object[] args) { await (Task)type.GetMethod(name,f)!.Invoke(page,args)!; typeof(ComponentBase).GetMethod("StateHasChanged",f)!.Invoke(page,null); }
    async Task Shot(int n) { var html="<!DOCTYPE html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><link rel='stylesheet' href='radzen.css'><link rel='stylesheet' href='pda.css'></head><body><div id='app'>"+rendered.ToHtmlString()+"</div></body></html>"; await File.WriteAllTextAsync(Path.Combine(output,$"screen-{n}.html"),html); }
    await Act("StartPptStep",1); Check(Count()==5,"Five history rows"); await Shot(1);
    foreach(var kind in new[]{"IN","PICK","LOAD","RETURN","ADJ"}) { await Act("RunPptValue",kind); Check(Count()==1,$"Filter {kind}"); }
    await Act("RunPptValue","YESTERDAY"); Check(Count()==0,"Yesterday empty");
    await Act("RunPptValue","UNKNOWN"); Check(Count()==0,"Unknown empty");
    await Act("StartPptStep",2); await Shot(2);
    foreach(var key in new[]{"LOT","STOCK","PART"}) { await Act("RunPptValue",key); Check(Count()==5,$"Search {key}"); }
    await Act("RunPptValue","SLIP"); Check(Count()==3,"Outgoing slip shows PICK LOAD RETURN"); await Shot(3);
    await Act("RunPptValue","RETURN_DETAIL"); var detail=Field("_detailRow")!;
    Check((string)detail.GetType().GetProperty("ReasonNote")!.GetValue(detail)! == "PPT return note","Return note"); await Shot(4);
    type.GetMethod("CloseDetail",f)!.Invoke(page,null);
    await Act("StartPptStep",5); detail=Field("_detailRow")!;
    Check((decimal)detail.GetType().GetProperty("BeforeQty")!.GetValue(detail)! == 20 && (decimal)detail.GetType().GetProperty("AfterQty")!.GetValue(detail)! == 22,"Adjustment before/after"); await Shot(5);
    type.GetMethod("CloseDetail",f)!.Invoke(page,null); Check(Field("_detailRow")==null && (string)Field("_search")! == "5011FG260908970001","Close preserves search");
    await Act("RunPptValue","API_ERROR");
    Check((string)Field("_msg")! == "FG transaction service is unavailable. Please try again." && Count()==0,
        "FG transaction API failure must be visible and clear stale rows");
});
File.Copy(Path.Combine(root,"src/05_Pda/AMES.Pda/wwwroot/css/pda.css"),Path.Combine(output,"pda.css"),true);
Console.WriteLine("PASS: FG Transactions authorization, operation types, searches, details, API errors and Razor HTML renders.");

sealed class TestNavigation:NavigationManager { public TestNavigation()=>Initialize("http://localhost/","http://localhost/fg/history"); protected override void NavigateToCore(string uri,bool forceLoad)=>Uri=ToAbsoluteUri(uri).ToString(); }
sealed class NoJs:IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id,object?[]? args)=>ValueTask.FromResult(default(T)!); public ValueTask<T> InvokeAsync<T>(string id,CancellationToken c,object?[]? args)=>InvokeAsync<T>(id,args); }
sealed class Capture:ComponentBase { public static Type PageType=null!; public static object? Page; protected override void BuildRenderTree(RenderTreeBuilder b) { b.OpenComponent(0,PageType); b.AddComponentReferenceCapture(1,p=>Page=p); b.CloseComponent(); } }
