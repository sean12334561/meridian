using Meridian.Api.Shared.Data;
using Meridian.Api.Shared.Data.Entities;
using Meridian.Api.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Api.Features.Imports.Parsing;

/// <summary>
/// Phase 1: writes every row of the file to import_rows. Runs in one transaction that also moves the
/// import to Checking, so a crash leaves no rows and the phase can simply run again.
/// </summary>
public class ImportParser(IDbContextFactory<AppDbContext> dbFactory, IFileStore files)
{
    private const int BatchSize = 10_000;

    private const string CopyCommand = """
        COPY import_rows (import_id, line_no, raw_text, vote_ref, session, vote_date, title, country_code,
                          country_name, ballot, published_at, normalizations, outcome, reason)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task ParseAsync(Import import, CancellationToken ct)
    {
        int estimatedRows = await CountRowsAsync(import.BlobKey, ct);
        await MarkStartedAsync(import.Id, estimatedRows, ct);

        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        List<MemberState> memberStates = await db.MemberStates.AsNoTracking().ToListAsync(ct);
        RowValidator validator = new(memberStates);

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using Stream stream = await files.OpenReadAsync(import.BlobKey, ct);
        using CsvRowReader reader = new(stream);
        CsvColumns columns = await reader.ReadColumnsAsync();

        int total = 0;
        List<ImportRow> batch = new(BatchSize);
        while (await reader.ReadRowAsync() is { } row)
        {
            batch.Add(validator.Validate(import.Id, row, columns));

            if (batch.Count == BatchSize)
            {
                await CopyAsync(connection, batch, ct);
                total += batch.Count;
                batch.Clear();
                await ReportProgressAsync(import.Id, total, ct);
            }
        }

        await CopyAsync(connection, batch, ct);
        total += batch.Count;

        if (total == 0)
        {
            throw new ImportFileException("The file has no data rows.");
        }

        await db.Imports
            .Where(i => i.Id == import.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Phase, ImportPhase.Checking)
                .SetProperty(i => i.ProcessedRows, total)
                .SetProperty(i => i.TotalRows, total), ct);

        await transaction.CommitAsync(ct);
    }

    // For the progress bar only: quoted fields and blank lines can make it slightly off, and the end corrects it
    private async Task<int> CountRowsAsync(string blobKey, CancellationToken ct)
    {
        await using Stream stream = await files.OpenReadAsync(blobKey, ct);

        byte[] buffer = new byte[81_920];
        int lines = 0;
        int read;
        byte last = (byte)'\n';
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            lines += buffer.AsSpan(0, read).Count((byte)'\n');
            last = buffer[read - 1];
        }

        int lineCount = last == '\n' ? lines : lines + 1;

        return Math.Max(lineCount - 1, 0);
    }

    private async Task MarkStartedAsync(Guid importId, int estimatedRows, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        await db.Imports
            .Where(i => i.Id == importId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, ImportStatus.Running)
                .SetProperty(i => i.Phase, ImportPhase.Parsing)
                .SetProperty(i => i.ProcessedRows, 0)
                .SetProperty(i => i.TotalRows, estimatedRows), ct);
    }

    // Its own context, so it commits and shows while the phase's transaction is still open
    private async Task ReportProgressAsync(Guid importId, int processedRows, CancellationToken ct)
    {
        await using AppDbContext db = await dbFactory.CreateDbContextAsync(ct);
        await db.Imports
            .Where(i => i.Id == importId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProcessedRows, processedRows), ct);
    }

    private static async Task CopyAsync(NpgsqlConnection connection, List<ImportRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using NpgsqlBinaryImporter writer = await connection.BeginBinaryImportAsync(CopyCommand, ct);

        foreach (ImportRow row in rows)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(row.ImportId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(row.LineNo, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(row.RawText, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(row.VoteRef, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(row.Session, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(row.VoteDate, NpgsqlDbType.Date, ct);
            await writer.WriteAsync(row.Title, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(row.CountryCode, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(row.CountryName, NpgsqlDbType.Text, ct);
            await writer.WriteAsync(row.Ballot, NpgsqlDbType.Char, ct);
            await writer.WriteAsync(row.PublishedAt, NpgsqlDbType.TimestampTz, ct);
            await writer.WriteAsync(row.Normalizations, NpgsqlDbType.Jsonb, ct);
            await writer.WriteAsync(row.Outcome?.ToString(), NpgsqlDbType.Text, ct);
            await writer.WriteAsync(row.Reason, NpgsqlDbType.Text, ct);
        }

        await writer.CompleteAsync(ct);
    }
}
