using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using CallerIdListener.Services;
using CallerIdListener.Utils;

namespace CallerIdListener;

public class Program
{
   // Entry point: build a Generic Host for DI, Logging, and lifetime handling.
   public static async Task Main(string[] args)
   {
      var builder = Host.CreateDefaultBuilder(args)
          .ConfigureAppConfiguration((ctx, cfg) =>
          {
             cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
             cfg.AddEnvironmentVariables(prefix: "CALLERID_"); // optional env override
             if (args is { Length: > 0 }) cfg.AddCommandLine(args);
          })
          .UseSerilog((ctx, services, serilogCfg) =>
          {
             // Read Serilog configuration from appsettings.json
             serilogCfg
                   .ReadFrom.Configuration(ctx.Configuration)
                   .ReadFrom.Services(services);
          })
          .ConfigureServices((ctx, services) =>
          {
             // Options binding
             services.Configure<ModemOptions>(ctx.Configuration.GetSection("Modem"));
             services.Configure<LookupOptions>(ctx.Configuration.GetSection("Lookup"));

             // HttpClientFactory for lookup service
             services.AddHttpClient("SearchCh", client =>
             {
                // Base address can stay empty because API needs full URL.
                client.Timeout = TimeSpan.FromSeconds(10);
             });

             // Utilities and services
             services.AddSingleton<CallerIdParser>();
             services.AddSingleton<ISearchLookup, SearchChLookup>(); // pluggable provider
             services.AddHostedService<ModemListener>();             // BackgroundService
          });

      using var host = builder.Build();

      Log.Information("Starting CallerIdListener...");
      await host.RunAsync();
      Log.Information("CallerIdListener stopped.");
   }
}

// Strongly-typed options for modem config
public sealed class ModemOptions
{
   public string ComPort { get; set; } = "COM3";
   public int BaudRate { get; set; } = 115200;
   public string Parity { get; set; } = "None";   // None, Odd, Even, Mark, Space
   public int DataBits { get; set; } = 8;
   public string StopBits { get; set; } = "One";  // None, One, Two, OnePointFive
   public int ReadTimeoutMs { get; set; } = 500;
   public int WriteTimeoutMs { get; set; } = 500;
   public string[] InitCommands { get; set; } = ["ATZ", "AT+VCID=1"];
}

public sealed class LookupOptions
{
   public bool Enable { get; set; } = true;
   public string Provider { get; set; } = "SearchCh";
   public string SearchChApiKey { get; set; } = "";
   public string CountryCode { get; set; } = "CH";
}
