using Xunit;

namespace AMES.Data.Tests;

/// <summary>
/// 차감 DB 테스트는 같은 라인 위치(I1) 의 WH_Inventory 행을 UPDLOCK 으로 잡고 하나는 트랜잭션 안에서 DDL 까지 돌린다 —
/// 클래스끼리 병렬로 돌면 서로 잠금을 기다리거나 교착한다. 한 컬렉션으로 묶어 차례로 돌린다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackflushDbCollection
{
    public const string Name = "Backflush DB";
}
