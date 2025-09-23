namespace CallerIdListener.Models;

/// <summary>
/// Normalized result of a reverse phone lookup.
/// </summary>
public sealed class LookupResult
{
   public string? Name { get; init; }
   public string? Phone { get; init; }
   public string? Address { get; init; }
   public string? Zip { get; init; }
   public string? City { get; init; }
   public string? RawSource { get; init; } // Optional raw XML/JSON for diagnostics
}
