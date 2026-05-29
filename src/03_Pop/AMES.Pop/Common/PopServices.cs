using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;

namespace AMES.Pop.Common;

/// <summary>
/// Process-wide service registry. Stands in for a DI container until
/// the codebase grows enough to justify Microsoft.Extensions.Hosting.
///
/// Initialised once in Program.Main and then accessed via the static
/// properties below. All repositories share a single AmesConnectionFactory
/// (which is itself just a connection-string holder, so this is cheap).
/// </summary>
internal static class PopServices
{
    public static AmesConnectionFactory  ConnectionFactory { get; private set; } = null!;
    public static AuthRepository         Auth              { get; private set; } = null!;
    public static PopSessionRepository   Sessions          { get; private set; } = null!;
    public static PopAuthService         PopAuth           { get; private set; } = null!;
    public static WorkOrderRepository    WorkOrders        { get; private set; } = null!;
    public static ProductionRepository   Production        { get; private set; } = null!;
    public static DefectRepository       Defects           { get; private set; } = null!;
    public static MoldRepository         Molds             { get; private set; } = null!;
    public static EquipmentRepository    Equipment         { get; private set; } = null!;
    public static AndonRepository        Andon             { get; private set; } = null!;
    public static DashboardRepository    Dashboard         { get; private set; } = null!;
    public static MasterDataRepository   Master            { get; private set; } = null!;
    public static FabricRepository       Fabric            { get; private set; } = null!;
    public static BondRepository         Bond              { get; private set; } = null!;

    public static void Initialize()
    {
        ConnectionFactory = new AmesConnectionFactory(AppConfig.Current.ConnectionString);
        Auth              = new AuthRepository      (ConnectionFactory);
        Sessions          = new PopSessionRepository(ConnectionFactory);
        PopAuth           = new PopAuthService      (Auth, Sessions);

        WorkOrders        = new WorkOrderRepository (ConnectionFactory);
        Production        = new ProductionRepository(ConnectionFactory);
        Defects           = new DefectRepository    (ConnectionFactory);
        Molds             = new MoldRepository      (ConnectionFactory);
        Equipment         = new EquipmentRepository (ConnectionFactory);
        Andon             = new AndonRepository     (ConnectionFactory);
        Dashboard         = new DashboardRepository (ConnectionFactory);
        Master            = new MasterDataRepository(ConnectionFactory);
        Fabric            = new FabricRepository    (ConnectionFactory);
        Bond              = new BondRepository      (ConnectionFactory);
    }
}
