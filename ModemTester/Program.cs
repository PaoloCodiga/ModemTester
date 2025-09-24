using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using CallerIdListener.Services;
using CallerIdListener.Utils;

namespace CallerIdListener;

public class Program
{
   public static async Task Main(string[] args)
   {
      var hostBuilder = Host.CreateDefaultBuilder(args)
          .ConfigureAppConfiguration((ctx, cfg) =>
          {
             cfg.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
             cfg.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
             if (ctx.HostingEnvironment.IsDevelopment())
                cfg.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
             cfg.AddEnvironmentVariables(prefix: "CALLERID_");
             cfg.AddCommandLine(args); // enables --Number=...
          })
          .UseSerilog((ctx, services, serilogCfg) =>
          {
             serilogCfg.ReadFrom.Configuration(ctx.Configuration)
                         .ReadFrom.Services(services);
          })
          .ConfigureServices((ctx, services) =>
          {
             services.Configure<ModemOptions>(ctx.Configuration.GetSection("Modem"));
             services.Configure<LookupOptions>(ctx.Configuration.GetSection("Lookup"));

             services.AddHttpClient("SearchCh", client => { client.Timeout = TimeSpan.FromSeconds(10); });
             services.AddSingleton<CallerIdParser>();
             services.AddSingleton<ISearchLookup, SearchChLookup>();

             // Register modem listener as hosted service (will run only if we call host.RunAsync()).
             services.AddHostedService<ModemListener>();
          });

      using var host = hostBuilder.Build();

      // Resolve helpers
      var config = host.Services.GetRequiredService<IConfiguration>();
      var parser = host.Services.GetRequiredService<CallerIdParser>();
      var lookup = host.Services.GetRequiredService<ISearchLookup>();

      // 1) Read number from --Number=... or as first positional argument.
      var cliNumber = config["Number"]; // from CommandLine provider if passed as --Number=...
      if (string.IsNullOrWhiteSpace(cliNumber) && args.Length > 0 && !args[0].StartsWith('-'))
         cliNumber = args[0]; // allow positional number

      // 2) If a number is provided, perform lookup and exit WITHOUT starting the modem.
      if (!string.IsNullOrWhiteSpace(cliNumber))
      {
         // Sanitize the number before lookup
         var sanitized = parser.SanitizeNumber(cliNumber);
         Log.Information("Lookup-only mode. Verifying number: {Number}", sanitized);

         var result = await lookup.LookupAsync(sanitized);
         if (result is null)
         {
            Log.Warning("No match found for {Number}", sanitized);
         }
         else
         {
            Log.Information("Match → {Name} | {Phone} | {Address} {Zip} {City}",
                result.Name, result.Phone, result.Address, result.Zip, result.City);
         }

         Log.Information("Lookup completed. Exiting without opening the modem.");
         return; // 🚪 Exit here, modem listener never starts
      }

      // 3) No number passed → start modem listener normally.
      Log.Information("No CLI number provided. Starting modem listener...");
      await host.RunAsync();
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
