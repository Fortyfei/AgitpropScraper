using System.Net;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Projects;

namespace Agitprop.IntegrationTests;

[NonParallelizable]
public class ApiPostgresIntegrationTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private IAsyncDisposable? _application;
    private HttpClient? _client;

    [OneTimeSetUp]
    public async Task StartApplication()
    {
        using var timeout = new CancellationTokenSource(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Agitprop_Testing_AppHost>(timeout.Token);
        var application = await builder.BuildAsync(timeout.Token)
            .WaitAsync(timeout.Token);
        _application = application;

        await application.StartAsync(timeout.Token)
            .WaitAsync(timeout.Token);
        await application.ResourceNotifications
            .WaitForResourceHealthyAsync("backend", timeout.Token)
            .WaitAsync(timeout.Token);

        _client = application.CreateHttpClient("backend");
    }

    [OneTimeTearDown]
    public async Task StopApplication()
    {
        _client?.Dispose();
        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }

    [Test]
    public async Task BrowseEntitiesWithSearch_QueriesMigratedPostgresDatabase()
    {
        using var response = await _client!.GetAsync(
            "/api/Entities?from=2026-01-01&to=2026-01-31&search=Telex");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("totalCount").GetInt32(), Is.Zero);
            Assert.That(body.RootElement.GetProperty("items").GetArrayLength(), Is.Zero);
        });
    }
}