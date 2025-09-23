using System.IO.Ports;
using CallerIdListener.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace CallerIdListener.Services;

/// <summary>
/// Background service that opens the modem COM port, sends init commands,
/// and processes incoming Caller ID lines.
/// </summary>
public sealed class ModemListener : BackgroundService
{
   private readonly ModemOptions _opts;
   private readonly ISearchLookup _lookup;
   private readonly CallerIdParser _parser;
   private SerialPort? _port;

   public ModemListener(
       IOptions<ModemOptions> modemOptions,
       ISearchLookup lookup,
       CallerIdParser parser)
   {
      _opts = modemOptions.Value;
      _lookup = lookup;
      _parser = parser;
   }

   protected override Task ExecuteAsync(CancellationToken stoppingToken)
   {
      // Run the port loop on the threadpool
      _ = Task.Run(() => RunAsync(stoppingToken), stoppingToken);
      return Task.CompletedTask;
   }

   private async Task RunAsync(CancellationToken ct)
   {
      try
      {
         OpenPort();

         SendInitCommands();

         Log.Information("Listening for calls on {Port}...", _opts.ComPort);

         // Event-based approach: DataReceived handler
         if (_port is not null)
            _port.DataReceived += async (_, __) => await OnDataAsync(ct);

         // Keep the service alive
         while (!ct.IsCancellationRequested)
         {
            await Task.Delay(500, ct);
         }
      }
      catch (TaskCanceledException)
      {
         // Normal termination
      }
      catch (Exception ex)
      {
         Log.Fatal(ex, "Fatal error in modem listener loop");
      }
      finally
      {
         try
         {
            if (_port?.IsOpen == true)
            {
               _port.DataReceived -= async (_, __) => await OnDataAsync(ct); // best-effort detach
               _port.Close();
               Log.Information("Serial port closed.");
            }
         }
         catch { /* ignore */ }
      }
   }

   private void OpenPort()
   {
      _port = new SerialPort
      {
         PortName = _opts.ComPort,
         BaudRate = _opts.BaudRate,
         Parity = Enum.TryParse(_opts.Parity, out Parity p) ? p : Parity.None,
         DataBits = _opts.DataBits,
         StopBits = Enum.TryParse(_opts.StopBits, out StopBits s) ? s : StopBits.One,
         ReadTimeout = _opts.ReadTimeoutMs,
         WriteTimeout = _opts.WriteTimeoutMs,
         NewLine = "\r"
      };

      _port.Open();
      Log.Information("Serial port {Port} opened.", _port.PortName);
   }

   private void SendInitCommands()
   {
      if (_port is null) return;

      foreach (var cmd in _opts.InitCommands ?? Array.Empty<string>())
      {
         if (string.IsNullOrWhiteSpace(cmd)) continue;
         _port.WriteLine(cmd);
         Log.Debug("Sent modem command: {Cmd}", cmd);
         // Small pause to allow modem to respond
         Thread.Sleep(100);
      }
   }

   private async Task OnDataAsync(CancellationToken ct)
   {
      if (_port is null || !_port.IsOpen) return;

      try
      {
         // Read what's currently in the buffer
         var data = _port.ReadExisting();
         if (string.IsNullOrWhiteSpace(data)) return;

         Log.Debug("MODEM RAW: {Data}", data);

         if (_parser.TryParseNumber(data, out var number))
         {
            Log.Information("Incoming call from: {Number}", number);

            // Reverse lookup
            var result = await _lookup.LookupAsync(number, ct);
            if (result is null)
            {
               Log.Warning("No result for {Number}", number);
               return;
            }

            // TODO: integrate with your POSsible app:
            // - Send a local HTTP POST
            // - Write a small JSON file the POS reads
            // - Simulate keyboard input (HID) if desired
         }
      }
      catch (TimeoutException)
      {
         // serial timeout - ignore
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error while processing serial data");
      }
   }
}
