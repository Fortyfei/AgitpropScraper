using Agitprop.Core;

namespace Agitprop.UnitTests;

public class NamedEntityTests
{
    [Test]
    public void Equals_IsCaseInsensitiveAndTypeAware()
    {
        var first = new NamedEntity { Name = "Tisza", Type = "LOC" };
        var same = new NamedEntity { Name = "tisza", Type = "loc" };
        var otherType = new NamedEntity { Name = "Tisza", Type = "ORG" };

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(otherType));
            Assert.That(first.GetHashCode(), Is.Not.EqualTo(otherType.GetHashCode()));
        });
    }
}
