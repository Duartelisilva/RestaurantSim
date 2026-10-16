using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RestaurantSim.Client;
using RestaurantSim.Client.Engine;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

var hubUrl = builder.Configuration["EngineHubUrl"]
    ?? throw new InvalidOperationException("EngineHubUrl is missing from wwwroot/appsettings.json.");

// UI code depends only on IEngineClient. Today it is SignalR to a server-hosted
// engine; an in-process engine for Android could replace it without UI changes.
builder.Services.AddSingleton<IEngineClient>(_ => new SignalREngineClient(hubUrl));

await builder.Build().RunAsync();
