using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MinGo.MyBillBook.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<NotificationStore>();

await builder.Build().RunAsync();
