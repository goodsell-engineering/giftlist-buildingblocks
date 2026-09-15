using BuildingBlocks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace BuildingBlocks.UnitTests.Persistence;

public sealed class MongoConfigurationExtensionsTests
{
    [Fact]
    public void AddBuildingBlocksMongo_ShouldThrow_WhenConnectionStringIsMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksMongo(configuration, "giftlist"));

        // Assert
        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains(MongoConfigurationExtensions.ConnectionStringConfigKey, invalidOperationException.Message);
    }

    [Fact]
    public void AddBuildingBlocksMongo_ShouldThrow_WhenConnectionStringIsBlank()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MongoConfigurationExtensions.ConnectionStringConfigKey] = "   ",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksMongo(configuration, "giftlist"));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void AddBuildingBlocksMongo_ShouldThrow_WhenDatabaseNameIsMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MongoConfigurationExtensions.ConnectionStringConfigKey] = "mongodb://localhost:27017",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksMongo(configuration, string.Empty));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void AddBuildingBlocksMongo_ShouldNotThrow_WhenConfigurationIsValid()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MongoConfigurationExtensions.ConnectionStringConfigKey] = "mongodb://localhost:27017",
            })
            .Build();

        // Act
        var exception = Record.Exception(
            () => services.AddBuildingBlocksMongo(configuration, "giftlist"));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void AddBuildingBlocksMongo_ShouldRegisterClientAndDatabase_WhenConfigurationIsValid()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [MongoConfigurationExtensions.ConnectionStringConfigKey] = "mongodb://localhost:27017",
            })
            .Build();

        // Act
        services.AddBuildingBlocksMongo(configuration, "giftlist");
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMongoClient>();
        var database = provider.GetRequiredService<IMongoDatabase>();

        // Assert
        Assert.NotNull(client);
        Assert.Equal("giftlist", database.DatabaseNamespace.DatabaseName);
    }
}
