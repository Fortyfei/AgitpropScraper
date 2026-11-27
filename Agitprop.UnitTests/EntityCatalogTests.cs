using Agitprop.Data;

namespace Agitprop.UnitTests;

public class EntityCatalogTests
{
    [Test]
    public void All_IsSelfConsistent()
    {
        var entities = EntityCatalog.All;

        Assert.Multiple(() =>
        {
            Assert.That(entities, Is.Not.Empty);
            Assert.That(entities.Select(entity => EntityNormalizer.Normalize(entity.Name)), Has.All.Not.Empty);
            Assert.That(entities.Select(entity => entity.Type), Is.All.AnyOf("PER", "LOC", "ORG", "MISC"));
        });
    }

    [Test]
    public void Catalog_KeepsRiverAndPartyTiszaApart()
    {
        var river = EntityCatalog.All.Single(entity => entity.Name == "Tisza" && entity.Type == "LOC");
        var party = EntityCatalog.All.Single(entity => entity.Name == "Tisza Párt" && entity.Type == "ORG");

        Assert.Multiple(() =>
        {
            Assert.That(river.Id, Is.Not.EqualTo(party.Id));
            Assert.That(
                party.Aliases.Select(alias => EntityNormalizer.Normalize(alias.Alias)),
                Does.Contain("tisza"));
            Assert.That(
                river.Aliases.Select(alias => EntityNormalizer.Normalize(alias.Alias)),
                Does.Not.Contain("tisza párt"));
        });
    }

    [Test]
    public void Catalog_ContainsMeasuredInflections()
    {
        var aliases = EntityCatalog.All
            .SelectMany(entity => entity.Aliases.Select(alias => EntityNormalizer.Normalize(alias.Alias)))
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(aliases, Is.SupersetOf(new[]
        {
            "magyarországon", "magyarországra", "ukrajnában", "ukrajnának",
            "oroszországgal", "putyinra", "putyint", "trumpnak", "kijevben", "münchenben"
        }));
    }

    [Test]
    public void Validate_RejectsCrossEntityAliasWithoutPriority()
    {
        var first = new CatalogEntity(Guid.NewGuid(), "Első", "ORG", [new CatalogAlias("közös")]);
        var second = new CatalogEntity(Guid.NewGuid(), "Második", "ORG", [new CatalogAlias("közös")]);

        Assert.That(
            () => EntityCatalog.Validate([first, second]),
            Throws.InvalidOperationException.With.Message.Contains("közös"));
    }

    [Test]
    public void Validate_AllowsCrossEntityAliasWithDistinctPriorities()
    {
        var first = new CatalogEntity(Guid.NewGuid(), "Első", "ORG", [new CatalogAlias("közös", Priority: 10)]);
        var second = new CatalogEntity(Guid.NewGuid(), "Második", "ORG", [new CatalogAlias("közös", Priority: 5)]);

        Assert.That(() => EntityCatalog.Validate([first, second]), Throws.Nothing);
    }

    [Test]
    public void Validate_RejectsDuplicateIdentity()
    {
        var first = new CatalogEntity(Guid.NewGuid(), "Ugyanaz", "ORG", []);
        var second = new CatalogEntity(Guid.NewGuid(), "ugyanaz", "ORG", []);

        Assert.That(() => EntityCatalog.Validate([first, second]), Throws.InvalidOperationException);
    }
}
