using System.Globalization;
using System.Text;

namespace AMES.Web.Services;

public sealed record ShippingLabel(string Barcode,string Kind,string Vendor,string PartNo,
    string PartName,string Quantity,string Reference,string Fifo="",string Destination="",string Order="",string Lot="",string Sequence="",
    string DeliveryDate="",string ParentNumber="",int UnitItemCount=0)
{
    public int Copies => Kind is "UNIT ITEM (UIT)" or "UIT" ? 2 : 1;
}

public sealed record ShippingLabelField(string Caption,string Value);

public sealed class CompactQrLabelLayout
{
    public const decimal WidthMm=100m,HeightMm=40m,TextLeftMm=31.6m,TextWidthMm=64.4m;
    public int Dpi {get;}
    public CompactQrLabelLayout(int dpi=203)
    {
        if(dpi is not (203 or 300))throw new ArgumentOutOfRangeException(nameof(dpi));
        Dpi=dpi;
    }
    public int Dots(decimal mm)=>(int)Math.Round(mm*Dpi/25.4m);
    public static IReadOnlyList<ShippingLabelField> Fields(ShippingLabel label)
    {
        static string Value(string? value)=>string.IsNullOrWhiteSpace(value)?"-":value.Replace('\r',' ').Replace('\n',' ').Trim();
        return label.Kind switch
        {
            "DELIVERY NOTE" => [new("SUPPLIER",Value(label.Vendor)),new("DELIVER TO",Value(label.Destination)),new("DELIVERY DATE",Value(label.DeliveryDate))],
            "SHIPPING UNIT (SHU)" or "SHU" => [new("DELIVERY NOTE",Value(label.ParentNumber)),new("UNIT ITEMS",label.UnitItemCount.ToString(CultureInfo.InvariantCulture))],
            "UNIT ITEM (UIT)" or "UIT" => [new("PART NO.",Value(label.PartNo)),new("QUANTITY",Value(label.Quantity)),new("LOT NO.",Value(label.Lot)),new("FIFO / PRODUCTION DATE",Value(label.Fifo))],
            _ => throw new ArgumentException("Unsupported shipping label type.")
        };
    }
    public static void ValidateBarcode(string value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>80||value.Any(c=>c<'!'||c>'~'))throw new ArgumentException("Invalid label barcode.");
    }
    public decimal FontMm(string value,decimal maximum=2.6m)
    {
        var units=Math.Max(value.Length*.65m,value.Sum(c=>c>127 ? 1m : "MW@#%".Contains(c) ? .95m : "ilI1|.:-/ ".Contains(c) ? .4m : .7m));
        var size=Math.Min(maximum,TextWidthMm/Math.Max(1m,units));
        if(size<1.6m)throw new ArgumentException("A label value is too long to print without clipping. Check the code or LOT value.");
        return Math.Floor(size*100m)/100m;
    }
    public int QrModules(string value)
    {
        // QR Model 2, byte mode, error correction L: supported identifiers contain at most 80 ASCII bytes.
        ValidateBarcode(value);
        var capacities=new[]{17,32,53,78,106};
        var version=Array.FindIndex(capacities,c=>value.Length<=c)+1;
        return 17+4*version;
    }
    public int QrMagnification(string value)=>Math.Clamp(Dots(24m)/(QrModules(value)+8),1,10);
    public decimal QrSideMm(string value)=>(QrModules(value)+8)*QrMagnification(value)*25.4m/Dpi;
    public decimal QrLeftMm(string value)=>(28.4m-QrSideMm(value))/2m+1.6m;
    public decimal QrTopMm(string value)=>(HeightMm-QrSideMm(value))/2m;
    public int QrX(string value)=>Dots(QrLeftMm(value))+4*QrMagnification(value);
    public int QrY(string value)=>Dots(QrTopMm(value))+4*QrMagnification(value);
}

public static class ShippingLabelZpl
{
    public static string Build(ShippingLabel label,int dpi=203)
    {
        CompactQrLabelLayout.ValidateBarcode(label.Barcode);
        var layout=new CompactQrLabelLayout(dpi);
        var fields=CompactQrLabelLayout.Fields(label);
        string Text(decimal y,decimal size,string value)=>
            $"^FO{layout.Dots(CompactQrLabelLayout.TextLeftMm)},{layout.Dots(y)}^A0N,{layout.Dots(size)},{layout.Dots(size)}^FB{layout.Dots(CompactQrLabelLayout.TextWidthMm)},1,0,L^FH^FD{Hex(value)}^FS\n";
        var z=new StringBuilder($"^XA\n^CI28\n^PW{layout.Dots(CompactQrLabelLayout.WidthMm)}\n^LL{layout.Dots(CompactQrLabelLayout.HeightMm)}\n^LH0,0\n");
        z.Append($"^FO0,0^GB{layout.Dots(CompactQrLabelLayout.WidthMm)-1},{layout.Dots(CompactQrLabelLayout.HeightMm)-1},1^FS\n");
        // Manual byte mode keeps the QR's value exact and its reserved module/quiet-zone size predictable.
        z.Append($"^FO{layout.QrX(label.Barcode)},{layout.QrY(label.Barcode)}^BQN,2,{layout.QrMagnification(label.Barcode)}^FH^FDLM,B{label.Barcode.Length.ToString("D4",CultureInfo.InvariantCulture)}{Hex(label.Barcode)}^FS\n");
        z.Append(Text(3.1m,layout.FontMm(label.Barcode,2.8m),label.Barcode));
        z.Append(Text(6.9m,1.7m,label.Kind));
        for(var i=0;i<fields.Count;i++)
        {
            var field=fields[i];var top=10.8m+6m*i;
            z.Append(Text(top,1.7m,field.Caption));
            z.Append(Text(top+2.1m,layout.FontMm(field.Value),field.Value));
        }
        return z.Append($"^PQ{label.Copies}\n^XZ\n").ToString();
    }
    private static string Hex(string value)=>string.Concat(Encoding.UTF8.GetBytes(value).Select(b=>$"_{b:X2}"));
}
