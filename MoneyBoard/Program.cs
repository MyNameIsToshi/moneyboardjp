using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MoneyBoard;
using MoneyBoard.Services;
using ApexCharts;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ローカル開発時は Functions のURL、本番は相対パス（SWA経由）
var apiBaseUrl = builder.HostEnvironment.IsDevelopment()
    ? "http://localhost:7071"
    : builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<StorageService>();
builder.Services.AddScoped<AppStateStore>();
builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<PortfolioStore>();
builder.Services.AddScoped<QuoteService>();
builder.Services.AddApexCharts();

await builder.Build().RunAsync();
