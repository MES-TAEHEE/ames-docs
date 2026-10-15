using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 10-16 사용자 결정 — MNT 담당자(Technician) 콤보는 부서가 보전(MAINTENANCE)인 사용자만
public class MntTechniciansTests
{
    [Theory]
    [InlineData("MAINTENANCE", true)]
    [InlineData(" maintenance ", true)]
    [InlineData("PRODUCTION", false)]
    [InlineData("IT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_maintenance_department_users_are_technicians(string? department, bool expected)
        => Assert.Equal(expected, MntTechnicians.IsCandidate(department));
}
