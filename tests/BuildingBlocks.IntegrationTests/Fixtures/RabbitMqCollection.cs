namespace BuildingBlocks.IntegrationTests.Fixtures;

[CollectionDefinition(Name)]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "RabbitMq";
}
