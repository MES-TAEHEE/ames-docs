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
    public static string                 ConnectionString  { get; private set; } = "";
    public static AuthRepository         Auth              { get; private set; } = null!;
    public static WorkerRepository       Workers           { get; private set; } = null!;
    public static PopSessionRepository   Sessions          { get; private set; } = null!;
    public static PopAuthService         PopAuth           { get; private set; } = null!;
    public static WorkOrderRepository    WorkOrders        { get; private set; } = null!;
    public static ProductionRepository   Production        { get; private set; } = null!;
    public static DefectRepository       Defects           { get; private set; } = null!;
    public static MoldRepository         Molds             { get; private set; } = null!;
    public static EquipmentRepository    Equipment         { get; private set; } = null!;
    public static AndonRepository        Andon             { get; private set; } = null!;
    public static MasterDataRepository   Master            { get; private set; } = null!;
    public static QcRepository           Qc                { get; private set; } = null!;
    public static InjLotRepository      InjLots           { get; private set; } = null!;
    public static ImgLotRepository      ImgLots           { get; private set; } = null!;
    public static PntLotRepository      PntLots           { get; private set; } = null!;
    public static ReworkRepository      Rework            { get; private set; } = null!;
    public static PalletStockRepository Pallets           { get; private set; } = null!;

    /// <summary>설정 PopTerminal:Db:ConnectTimeoutSeconds 가 있으면 접속문자열의 Connect Timeout 을 덮어쓴다.</summary>
    internal static string ConnectionStringWithTimeout(string connectionString, int timeoutSeconds)
    {
        if (timeoutSeconds <= 0) return connectionString;
        var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { ConnectTimeout = timeoutSeconds };
        return b.ConnectionString;
    }

    public static void Initialize()
        => Initialize(ConnectionStringWithTimeout(AppConfig.Current.ConnectionString, AppConfig.Current.DbConnectTimeoutSeconds));

    /// <summary>접속문자열을 직접 주는 초기화 — 설정 파일이 없는 테스트가 쓴다.</summary>
    internal static void Initialize(string connectionString)
    {
        ConnectionString  = connectionString;
        ConnectionFactory = new AmesConnectionFactory(connectionString);
        // "오늘"·시각 표시는 DB 서버 시각 기준 — 실제 동기화는 로그인 화면이 한다(DB 가 죽어 있으면 Offset 0 으로 종전 동작)
        DbClock.Configure(ConnectionFactory);
        Auth              = new AuthRepository      (ConnectionFactory);
        Workers           = new WorkerRepository    (ConnectionFactory);
        Sessions          = new PopSessionRepository(ConnectionFactory);
        PopAuth           = new PopAuthService      (Auth, Workers, Sessions);

        WorkOrders        = new WorkOrderRepository (ConnectionFactory);
        Production        = new ProductionRepository(ConnectionFactory);
        Defects           = new DefectRepository    (ConnectionFactory);
        Molds             = new MoldRepository      (ConnectionFactory);
        Equipment         = new EquipmentRepository (ConnectionFactory);
        Andon             = new AndonRepository     (ConnectionFactory);
        Master            = new MasterDataRepository(ConnectionFactory);
        Qc                = new QcRepository        (ConnectionFactory);
        InjLots           = new InjLotRepository    (ConnectionFactory);
        ImgLots           = new ImgLotRepository    (ConnectionFactory);
        PntLots           = new PntLotRepository    (ConnectionFactory);
        Rework            = new ReworkRepository    (ConnectionFactory);
        Pallets           = new PalletStockRepository(ConnectionFactory);
    }
}
