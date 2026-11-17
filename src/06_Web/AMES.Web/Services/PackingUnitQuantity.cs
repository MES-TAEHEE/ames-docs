namespace AMES.Web.Services;

public static class PackingUnitQuantity
{
    public static decimal FromCount(decimal count,decimal packingQuantity,decimal remaining)
    {
        if(count<0 || count>1000 || decimal.Truncate(count)!=count || packingQuantity<=0)
            throw new ArgumentException("Enter a whole unit count and set the packing quantity first.");
        var quantity=count*packingQuantity;
        if(quantity>remaining)throw new ArgumentException("Unit quantity exceeds the remaining PO balance.");
        return quantity;
    }
}
