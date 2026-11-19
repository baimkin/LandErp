using System.Net.Sockets;
using System.Text.Json;
using LandErp.Server.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class WorkspaceFailureDiagnosticsTests
{
    [TestMethod]
    [DataRow("23505", "PG_23505")]
    [DataRow("23503", "PG_23503")]
    [DataRow("23502", "PG_23502")]
    [DataRow("42501", "PG_42501")]
    [DataRow("40P01", "PG_40P01")]
    [DataRow("40001", "PG_40001")]
    [DataRow("57014", "PG_57014")]
    public void DatabaseMetadataDistinguishesConstraintAccessDeadlockAndCancellation(string state, string expected)
    {
        const string secret = "SYNTHETIC_PRIVATE_TEXT_AND_PASSWORD";
        var error = new DbUpdateException(secret, new PostgresException(secret, "ERROR", "ERROR", state,
            detail: secret, hint: secret, tableName: "case_negotiations", columnName: "effective_at", constraintName: "fk_negotiation_case"));
        var details = WorkspaceFailureDiagnostics.Inspect(error);
        Assert.AreEqual(expected, details.Code);
        Assert.AreEqual(state, details.SqlState);
        Assert.AreEqual("case_negotiations", details.Table);
        Assert.AreEqual("fk_negotiation_case", details.Constraint);
        Assert.IsFalse(JsonSerializer.Serialize(details).Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public void TimeoutConnectionConcurrencyAndUnknownRemainDistinctWithoutMessages()
    {
        Assert.AreEqual("DB_TIMEOUT", WorkspaceFailureDiagnostics.Inspect(new NpgsqlException("private", new TimeoutException("private"))).Code);
        Assert.AreEqual("DB_CONNECTION", WorkspaceFailureDiagnostics.Inspect(new NpgsqlException("private", new SocketException((int)SocketError.ConnectionRefused))).Code);
        Assert.AreEqual("DB_CONCURRENCY", WorkspaceFailureDiagnostics.Inspect(new DbUpdateConcurrencyException("private")).Code);
        Assert.AreEqual("TIMEOUT", WorkspaceFailureDiagnostics.Inspect(new TimeoutException("private")).Code);
        Assert.AreEqual("CANCELLED", WorkspaceFailureDiagnostics.Inspect(new OperationCanceledException("private")).Code);
        Assert.AreEqual("INVALID_OPERATION", WorkspaceFailureDiagnostics.Inspect(new InvalidOperationException("private")).Code);
        Assert.AreEqual("UNEXPECTED_ERROR", WorkspaceFailureDiagnostics.Inspect(new UnclassifiedFailure("private")).Code);
    }

    [TestMethod]
    public void UnexpectedProviderIdentifiersAreOmittedAndInnerTraversalIsBounded()
    {
        var details = WorkspaceFailureDiagnostics.Inspect(new PostgresException("private", "ERROR", "ERROR", "private-invalid-state",
            tableName: "table\nPRIVATE", constraintName: new string('a', 100), columnName: "value with private text"));
        Assert.AreEqual("PG_ERROR", details.Code);
        Assert.IsNull(details.SqlState); Assert.IsNull(details.Table); Assert.IsNull(details.Constraint); Assert.IsNull(details.Column);
        Exception nested = new TimeoutException("private");
        for (int i = 0; i < 100; i++) nested = new UnclassifiedFailure("private", nested);
        details = WorkspaceFailureDiagnostics.Inspect(nested);
        Assert.AreEqual("UNEXPECTED_ERROR", details.Code);
        Assert.AreEqual(nameof(UnclassifiedFailure), details.InnerFailureType);
    }
    private sealed class UnclassifiedFailure(string message, Exception? inner = null) : Exception(message, inner);
}
