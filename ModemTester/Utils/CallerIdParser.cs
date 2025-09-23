using Serilog;

namespace CallerIdListener.Utils;

/// <summary>
/// Parses Caller ID payloads from typical USB modem outputs (e.g., USR5637).
/// Looks for "NMBR = +41..." line and extracts the number.
/// </summary>
public sealed class CallerIdParser
{
   /// <summary>
   /// Try to extract the phone number from raw modem data.
   /// </summary>
   public bool TryParseNumber(string rawData, out string number)
   {
      number = string.Empty;
      if (string.IsNullOrWhiteSpace(rawData)) return false;

      // Normalize newlines
      var lines = rawData
          .Replace("\r\n", "\n")
          .Replace('\r', '\n')
          .Split('\n', StringSplitOptions.RemoveEmptyEntries);

      foreach (var rawLine in lines)
      {
         var line = rawLine.Trim();
         // Typical format: "NMBR = +41791234567"
         if (line.StartsWith("NMBR", StringComparison.OrdinalIgnoreCase))
         {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
               number = SanitizeNumber(parts[1]);
               return number.Length > 0;
            }
         }
      }

      return false;
   }

   /// <summary>
   /// Basic number cleanup: remove spaces, hyphens, leading zeros patterns, etc.
   /// </summary>
   public string SanitizeNumber(string input)
   {
      if (string.IsNullOrWhiteSpace(input)) return string.Empty;
      var s = new string(input.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());

      // Normalize Swiss numbers to +41 if needed (basic example, adapt as you prefer)
      if (s.StartsWith("00")) s = "+" + s[2..];
      if (!s.StartsWith("+") && s.StartsWith("0"))
         s = "+41" + s[1..]; // assume CH by default

      return s;
   }
}
