using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using DrunkDeer.Web;
using DrunkDeer.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMudServices();

// Built by hand rather than resolved, because it has to be in place before anything can log to
// it: the logging pipeline and the container are handed the same instance.
var diagnostics = new DiagnosticsLog();
builder.Services.AddSingleton(diagnostics);
builder.Logging.AddProvider(new DiagnosticsLoggerProvider(diagnostics));

// Debug for the SDK only. That level is where the session reports the things the diagnostics page
// exists to show — a dropped frame, a packet arriving out of turn — while the framework's own
// Debug output would bury them. Deliberately below Trace: the poll loop logs there every frame,
// hundreds of times a second.
builder.Logging.AddFilter("DrunkDeer", LogLevel.Debug);

// The session lives for the lifetime of the page, so these are singletons in WASM
// (one user, one tab, one keyboard). KeyboardService owns the async session and the
// connection; KeyboardStore is the UI-facing snapshot components bind to; SelectionStore
// carries the key selection the edit panels act on.
builder.Services.AddSingleton<KeyboardService>();
builder.Services.AddSingleton<KeyboardStore>();
builder.Services.AddSingleton<SelectionStore>();

// Holds no keyboard state of its own — it reads and writes localStorage — but stays a singleton
// so the interop module is imported once rather than per panel render.
builder.Services.AddSingleton<ProfileLibrary>();

await builder.Build().RunAsync();
