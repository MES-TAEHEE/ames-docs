using AMES.Contracts.Dto;
using AMES.Pda.Services;
using Xunit;

namespace AMES.Api.Tests;

public sealed class PickingRequestScanTests
{
    static PickingRequestOptionsDto Options()=>new([new("I301A1F1","Injection 3","")],
        [new("21358G2-PIDHA","Resin","G")],new(2026,11,5),true);

    [Theory]
    [InlineData("I301A1F1","LOCATION","I301A1F1")]
    [InlineData("\u0002i301a1f1\u0003\r\n","LOCATION","I301A1F1")]
    [InlineData("21358G2-PIDHA","PART","21358G2-PIDHA")]
    [InlineData(" 21358g2-pidha ","PART","21358G2-PIDHA")]
    public void Registered_codes_are_classified_without_prefix_guessing(string scan,string kind,string code)
    {
        var result=PickingRequestScan.Resolve(scan,Options(),"I301A1F1");
        Assert.Equal(kind,result.Kind);Assert.Equal(code,result.Option!.Code);
    }

    [Fact]
    public void Part_scanning_requires_a_previously_validated_line_location()
    {
        Assert.Equal("LOCATION_REQUIRED",PickingRequestScan.Resolve("21358G2-PIDHA",Options()).Kind);
        Assert.Equal("LOCATION_REQUIRED",PickingRequestScan.Resolve("21358G2-PIDHA",Options(),"WAREHOUSE-INVALID").Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FG-UNKNOWN")]
    [InlineData("UIT-LP-DEAHA-20261021-0011")]
    public void Missing_or_unavailable_codes_do_not_select_an_unrelated_option(string? scan)
    {
        var result=PickingRequestScan.Resolve(scan,Options());
        Assert.Equal("UNKNOWN",result.Kind);Assert.Null(result.Option);
    }

    [Fact]
    public void Colliding_part_and_location_codes_require_explicit_list_selection()
    {
        var options=Options();options.Parts.Add(new("I301A1F1","Duplicate code","EA"));
        Assert.Equal("AMBIGUOUS",PickingRequestScan.Resolve("I301A1F1",options).Kind);
    }

    [Fact]
    public void Request_form_uses_sequential_scan_fields_dark_qty_and_shared_footer_without_date_or_pickers()
    {
        var page=Read("WhPickingRequest.razor");var css=Read("WhPickingRequest.razor.css");
        Assert.DoesNotContain("<RadzenDropDown",page);
        Assert.DoesNotContain("REFRESH",page);
        Assert.DoesNotContain("OpenPicker",page);
        Assert.DoesNotContain("RadzenDatePicker",page);
        Assert.Contains("request-location-scan",page);Assert.Contains("request-part-scan",page);
        Assert.Contains("@if (!string.IsNullOrEmpty(_destination))",page);
        Assert.Contains("_part=\"\";_partBarcode=\"\";_qty=1;_focusScan=true;",page);
        Assert.Contains("wh02-action-bar picking-request-actions",page);
        Assert.Contains("pda-btn-pri wh02-receive-btn wh02-confirm-btn",page);
        Assert.Contains("PdaBarcodeHub.Scanned+=OnNativeBarcodeScanned",page);
        Assert.Contains("PdaBarcodeHub.Scanned-=OnNativeBarcodeScanned",page);
        Assert.Contains("PickingRequestScan.Resolve(value,_options,_destination)",page);
        Assert.Contains("picking-request-shell ::deep .request-qty .rz-inputtext",css);
        Assert.Contains("background:#071225 !important; color:#fff !important;",css);
    }
    static string Read(string name,[System.Runtime.CompilerServices.CallerFilePath]string source="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,"../../05_Pda/AMES.Pda/Components/Pages/Wh",name)));
}
