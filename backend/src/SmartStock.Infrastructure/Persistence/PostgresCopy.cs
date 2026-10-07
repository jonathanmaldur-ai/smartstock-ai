using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SmartStock.Infrastructure.Persistence;

/// <summary>
/// Gravação em massa pelo COPY binário do PostgreSQL (centenas de milhares de linhas em segundos).
/// Usa a conexão e a transação correntes do contexto.
/// </summary>
internal static class PostgresCopy
{
    public static async Task WriteAsync<T>(
        SmartStockDbContext db,
        string copyCommand,
        IEnumerable<T> rows,
        Func<NpgsqlBinaryImporter, T, CancellationToken, Task> writeRow,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await db.Database.OpenConnectionAsync(cancellationToken);

        await using var writer = await connection.BeginBinaryImportAsync(copyCommand, cancellationToken);
        foreach (var row in rows)
        {
            await writer.StartRowAsync(cancellationToken);
            await writeRow(writer, row, cancellationToken);
        }
        await writer.CompleteAsync(cancellationToken);
    }
}
