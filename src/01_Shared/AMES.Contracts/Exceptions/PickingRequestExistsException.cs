using AMES.Contracts.Dto;

namespace AMES.Contracts.Exceptions;

public sealed class PickingRequestExistsException(IReadOnlyList<ExistingPickingRequestDto> existingOrders)
    : InvalidOperationException("Open picking orders already exist for this location and part.")
{
    public IReadOnlyList<ExistingPickingRequestDto> ExistingOrders { get; } = existingOrders;
}
