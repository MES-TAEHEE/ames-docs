namespace AMES.Contracts.Dto;

public sealed record ShippingOrderInput(int SoId,decimal Qty);
public sealed record ShippingOrderLineDto(int OrderLineId,int SoId,string SoNumber,string PartNo,string? PartName,decimal Qty,string? Unit,int? PalletQty,bool ToteFlag,DateTime? RequestedDate=null);
public sealed record ShippingOrderDto(int OrderId,string OrderNo,string CustomerId,DateTime ShipDate,string Status,int? DeliveryId,string? DeliveryNo,List<ShippingOrderLineDto> Lines);
public sealed record DeliveryRequirementDto(string PartNo,string? PartName,decimal RequiredQty,decimal LoadedQty);
public sealed record DeliveryLoadDto(string RootLotNo,string LotNo,string PartNo,decimal Qty);
public sealed record ShippingDeliveryDto(int DeliveryId,string DeliveryNo,string CustomerId,DateTime ShipDate,string? TrailerNo,string Status,List<string> OrderNos,List<DeliveryRequirementDto> Parts,List<DeliveryLoadDto> Loads);
public sealed record ShippingDeliveryListDto(int DeliveryId,string DeliveryNo,string CustomerId,DateTime ShipDate,string? TrailerNo,string Status,int OrderCount,int PartCount);
public sealed record DeliveryLoadRequest(string DeliveryBarcode,string PalletBarcode);
public sealed record DeliveryCompleteRequest(string DeliveryBarcode);
public sealed record ShippingActionResult(bool Success,string Message,ShippingDeliveryDto? Delivery=null);
