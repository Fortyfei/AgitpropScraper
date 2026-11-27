using Agitprop.Core;
using Agitprop.Data;
using Agitprop.Data.Models;

namespace Agitprop.UnitTests;

public class EntityResolverTests
{
    private static readonly Guid Hungary = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ukraine = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TiszaRiver = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TiszaParty = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherOrg = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Test]
    public void Resolve_InflectedAliasResolvesToCanonicalEntity()
    {
        var resolution = EntityResolver.Resolve(Entity("Magyarországon", "LOC"), CreateIndex());

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Existing));
            Assert.That(resolution.EntityId, Is.EqualTo(Hungary));
            Assert.That(resolution.CanonicalName, Is.EqualTo("Magyarország"));
            Assert.That(resolution.MatchKind, Is.EqualTo(EntityMatchKinds.Alias));
            Assert.That(resolution.TypeConflict, Is.False);
        });
    }

    [Test]
    public void Resolve_ExactCanonicalNameWins()
    {
        var resolution = EntityResolver.Resolve(Entity("MAGYARORSZÁG", "LOC"), CreateIndex());

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Existing));
            Assert.That(resolution.EntityId, Is.EqualTo(Hungary));
            Assert.That(resolution.MatchKind, Is.EqualTo(EntityMatchKinds.Exact));
        });
    }

    [Test]
    public void Resolve_TiszaHomographStaysSeparate()
    {
        var index = CreateIndex();

        var river = EntityResolver.Resolve(Entity("Tisza", "LOC"), index);
        var party = EntityResolver.Resolve(Entity("Tisza", "ORG"), index);

        Assert.Multiple(() =>
        {
            Assert.That(river.EntityId, Is.EqualTo(TiszaRiver));
            Assert.That(river.CanonicalName, Is.EqualTo("Tisza"));
            Assert.That(party.EntityId, Is.EqualTo(TiszaParty));
            Assert.That(party.CanonicalName, Is.EqualTo("Tisza Párt"));
            Assert.That(party.EntityId, Is.Not.EqualTo(river.EntityId));
        });
    }

    [Test]
    public void Resolve_TypeDriftUsesSingleCrossTypeCandidate()
    {
        var resolution = EntityResolver.Resolve(Entity("Magyarországon", "ORG"), CreateIndex());

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Existing));
            Assert.That(resolution.EntityId, Is.EqualTo(Hungary));
            Assert.That(resolution.TypeConflict, Is.True);
        });
    }

    [Test]
    public void Resolve_AmbiguousCrossTypeSurfaceIsRejectedInsteadOfGuessed()
    {
        var resolution = EntityResolver.Resolve(Entity("Tisza", "MISC"), CreateIndex());

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Rejected));
            Assert.That(resolution.AliasAmbiguous, Is.True);
        });
    }

    [TestCase("-", "ORG")]
    [TestCase("50", "MISC")]
    [TestCase(".", "LOC")]
    public void Resolve_RejectsSurfaceNoise(string name, string type)
    {
        var resolution = EntityResolver.Resolve(Entity(name, type), CreateIndex());

        Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Rejected));
    }

    [Test]
    public void Resolve_PrefersHigherPriorityAliasAndFlagsAmbiguity()
    {
        var index = new EntityResolutionIndex();
        index.AddEntity(TiszaParty, "Tisza Párt", "tisza párt", "ORG");
        index.AddEntity(OtherOrg, "Másik szervezet", "másik szervezet", "ORG");
        index.AddAlias(TiszaParty, "Tisza Párt", "ORG", "tisza", "ORG", priority: 10);
        index.AddAlias(OtherOrg, "Másik szervezet", "ORG", "tisza", "ORG", priority: 5);

        var resolution = EntityResolver.Resolve(Entity("Tisza", "ORG"), index);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Existing));
            Assert.That(resolution.EntityId, Is.EqualTo(TiszaParty));
            Assert.That(resolution.AliasAmbiguous, Is.True);
        });
    }

    [Test]
    public void Resolve_UnknownSurfaceCreatesNewEntity()
    {
        var resolution = EntityResolver.Resolve(Entity("  Kovács Béla ", "PER"), CreateIndex());

        Assert.Multiple(() =>
        {
            Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Create));
            Assert.That(resolution.CanonicalName, Is.EqualTo("Kovács Béla"));
            Assert.That(resolution.NormalizedSurface, Is.EqualTo("kovács béla"));
            Assert.That(resolution.MatchKind, Is.EqualTo(EntityMatchKinds.Created));
        });
    }

    [Test]
    public void Resolve_TwoInflectionsOfOneEntityShareTheSameId()
    {
        var index = CreateIndex();

        var first = EntityResolver.Resolve(Entity("Ukrajnának", "LOC"), index);
        var second = EntityResolver.Resolve(Entity("Ukrajnában", "LOC"), index);

        Assert.Multiple(() =>
        {
            Assert.That(first.EntityId, Is.EqualTo(Ukraine));
            Assert.That(second.EntityId, Is.EqualTo(Ukraine));
        });
    }

    [Test]
    public void Resolve_RequiresType()
    {
        var resolution = EntityResolver.Resolve(Entity("Magyarország", " "), CreateIndex());

        Assert.That(resolution.Kind, Is.EqualTo(EntityResolutionKind.Rejected));
    }

    private static NamedEntity Entity(string name, string type) => new() { Name = name, Type = type };

    private static EntityResolutionIndex CreateIndex()
    {
        var index = new EntityResolutionIndex();

        index.AddEntity(Hungary, "Magyarország", "magyarország", "LOC");
        index.AddAlias(Hungary, "Magyarország", "LOC", "magyarországon", "LOC", 0);
        index.AddAlias(Hungary, "Magyarország", "LOC", "magyarországra", "LOC", 0);

        index.AddEntity(Ukraine, "Ukrajna", "ukrajna", "LOC");
        index.AddAlias(Ukraine, "Ukrajna", "LOC", "ukrajnában", "LOC", 0);
        index.AddAlias(Ukraine, "Ukrajna", "LOC", "ukrajnának", "LOC", 0);

        index.AddEntity(TiszaRiver, "Tisza", "tisza", "LOC");
        index.AddAlias(TiszaRiver, "Tisza", "LOC", "tisza", "LOC", 0);

        index.AddEntity(TiszaParty, "Tisza Párt", "tisza párt", "ORG");
        index.AddAlias(TiszaParty, "Tisza Párt", "ORG", "tisza", "ORG", 0);

        return index;
    }
}
