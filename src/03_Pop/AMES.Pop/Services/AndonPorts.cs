using AMES.Contracts.Dto;

namespace AMES.Pop.Services;

/// <summary>안돈 워크플로가 보는 저장소 — 테스트에서 대체 가능하도록 좁게 정의.</summary>
internal interface IAndonStore
{
    AndonCallDto? GetOpenForLine(string lineId);
    int  Raise(string lineId, string? equipId, string employeeNo);
    bool IsLineSupervisor(string lineId, string workerNo);
    List<AndonCauseDto> ListCauses();
    List<AndonDeptDto>  ListDepts();
    void AcknowledgeBySupervisor(int andonId, string workerNo, string? name);
    void CallDepts(int andonId, string causeCode, IEnumerable<string> deptCodes, string calledBy);
    void RecordArrival(int deptCallId, string workerNo, string? name);
    void AckDept(int deptCallId);
    void Resolve(int andonId, string? causeCode);
}

/// <summary>배지에 이름이 없을 때 사번으로 이름을 찾는다. 모르면 null.</summary>
internal interface IBadgeResolver
{
    string? ResolveName(string workerNo);
}
