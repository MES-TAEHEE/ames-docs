using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Services;

/// <summary>IAndonStore → PopServices.Andon 위임.</summary>
internal sealed class RepoAndonStore : IAndonStore
{
    public AndonCallDto? GetOpenForLine(string lineId) => PopServices.Andon.GetOpenForLine(lineId);

    public int Raise(string lineId, string? equipId, string employeeNo)
        => PopServices.Andon.Raise(lineId, equipId,
                                   triggerSource: "MANUAL", ruleId: "INJ-008-MANUAL", severity: null,
                                   employeeNo);

    public bool IsLineSupervisor(string lineId, string workerNo) => PopServices.Andon.IsLineSupervisor(lineId, workerNo);
    public List<AndonCauseDto> ListCauses() => PopServices.Andon.ListCauses();
    public List<AndonDeptDto>  ListDepts()  => PopServices.Andon.ListDepts();
    public List<AndonSeverityDto> ListSeverities() => PopServices.Andon.ListSeverities();

    public void AcknowledgeBySupervisor(int andonId, string workerNo, string? name)
        => PopServices.Andon.AcknowledgeBySupervisor(andonId, workerNo, name);

    public void CallDepts(int andonId, string causeCode, string severity, IEnumerable<string> deptCodes, string calledBy)
        => PopServices.Andon.CallDepts(andonId, causeCode, severity, deptCodes, calledBy);

    public void RecordArrival(int deptCallId, string workerNo, string? name)
        => PopServices.Andon.RecordArrival(deptCallId, workerNo, name);

    public void AckDept(int deptCallId) => PopServices.Andon.AckDept(deptCallId);
    public void Resolve(int andonId, string? causeCode, string? severity) => PopServices.Andon.Resolve(andonId, causeCode, severity);
}

/// <summary>사번 → 이름. 로그인과 같은 순서(웹 계정 우선, 그 다음 MD_Worker).</summary>
internal sealed class RepoBadgeResolver : IBadgeResolver
{
    public string? ResolveName(string workerNo)
    {
        try
        {
            return PopServices.Auth.FindByEmployeeNo(workerNo)?.EmployeeName
                ?? PopServices.Workers.FindByWorkerNo(workerNo)?.EmployeeName;
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            // 이름은 표시용이다. 조회가 죽었다고 도착 기록까지 막지 않는다.
            return null;
        }
    }
}
