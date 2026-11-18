namespace AMES.Contracts.Dto;

public sealed record ShippingOrderInput(int SoId,decimal Qty);
public sealed record ShippingOrderLineDto(int OrderLineId,int SoId,string SoNumber,string PartNo,string? PartName,decimal Qty,string? Unit,int? PalletQty,bool ToteFlag,DateTime? RequestedDate=null);
public sealed record ShippingOrderDto(int OrderId,string OrderNo,string CustomerId,DateTime ShipDate,string Status,int? DeliveryId,string? DeliveryNo,List<ShippingOrderLineDto> Lines);
public sealed record DeliveryRequirementDto(string PartNo,string? PartName,decimal RequiredQty,decimal LoadedQty,decimal PreparedQty=0,decimal StockShortageQty=0)
{
    public decimal StagingQty=>Math.Max(0,PreparedQty-LoadedQty);
}
public sealed record DeliveryLoadDto(string RootLotNo,string LotNo,string PartNo,decimal Qty);
public sealed record ShippingDeliveryDto(int DeliveryId,string DeliveryNo,string CustomerId,DateTime ShipDate,string? TrailerNo,string Status,List<string> OrderNos,List<DeliveryRequirementDto> Parts,List<DeliveryLoadDto> Loads,List<DeliveryLoadDto>? Preparations=null,bool SupportsPreparation=false,List<DispatchRecommendationDto>? Recommendations=null);
public sealed record ShippingDeliveryListDto(int DeliveryId,string DeliveryNo,string CustomerId,DateTime ShipDate,string? TrailerNo,string Status,int OrderCount,int PartCount);
public sealed record FifoWarningDto(string PartNo,string LotNo,string? PalletNo,string LocationNo,DateTime ReceivedAt);
public sealed record DeliveryLoadRequest(string DeliveryBarcode,string PalletBarcode,bool AllowNonFifo=false);
public sealed record DeliveryCompleteRequest(string DeliveryBarcode,bool AllowNonFifo=false);
public sealed record ShippingActionResult(bool Success,string Message,ShippingDeliveryDto? Delivery=null,List<FifoWarningDto>? FifoWarnings=null);

public sealed record DispatchRecommendationDto(string PartNo,string Barcode,string UnitType,string LocationNo,string? LocationName,decimal Qty,DateTime? ReceivedAt);
