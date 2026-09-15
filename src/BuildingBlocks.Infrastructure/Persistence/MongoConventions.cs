using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace BuildingBlocks.Persistence;

/// <summary>
/// The one BSON convention registration shared by every service (a local decision — no
/// document states it; the nearest rule is ARCHITECTURE.md "Data that crosses boundaries"'s
/// "explicit mapper in Infrastructure"), so that
/// "camelCase field names" and "ignore unknown fields" are decided once instead of per
/// persistence document.
/// </summary>
public static class MongoConventions
{
    private const string PackName = "BuildingBlocks";

    private static int _registered;

    /// <summary>
    /// Registers the shared convention pack and GUID representation. Safe to call more than
    /// once (e.g. from several hosts sharing a test process) — only the first call takes effect.
    /// </summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true),
        };

        ConventionRegistry.Register(PackName, pack, _ => true);

        // Guid.ToString()/parsing round-trips correctly across drivers and languages only under
        // the "Standard" representation; the legacy ones are byte-order traps left over from
        // early driver versions.
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }
}
