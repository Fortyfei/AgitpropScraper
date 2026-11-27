using Agitprop.Data;

namespace Agitprop.UnitTests;

public class EntityNormalizerTests
{
    [TestCase("Magyarországon", "magyarországon")]
    [TestCase("  Magyar Péter  ", "magyar péter")]
    [TestCase("ENSZ-", "ensz")]
    [TestCase("Tisza-", "tisza")]
    [TestCase("„Tisza Párt”", "tisza párt")]
    [TestCase("EU-nak", "eu-nak")]
    [TestCase("Tisza-parti   sétány", "tisza-parti sétány")]
    [TestCase("O\u0301", "ó")]
    [TestCase("", "")]
    [TestCase("   ", "")]
    [TestCase("-", "")]
    public void Normalize_ProducesStableComparisonKey(string input, string expected)
    {
        Assert.That(EntityNormalizer.Normalize(input), Is.EqualTo(expected));
    }

    [Test]
    public void Normalize_KeepsHungarianAccents()
    {
        Assert.That(EntityNormalizer.Normalize("Árvíz"), Is.EqualTo("árvíz"));
        Assert.That(EntityNormalizer.Normalize("Arviz"), Is.Not.EqualTo(EntityNormalizer.Normalize("Árvíz")));
    }

    [TestCase("", false)]
    [TestCase("-", false)]
    [TestCase("50", false)]
    [TestCase(".", false)]
    [TestCase("a", false)]
    [TestCase("eu", true)]
    [TestCase("magyarország", true)]
    [TestCase("x-faktor", true)]
    public void IsMeaningful_RejectsSurfaceNoise(string normalizedName, bool expected)
    {
        Assert.That(EntityNormalizer.IsMeaningful(normalizedName), Is.EqualTo(expected));
    }
}
