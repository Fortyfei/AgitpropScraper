using System.Text.Json;
using Agitprop.Core.Enums;
using Agitprop.Sinks.Newsfeed;

namespace Agitprop.UnitTests;

public class NewsfeedJobContractTests
{
    [Test]
    public void NewsfeedJobDescription_RetainsItsWireTypeAndPayload()
    {
        var message = new NewsfeedJobDescrpition
        {
            Url = "https://example.com/article",
            Type = PageContentType.Article
        };

        Assert.That(
            typeof(NewsfeedJobDescrpition).FullName,
            Is.EqualTo("Agitprop.Sinks.Newsfeed.NewsfeedJobDescrpition"));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(message));
        Assert.That(json.RootElement.GetProperty("Url").GetString(), Is.EqualTo(message.Url));
        Assert.That(
            json.RootElement.GetProperty("Type").GetInt32(),
            Is.EqualTo((int)message.Type));
    }
}
