// Run with: dotnet run --file tools/test_pda_fg_return_ui.cs
static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
var root = Directory.GetParent(Path.GetDirectoryName(SourcePath())!)!.FullName;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var page = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Components/Pages/Fg/FgRtnReturn.razor"));
Check(page.Contains("fg-return-reason-field")
    && page.Contains("fg-return-reason-trigger") && page.Contains("fg-return-reason-options")
    && page.IndexOf("fg-return-reason-options", StringComparison.Ordinal) < page.IndexOf("fg-return-note-field", StringComparison.Ordinal)
    && !page.Contains("<RadzenDropDown") && !page.Contains("pdaScan.preferDropdownBelow")
    && page.Contains("fg-return-note-input") && page.Contains("maxlength=\"500\"")
    && page.Contains("_note.Trim()") && page.Contains("_note = \"\";"),
    "Return Reason must expand inline below its trigger and be followed by a persisted, resettable Note field.");
Check(page.Contains("private string? _reason;")
    && page.Contains("Select return reason")
    && page.Contains("_reason = null;")
    && page.Contains("ToggleReasonOptions") && page.Contains("SelectReason")
    && page.Contains("Return Reason Required")
    && page.Contains("Select a return reason."),
    "Return Reason must start empty and show a focused alert when RECEIVE is pressed without a selection.");
Check(page.Contains("Text=\"RECEIVE\"") && !page.Contains("Text=\"RECEIVE RETURN\""),
    "The final Customer Return action must be labeled RECEIVE.");

var client = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/Services/PdaApi.cs"));
Check(client.Contains("FgReturnReq(string Barcode, string ReturnReason, string? Note)"),
    "The PDA return request must include Note.");

var api = File.ReadAllText(Path.Combine(root, "src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs"));
Check(api.Contains("ReturnReq(string Barcode, string ReturnReason, string? Note)")
    && api.Contains("dbo.FG_PDA_RETURN_SCAN")
    && api.Contains("dbo.FG_PDA_RETURN_RECEIVE")
    && api.Contains("cmd.Parameters.Add(\"@Note\"")
    && api.Contains("Return note must be 500 characters or fewer."),
    "The API must validate Note and use the transactional return procedures.");

var schema = File.ReadAllText(Path.Combine(root, "dist/AMES_Schema.sql"));
var migration = File.ReadAllText(Path.Combine(root, "dist/pda/PDA_SCHEMA.sql"));
Check(schema.Contains("[Note]                      NVARCHAR(500)")
    && migration.Contains("COL_LENGTH(N'dbo.FG_CustomerReturn', N'Note') IS NULL")
    && migration.Contains("ALTER TABLE dbo.FG_CustomerReturn ADD [Note] NVARCHAR(500) NULL")
    && migration.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_SCAN")
    && migration.Contains("CREATE OR ALTER PROCEDURE dbo.FG_PDA_RETURN_RECEIVE")
    && migration.Contains("JOIN dbo.FG_PickingDetail D ON D.PickID=C.PickID")
    && migration.Contains("Status='RETURN_HOLD',HoldFlag=1,Location=NULL")
    && migration.Contains("UX_FG_CustomerReturn_Stock")
    && !schema.Contains("CREATE TABLE dbo.FG_ReturnDisposition"),
    "Customer Return must use picked stock, hold returned inventory, enforce integrity, and omit the unused disposition table.");

var css = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/wwwroot/css/pda.css"));
Check(css.Contains(".fg-return-reason-card .pda-fld")
    && css.Contains(".fg-return-reason-options")
    && css.Contains("flex-direction: column !important")
    && css.Contains(".fg-return-note-input::placeholder")
    && css.Contains("height: 88px !important")
    && css.Contains("text-align: left !important")
    && css.Contains("vertical-align: top !important"),
    "Return Reason and Note must use the vertical full-width form layout.");
Check(css.Contains(".fg06-shell .fg-form-actions")
    && css.Contains("position: fixed !important") && css.Contains("z-index: 30 !important"),
    "Customer Return actions must stay fixed at the bottom of the viewport.");

var scanJs = File.ReadAllText(Path.Combine(root, "src/05_Pda/AMES.Pda/wwwroot/js/pda-scan.js"));
Check(!scanJs.Contains("preferDropdownBelow"),
    "Customer Return must not depend on popup repositioning for the Reason list.");

Console.WriteLine("PASS: Customer Return reason and dedicated Note column.");
