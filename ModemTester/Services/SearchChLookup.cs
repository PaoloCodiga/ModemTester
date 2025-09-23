using System.Xml;
using CallerIdListener.Models;
using Microsoft.Extensions.Options;
using Serilog;

namespace CallerIdListener.Services;

/// <summary>
/// search.ch / local.ch reverse lookup implementation.
/// </summary>
public interface ISearchLookup
{
   Task<LookupResult?> LookupAsync(string phoneNumber, CancellationToken ct = default);
}

public sealed class SearchChLookup : ISearchLookup
{
   private readonly IHttpClientFactory _httpClientFactory;
   private readonly LookupOptions _opts;

   public SearchChLookup(IHttpClientFactory httpClientFactory, IOptions<LookupOptions> opts)
   {
      _httpClientFactory = httpClientFactory;
      _opts = opts.Value;
   }

   public async Task<LookupResult?> LookupAsync(string phoneNumber, CancellationToken ct = default)
   {
      if (!_opts.Enable)
      {
         Log.Debug("Lookup is disabled via configuration.");
         return null;
      }

      if (string.IsNullOrWhiteSpace(_opts.SearchChApiKey))
      {
         Log.Warning("SearchChApiKey is missing in configuration.");
         return null;
      }

      // Build URL
      var url = $"https://tel.search.ch/api/?was={Uri.EscapeDataString(phoneNumber)}&key={_opts.SearchChApiKey}&lang=en";
      Log.Debug("Calling search.ch: {Url}", url);

      var client = _httpClientFactory.CreateClient("SearchCh");
      using var resp = await client.GetAsync(url, ct);
      if (!resp.IsSuccessStatusCode)
      {
         Log.Warning("search.ch returned status {Status}", resp.StatusCode);
         return null;
      }

      var xml = await resp.Content.ReadAsStringAsync(ct);
      try
      {
         var xmlDoc = new XmlDocument();
         xmlDoc.LoadXml(xml);

         // Namespaces
         var nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
         // Namespace used by search.ch tel results
         nsmgr.AddNamespace("tel", "http://tel.search.ch/api/spec/result/1.0/");
         // Atom may be present as default ns; try both with/without ns for <entry>
         var entry = xmlDoc.SelectSingleNode("//entry") ?? xmlDoc.SelectSingleNode("//*[local-name()='entry']");
         if (entry is null) return null;

         string? Get(string localName)
         {
            var node = entry.SelectSingleNode($"tel:{localName}", nsmgr)
                       ?? entry.SelectSingleNode($"*[local-name()='{localName}']");
            return node?.InnerText?.Trim();
         }

         var result = new LookupResult
         {
            Name = Get("name"),
            Phone = Get("phone"),
            Address = Get("street"),
            Zip = Get("zip"),
            City = Get("city"),
            RawSource = xml
         };

         // If phone is empty in response, still return name/address if present
         if (string.IsNullOrEmpty(result.Name) &&
             string.IsNullOrEmpty(result.Address) &&
             string.IsNullOrEmpty(result.City))
         {
            Log.Information("search.ch found no person/company details for {Number}", phoneNumber);
            return null;
         }

         Log.Information("search.ch match → {Name} | {Phone} | {Address} {Zip} {City}",
             result.Name, result.Phone, result.Address, result.Zip, result.City);

         return result;
      }
      catch (Exception ex)
      {
         Log.Error(ex, "Failed to parse search.ch XML");
         return null;
      }
   }
}
