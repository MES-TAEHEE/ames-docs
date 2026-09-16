using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public sealed class AuthApi(PdaApi api)
{
    public Task<(string Token, PopSessionDto Session, string? Reason)?> LoginAsync(
        string employeeNo, string pin,
        string terminalId = "PDA-DEV-01",
        string lineId = "LINE-INJ-01",
        string shiftCode = "A")
        => api.LoginAsync(employeeNo, pin, terminalId, lineId, shiftCode);

    public Task LogoutAsync() => api.LogoutAsync();
    public Task<PopSessionDto?> MeAsync() => api.MeAsync();
}
