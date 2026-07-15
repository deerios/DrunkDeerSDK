using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using DrunkDeer.Web;
using DrunkDeer.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

// The session lives for the lifetime of the page, so these are singletons in WASM
// (one user, one tab, one keyboard). KeyboardService owns the async session and the
// connection; KeyboardStore is the UI-facing snapshot components bind to; SelectionStore
// carries the key selection the edit panels act on.
builder.Services.AddSingleton<KeyboardService>();
builder.Services.AddSingleton<KeyboardStore>();
builder.Services.AddSingleton<SelectionStore>();

await builder.Build().RunAsync();
