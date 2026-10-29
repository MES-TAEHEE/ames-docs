using System.Globalization;

namespace AMES.Pda.Components.Pages.Wh;

public partial class WhPutAway
{
    internal static string DateText(DateTime? value, bool finishedGoods = false) =>
        value?.ToString(finishedGoods ? "MM/dd/yyyy" : "MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
}
