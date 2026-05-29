using System.Data;
using AMES.Data.Connection;
using Microsoft.Data.SqlClient;

namespace AMES.Data.Repositories;

/// <summary>
/// Catch-all lookup repository for small master tables (MD_Line, MD_Recipe.CycleTime, etc.)
/// that don't yet warrant their own repository.
/// </summary>
public sealed class MasterDataRepository
{
    private readonly AmesConnectionFactory _factory;
    public MasterDataRepository(AmesConnectionFactory f) => _factory = f;

    public string? GetLineName(string lineId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT TOP 1 LineName FROM dbo.MD_Line WHERE LineID = @L;", conn);
        cmd.Parameters.Add("@L", SqlDbType.VarChar, 20).Value = lineId;
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// Returns the (CycleTime,Pressure,Temp) for a recipe — used by INJ-04
    /// to populate the [CT] auto-fill key.
    /// </summary>
    public int? GetRecipeCycleTime(string recipeId)
    {
        using var conn = _factory.OpenConnection();
        using var cmd  = new SqlCommand(
            "SELECT TOP 1 CycleTime FROM dbo.MD_Recipe WHERE RecipeID = @R;", conn);
        cmd.Parameters.Add("@R", SqlDbType.VarChar, 20).Value = recipeId;
        var v = cmd.ExecuteScalar();
        return v is int i ? i : null;
    }
}
