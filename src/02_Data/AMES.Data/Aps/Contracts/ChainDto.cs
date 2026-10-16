namespace AMES.Data.Aps.Contracts;

/// /api/chain/plan 요청·응답 — docs/reference/aps-new-ui/api/chain_LQ10.json 그대로.
public sealed class ChainRequest
{
    public string BaseDate { get; set; } = "";
    public List<string> Dates { get; set; } = new();
    public int Levels { get; set; } = 4;
    public List<ChainRoot> Roots { get; set; } = new();
}

public sealed class ChainRoot
{
    public string PartNo { get; set; } = "";
    public string? PartName { get; set; }
    public string? LineCd { get; set; }
    public Dictionary<string, double> Plan { get; set; } = new();
}

public sealed class ChainResponse
{
    public string BaseDate { get; set; } = "";
    public int Roots { get; set; }
    public int Edges { get; set; }
    public List<string> Dates { get; set; } = new();
    public List<ChainLevel> Levels { get; set; } = new();
    public List<ChainLoad> Loads { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class ChainLevel
{
    public int Level { get; set; }
    public List<string> Lines { get; set; } = new();
    public List<ChainRow> Rows { get; set; } = new();
}

public sealed class ChainRow
{
    public string PartNo { get; set; } = "";
    public string? PartName { get; set; }
    public string? LineCd { get; set; }
    public int Level { get; set; }
    public double OpeningStock { get; set; }
    public bool StockCounted { get; set; }
    public bool UsesStock { get; set; } = true;
    public int? PackSize { get; set; }
    public int BatchDays { get; set; } = 1;
    public double Uph { get; set; }
    public List<string> Parents { get; set; } = new();
    public List<string> Leads { get; set; } = new();
    public List<ChainDay> Days { get; set; } = new();
    public List<Peg> Pegs { get; set; } = new();
    public int PegCount { get; set; }
    public List<double> PegOthers { get; set; } = new();
    public int ShortDays { get; set; }
}

public sealed class ChainDay
{
    public string Date { get; set; } = "";
    public double Requirement { get; set; }
    public double Opening { get; set; }
    public double Target { get; set; }
    public double Plan { get; set; }
    public double Closing { get; set; }
    public double Overdue { get; set; }
    public double Shortage { get; set; }
}

public sealed class Peg
{
    public string PartNo { get; set; } = "";
    public string? PartName { get; set; }
    public List<double> Days { get; set; } = new();
    public double Total { get; set; }
}

public sealed class ChainLoad
{
    public string LineCd { get; set; } = "";
    public string Date { get; set; } = "";
    public double Qty { get; set; }
    public double Hours { get; set; }
    public double Capacity { get; set; }
    public bool Over { get; set; }
}

public sealed class StageSuggestResponse
{
    public string Root { get; set; } = "";
    public int Roots { get; set; }
    public int Edges { get; set; }
    public List<StageNode> Nodes { get; set; } = new();
}

public sealed class StageNode
{
    public string LineCd { get; set; } = "";
    public string? LineName { get; set; }
    public string? ParentLine { get; set; }
    public int Level { get; set; }
    public int Parts { get; set; }
    public List<string>? AlsoUnder { get; set; }
}
