using Serilog;
using System.IO.Ports;

class Program
{
   private static SerialPort? _serialPort;

   static void Main()
   {
      // Configure Serilog (console + file)
      Log.Logger = new LoggerConfiguration()
          .MinimumLevel
         .Debug()
         .WriteTo
         .Console()
         .WriteTo
         .File("logs\\modem_log.txt", rollingInterval: RollingInterval.Day)
         .CreateLogger();

      try
      {
         Log.Information("Starting modem listener...");

         // Adjust COM port based on your system
         _serialPort = new SerialPort("COM3", 115200, Parity.None, 8, StopBits.One);
         _serialPort.DataReceived += SerialPort_DataReceived;
         _serialPort.Open();

         Log.Information("Serial port {PortName} opened successfully", _serialPort.PortName);

         // Reset and enable Caller ID
         _serialPort.WriteLine("ATZ");
         Log.Debug("Sent command: ATZ");

         _serialPort.WriteLine("AT+VCID=1");
         Log.Debug("Sent command: AT+VCID=1 (enable Caller ID)");

         Console.WriteLine("Listening for calls... Press any key to exit.");
         Console.ReadKey();
      }
      catch (Exception ex)
      {
         Log.Error(ex, "An error occurred while initializing modem listener");
      }
      finally
      {
         if (_serialPort != null && _serialPort.IsOpen)
         {
            _serialPort.Close();
            Log.Information("Serial port closed.");
         }

         Log.CloseAndFlush();
      }
   }

   private static void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
   {
      try
      {
         string? data = _serialPort?.ReadExisting();
         Log.Debug("Raw data received: {Data}", data);

         if (data?.Contains("NMBR") == true)
         {
            var lines = data.Split('\n');
            foreach (var line in lines)
            {
               if (line.Trim().StartsWith("NMBR"))
               {
                  string number = line.Split('=')[1].Trim();
                  Log.Information("Incoming call detected from: {Number}", number);

                  // TODO: forward number to POSsible (e.g., simulate keyboard input or API)
               }
            }
         }
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Error while processing serial data");
      }
   }
}
