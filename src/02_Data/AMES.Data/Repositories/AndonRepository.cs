using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Writes PR_AndonCall + PR_AndonPush for INJ-08 (and INJ-05 auto-trigger
/// when defect rate ≥ 5%).
/// </summary>
public sealed class AndonRepository
{
    private readonly AmesConnectionFactory _factory;
    public AndonRepository(AmesConnectionFactory f) => _factory = f;

    /// <summary>Opens a new Andon call and returns its AndonID.</summary>
    public int Raise(string lineId, string? equipId, string triggerSource,
                     string ruleId, string severity, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_AndonCall
                (LineID, EquipID, TriggerSource, RuleID, Severity, TriggeredAt,
                 Status, CreatedBy, CreatedTS)
            OUTPUT INSERTED.AndonID
            VALUES (@L, @E, @T, @R, @S, SYSDATETIME(), 'OPEN', @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@L",  SqlDbType.VarChar, 20).Value = lineId;
        cmd.Parameters.Add("@E",  SqlDbType.VarChar, 20).Value = (object?)equipId ?? DBNull.Value;
        cmd.Parameters.Add("@T",  SqlDbType.VarChar, 20).Value = triggerSource;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar, 20).Value = ruleId;
        cmd.Parameters.Add("@S",  SqlDbType.VarChar, 10).Value = severity;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
        var id = (int)cmd.ExecuteScalar()!;

        // Push stubs — recipients hard-coded for now; replaced when SYS_NotificationRule wires up.
        RecordPush(id, "SUPERVISOR", "PDA",   employeeNo);
        RecordPush(id, "LINE-LEAD",  "EMAIL", employeeNo);
        return id;
    }

    public void RecordPush(int andonId, string recipient, string channel, string employeeNo)
    {
        const string sql = """
            INSERT INTO dbo.PR_AndonPush
                (AndonID, Recipient, Channel, CreatedBy, CreatedTS)
            VALUES (@A, @R, @C, @By, SYSDATETIME());
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@A",  SqlDbType.Int        ).Value = andonId;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar,100).Value = recipient;
        cmd.Parameters.Add("@C",  SqlDbType.VarChar, 20).Value = channel;
        cmd.Parameters.Add("@By", SqlDbType.VarChar, 50).Value = employeeNo;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Supervisor PIN confirmed — Ack the andon.</summary>
    public void Acknowledge(int andonId, string supervisorUserId,
                            string reasonCode, string correctiveAction)
    {
        const string sql = """
            UPDATE dbo.PR_AndonCall
            SET    AckedBy           = @U,
                   AckedAt           = SYSDATETIME(),
                   ReasonCode        = @R,
                   CorrectiveAction  = @C,
                   Status            = 'ACKED',
                   ModifiedTS        = SYSDATETIME()
            WHERE  AndonID = @ID AND AckedAt IS NULL;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int          ).Value = andonId;
        cmd.Parameters.Add("@U",  SqlDbType.NVarChar, 450).Value = supervisorUserId;
        cmd.Parameters.Add("@R",  SqlDbType.VarChar,   30).Value = reasonCode;
        cmd.Parameters.Add("@C",  SqlDbType.NVarChar,  500).Value = correctiveAction;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Resume production — closes the andon and stamps downtime seconds.</summary>
    public void Resume(int andonId)
    {
        const string sql = """
            UPDATE dbo.PR_AndonCall
            SET    ResumedAt   = SYSDATETIME(),
                   DowntimeSec = DATEDIFF(SECOND, TriggeredAt, SYSDATETIME()),
                   Status      = 'RESUMED',
                   ModifiedTS  = SYSDATETIME()
            WHERE  AndonID = @ID;
            """;
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = andonId;
        cmd.ExecuteNonQuery();
    }
}
