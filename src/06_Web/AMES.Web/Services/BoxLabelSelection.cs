namespace AMES.Web.Services;

public sealed class BoxLabelSelection
{
    public string Delivery { get; set; }="";
    public HashSet<long> Ids { get; set; }=[];
}
