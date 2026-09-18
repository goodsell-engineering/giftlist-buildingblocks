using BuildingBlocks.Testing;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.UnitTests.Testing;

/// <summary>
/// GL-37: gap-filling unit coverage for the type promoted from three services' byte-identical
/// <c>Support/LogCapture.cs</c> copies. No Testcontainers suite needed — <see cref="LogCapture"/>
/// never touches Mongo, RabbitMQ or a real host, so <c>BuildingBlocks.UnitTests</c> is the right
/// home rather than <c>BuildingBlocks.IntegrationTests</c> (contrast <c>QueueCleanupTests</c>,
/// which genuinely needs a broker).
/// </summary>
public sealed class LogCaptureTests
{
    [Fact]
    public void Log_ShouldCaptureTheRenderedMessage()
    {
        // Arrange
        var capture = new LogCapture();
        var logger = capture.CreateLogger("test-category");

        // Act
        logger.LogInformation("hello {Name}", "world");

        // Assert
        var entry = Assert.Single(capture.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("hello world", entry.Message);
    }

    [Fact]
    public void Log_ShouldCaptureEveryLevel_IncludingTraceAndCritical()
    {
        // Arrange — CONVENTIONS.md "Testing"-style negative assertions ("was NOT logged") only
        // hold if LogCapture itself never filters by level; IsEnabled always returning true is
        // what makes that true.
        var capture = new LogCapture();
        var logger = capture.CreateLogger("test-category");

        // Act
        logger.LogTrace("trace entry");
        logger.LogCritical("critical entry");

        // Assert
        Assert.Equal(2, capture.Entries.Count);
        Assert.Contains(capture.Entries, e => e.Level == LogLevel.Trace && e.Message == "trace entry");
        Assert.Contains(capture.Entries, e => e.Level == LogLevel.Critical && e.Message == "critical entry");
    }

    [Fact]
    public void Entries_ShouldStayEmpty_WhenNothingWasLogged()
    {
        // Arrange
        var capture = new LogCapture();

        // Act — no logging at all; CreateLogger alone must not produce an entry.
        _ = capture.CreateLogger("unused-category");

        // Assert
        Assert.Empty(capture.Entries);
    }

    [Fact]
    public async Task Log_ShouldNotLoseOrCorruptEntries_WhenLoggedConcurrentlyFromManyThreads()
    {
        // Arrange — one shared logger, many writers, the way a real host's DI-provided ILogger
        // is shared across concurrently-handled requests/messages.
        var capture = new LogCapture();
        var logger = capture.CreateLogger("concurrent-category");
        const int writerCount = 50;
        const int messagesPerWriter = 20;

        // Act
        var writers = Enumerable.Range(0, writerCount).Select(writerIndex => Task.Run(() =>
        {
            for (var messageIndex = 0; messageIndex < messagesPerWriter; messageIndex++)
            {
                logger.LogInformation("writer {Writer} message {Message}", writerIndex, messageIndex);
            }
        }));
        await Task.WhenAll(writers);

        // Assert — every single message survived, none dropped and none merged with another.
        Assert.Equal(writerCount * messagesPerWriter, capture.Entries.Count);
        for (var writerIndex = 0; writerIndex < writerCount; writerIndex++)
        {
            for (var messageIndex = 0; messageIndex < messagesPerWriter; messageIndex++)
            {
                var expected = $"writer {writerIndex} message {messageIndex}";
                Assert.Contains(capture.Entries, e => e.Message == expected);
            }
        }
    }
}
