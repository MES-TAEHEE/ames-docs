#:project ../src/06_Web/AMES.Web/AMES.Web.csproj
#:property PublishAot=false

// Run: dotnet run --file tools/check_portal_case_creation.cs
// Component state only: no application host, authentication, or database connection.
using System.Collections;
using System.Reflection;
using AMES.Data.Repositories;
using AMES.Web.Components.Pages.Portal;

var page = new DeliveryCases();
var type = typeof(DeliveryCases);
const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
object Field(string name) => type.GetField(name, flags)!.GetValue(page)!;
void Call(string name, params object[] args) => type.GetMethod(name, flags)!.Invoke(page, args);
void Check(bool pass, string message) { if (!pass) throw new Exception(message); }

Call("NextDelivery");
Check((int)Field("_step") == 1, "An empty case list must not advance.");
var cases = (List<ScmRepository.DeliveryCase>)Field("_cases");
ScmRepository.DeliveryCase Case(string number, string status, decimal qty) => new(
    number, "", "V1", "Vendor", DateTime.Today,
    [new(1, "BOX-" + number, "PART-1", "Part", "EA", qty, "PO-1", number, false, 1, 20)],
    "PO-1", null, status);
cases.Add(Case("CASE-1", "Prepared", 20));
cases.Add(Case("CASE-2", "Prepared", 10));
cases.Add(Case("CASE-3", "Registered", 100));
cases.Add(Case("CASE-4", "Cancelled", 100));
Call("UpdateLines");
Check((int)Field("_step") == 1, "Updating cases must stay on creation step.");
Call("NextDelivery");
Check((int)Field("_step") == 2, "Next must advance with prepared cases.");
var lines = (IList)Field("_deliveryLines");
Check(lines.Count == 1 && (decimal)lines[0]!.GetType().GetProperty("Quantity")!.GetValue(lines[0])! == 30,
    "Include every prepared case, but exclude assigned/cancelled cases.");

var partType = type.GetNestedType("PartEdit", BindingFlags.NonPublic)!;
var part = Activator.CreateInstance(partType, new ScmRepository.CasePart(1, "PART-1", "Part", "EA", 20, 100))!;
((IList)Field("_parts")).Add(part);
Call("ChangeQuantity", part, "");
Check(!(bool)Field("_quantityError") && (string)partType.GetProperty("QuantityText")!.GetValue(part)! == "",
    "Clearing the input must preserve blank text without an error.");
Call("ChangeQuantity", part, "12");
Check((decimal)partType.GetProperty("Quantity")!.GetValue(part)! == 12, "Typing updates the quantity.");
Call("ChangeQuantity", part, "25");
Check((string)partType.GetProperty("QuantityText")!.GetValue(part)! == "25" && ((IList)Field("_draft")).Count == 2,
    "Replacing the input updates both visible text and boxes.");
Call("ChangeQuantity", part, "100");
Check(((IList)Field("_draft")).Count == 5, "Typing the available quantity generates five boxes.");
foreach (var input in new[] { "1.5", "-1", "abc", "101" })
{
    Call("ChangeQuantity", part, input);
    Check((bool)Field("_quantityError") && ((IList)Field("_draft")).Count == 0, "Reject invalid quantity: " + input);
}
Call("ChangeQuantity", part, "45");
Check(!(bool)Field("_quantityError") && ((IList)Field("_draft")).Count == 3,
    "45 units at 20 per box must produce three boxes.");
Call("SelectAllBoxes", true);
Call("UpdateLines");
lines = (IList)Field("_deliveryLines");
Check((decimal)lines[0]!.GetType().GetProperty("Quantity")!.GetValue(lines[0])! == 30,
    "Ungrouped boxes must not enter the delivery.");
// There is intentionally no repository injected: these operations must not write to DB.
type.GetField("_allowed", flags)!.SetValue(page, true);
type.GetField("_step", flags)!.SetValue(page, 1);
Call("CreateCase");
Check((int)Field("_step") == 1 && cases.Count == 5, "Create Case stays on step 1 and creates a local draft.");
var draftCase=cases.Last();
Check(draftCase.Number.Contains("Draft") && draftCase.Boxes.Count==3, "Use temporary labels before saving delivery.");
var editedPart=((IList)Field("_parts"))[0]!;
Check(((ScmRepository.CasePart)partType.GetProperty("Part")!.GetValue(editedPart)!).Available==55,
    "Draft case reserves quantity only in the current screen.");
Call("Cancel",draftCase.Number);
editedPart=((IList)Field("_parts"))[0]!;
Check(((ScmRepository.CasePart)partType.GetProperty("Part")!.GetValue(editedPart)!).Available==100,
    "Removing a draft returns local availability.");
type.GetField("_allowed", flags)!.SetValue(page, false);
Call("Load");
Check(((IList)Field("_cases")).Count==0, "Reopening must discard draft cases.");
Console.WriteLine("PASS: local draft create/remove/discard, explicit steps, integer quantities, case-only delivery.");
