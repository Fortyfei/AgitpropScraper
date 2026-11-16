using Agitprop.Data;
using Agitprop.Web.Components;
using Agitprop.Web.Diagnostics;
using Agitprop.Web.Services;
using ApexCharts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

builder.Services.AddApexCharts();
builder.Services.AddMemoryCache();

builder.AddNewsfeedRepositories();
builder.Services.AddTransient<AnalyticsService>();

builder.Services.AddScoped<CircuitTraceContext>();
builder.Services.AddScoped<UserJourneyFactory>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("ApplyMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    app.Logger.LogInformation("Applied database migrations at startup");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
