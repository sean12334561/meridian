namespace Meridian.Api.Features.Imports;

/// <summary>The JSON stored in imports.report, written by ImportReportWriter.</summary>
public class ImportReportDto
{
    public required ImportReportTotals Totals { get; init; }
    public required int ConflictsOpened { get; init; }
    public required List<VoteReportDto> Votes { get; init; }
}

public class ImportReportTotals
{
    public required int Rows { get; init; }
    public required int New { get; init; }
    public required int Unchanged { get; init; }
    public required int Duplicate { get; init; }
    public required int Invalid { get; init; }
    public required int Contradiction { get; init; }
    public required int Conflict { get; init; }
    public required int PreviouslyDecided { get; init; }
    public required int AlreadyPending { get; init; }
    public required int Normalized { get; init; }
}

public class VoteReportDto
{
    public required string VoteRef { get; init; }
    public required int New { get; init; }
    public required int Unchanged { get; init; }
    public required int Duplicate { get; init; }
    public required int Invalid { get; init; }
    public required int Contradiction { get; init; }
    public required int Conflict { get; init; }
    public required int PreviouslyDecided { get; init; }
    public required VoteDetailsStatus Details { get; init; }
}

public enum VoteDetailsStatus { Same, Conflict, PreviouslyDecided, DiffersInFile, NotStored }
