using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// MD-15-C — 고객 코드는 대소문자·앞뒤 공백을 무시하고 하나뿐이다(등록·수정 모두)
public class CustomerCodeUniqueTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test customers";
    const string Actor = "itest-cc";

    static void Cleanup(Connection.AmesConnectionFactory f) =>
        Exec(f, "DELETE dbo.MD_Customer WHERE CustomerID IN ('ITCC-1','ITCC-2')");

    static void Insert(MasterDataRepository md, string id, string? code) =>
        md.InsertCustomer(id, code, "cc test", null, null, null, null, null, null, null, false, null, "ACTIVE", Actor);

    [SkippableFact]
    public void Duplicate_code_is_rejected_ignoring_case_on_insert_and_update()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        Cleanup(f!);
        try
        {
            Insert(md, "ITCC-1", "ITCC-Code");
            Assert.Throws<MasterDataRepository.DuplicateCustomerCodeException>(() => Insert(md, "ITCC-2", "itcc-code"));
            Assert.Throws<MasterDataRepository.DuplicateCustomerCodeException>(() => Insert(md, "ITCC-2", " ITCC-CODE "));

            Insert(md, "ITCC-2", "ITCC-Other");
            Assert.Throws<MasterDataRepository.DuplicateCustomerCodeException>(() =>
                md.UpdateCustomer("ITCC-2", "itcc-CODE", "cc test", null, null, null, null, null, null, null, false, null, "ACTIVE", Actor));
            // 자기 자신의 코드는 대소문자만 바꿔도 통과
            md.UpdateCustomer("ITCC-1", "itcc-code", "cc test", null, null, null, null, null, null, null, false, null, "ACTIVE", Actor);
        }
        finally { Cleanup(f!); }
    }
}
