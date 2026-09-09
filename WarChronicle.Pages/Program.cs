using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WarChronicle.Web.Components;
using WarChronicle.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<WcDataCatalog>();
builder.Services.AddScoped<GameSession>();
builder.Services.AddSingleton<GameLogFileStore>();

await builder.Build().RunAsync();
