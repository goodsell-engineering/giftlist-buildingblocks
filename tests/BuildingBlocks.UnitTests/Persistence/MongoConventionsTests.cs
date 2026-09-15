using BuildingBlocks.Persistence;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace BuildingBlocks.UnitTests.Persistence;

public sealed class MongoConventionsTests
{
    [Fact]
    public void Register_ShouldMapPropertiesToCamelCaseElementNames_WhenSerializingDocument()
    {
        // Arrange
        MongoConventions.Register();
        var document = new SampleDocument { FirstName = "Ada", LastName = "Lovelace" };

        // Act
        var bson = document.ToBsonDocument();

        // Assert
        Assert.True(bson.Contains("firstName"));
        Assert.True(bson.Contains("lastName"));
        Assert.False(bson.Contains("FirstName"));
    }

    [Fact]
    public void Register_ShouldIgnoreExtraElements_WhenDeserializing()
    {
        // Arrange
        MongoConventions.Register();
        var bson = new BsonDocument
        {
            { "firstName", "Ada" },
            { "lastName", "Lovelace" },
            { "fieldFromANewerSchema", "surplus" },
        };

        // Act
        var exception = Record.Exception(() => BsonSerializer.Deserialize<SampleDocument>(bson));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Register_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange — none

        // Act
        var exception = Record.Exception(() =>
        {
            MongoConventions.Register();
            MongoConventions.Register();
        });

        // Assert
        Assert.Null(exception);
    }

    private sealed class SampleDocument
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;
    }
}
