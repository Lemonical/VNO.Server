using System;
using Microsoft.Extensions.Logging;
using VNO.Server.Admin;
using Xunit;

namespace VNO.Server.Tests;

/// <summary>
/// Tests for the issue log that feeds the console dashboard
/// </summary>
public sealed class IssueLogTests
{
    [Fact]
    public void Captures_warnings_and_errors_only()
    {
        using var log = new IssueLog();
        var logger = ((ILoggerProvider)log).CreateLogger("Test.Category");

        logger.LogInformation("routine");
        logger.LogWarning("heartbeat slow");
        logger.LogError("relay failed");

        Assert.Collection(
            log.Issues,
            first =>
            {
                Assert.Equal(IssueLevel.Warning, first.Level);
                Assert.Equal("heartbeat slow", first.Text);
            },
            second => Assert.Equal(IssueLevel.Error, second.Level));
    }

    [Fact]
    public void Raises_an_event_per_issue_with_the_category_in_the_detail()
    {
        using var log = new IssueLog();
        IssueEntry? raised = null;
        log.IssueRaised += (_, entry) => raised = entry;

        var logger = ((ILoggerProvider)log).CreateLogger("VNO.Server.Services.GameHost");
        logger.LogError(new InvalidOperationException("boom"), "relay failed");

        Assert.NotNull(raised);
        Assert.Contains("VNO.Server.Services.GameHost", raised.Detail, StringComparison.Ordinal);
        Assert.Contains("boom", raised.Detail, StringComparison.Ordinal);
    }
}
