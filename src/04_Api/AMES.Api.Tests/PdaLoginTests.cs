using System.Text.Json;
using AMES.Contracts.Dto;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PdaLoginTests
{
    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string source="")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "../..", path));

    [Fact]
    public void Public_employee_option_contains_only_id_and_name()
    {
        using var json=JsonDocument.Parse(JsonSerializer.Serialize(new LoginEmployeeDto("E001","Operator"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(new[]{"employeeNo","employeeName"},json.RootElement.EnumerateObject().Select(p=>p.Name));
    }

    [Fact]
    public void Employee_list_uses_active_accounts_and_web_profile_precedence_without_reading_hashes()
    {
        var code=Read("02_Data/AMES.Data/Repositories/AuthRepository.cs");
        code=code[code.IndexOf("public List<LoginEmployeeDto> ListLoginEmployees()",StringComparison.Ordinal)..];
        code=code[..code.IndexOf("return list;",StringComparison.Ordinal)];
        Assert.Contains("COALESCE(p.AccountStatus,'Active') IN ('Active','')",code);
        Assert.Contains("w.ActiveFlag=1",code);
        Assert.Contains("NOT EXISTS",code);
        Assert.DoesNotContain("PasswordHash",code);
        Assert.DoesNotContain("PinHash",code);

        var api=Read("04_Api/AMES.Api/Endpoints/AuthEndpoints.cs");
        api=api[api.IndexOf("g.MapGet(\"/employees\"",StringComparison.Ordinal)..];
        api=api[..api.IndexOf("g.MapPost(\"/login\"",StringComparison.Ordinal)];
        Assert.Contains("CacheControl = \"no-store\"",api);
        Assert.Contains("PdaScenarioUsers.IsTestAccount",api);
        Assert.Contains("RequireRateLimiting(\"login\")",api);
    }

    [Fact]
    public void Login_keeps_badge_sign_in_and_adds_editable_employee_picker_with_pin()
    {
        var code=Read("05_Pda/AMES.Pda/Components/Pages/Login.razor");
        Assert.Contains("RadzenAutoComplete",code);
        Assert.Contains("OpenOnFocus=\"true\"",code);
        Assert.Contains("pda-login-keypad",code);
        Assert.Contains("pda-pin-dot",code);
        Assert.Contains("_pin.Length >= 4",code);
        Assert.Contains("PressPinDigit",code);
        Assert.Contains("DeletePinDigit",code);
        Assert.DoesNotContain("RadzenPassword",code);
        Assert.Contains("Api.LoginAsync(_employeeNo.Trim(), _pin)",code);
        Assert.Contains("Api.LoginByBarcodeAsync(barcode)",code);
        Assert.Contains("BadgeScanParser.Parse(barcode)",code);
        Assert.Contains("EmployeeNo = badge.WorkerNo",code);
        Assert.Contains("if (!badge.IsEosFormat)",code);
        Assert.Contains("_pin = \"\"",code);
        Assert.DoesNotContain("pda-scan-input",code); // manual ID/PIN must not be captured as scanner input
    }

    [Fact]
    public void Missing_employee_endpoint_reports_required_server_update_without_changing_api_address()
    {
        var code=Read("05_Pda/AMES.Pda/Components/Pages/Login.razor");
        Assert.Contains("ex.StatusCode == System.Net.HttpStatusCode.NotFound",code);
        Assert.Contains("Server API update required",code);
        Assert.Contains("PdaBarcodeHub.Scanned += OnNativeBarcodeScanned",code);
    }
}
