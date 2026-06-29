using System;
using Dweiss;
using UnityEngine;
using VDS.RDF.Query;
using VDS.RDF.Storage;

public static class SparqlProviderFactory
{
  public const string GenericSparql = "GenericSparql";
  public const string Virtuoso = "Virtuoso";
  public const string GraphDB = "GraphDB";
  public const string AllegroGraph = "AllegroGraph";
  public const string Fuseki = "Fuseki";
  public const string Stardog = "Stardog";

  public static ISparqlProvider Create(Settings settings)
  {
    string providerType = string.IsNullOrEmpty(settings.providerType) ? GenericSparql : settings.providerType.Trim();

    switch (providerType.ToLowerInvariant())
    {
      case "allegrograph":
        return CreateAllegroGraphProvider(settings);
      case "graphdb":
        return CreateGraphDbProvider(settings);
      case "fuseki":
        return CreateRemoteEndpointProvider(settings, Fuseki);
      case "stardog":
        return CreateStardogProvider(settings);
      case "virtuoso":
      case "genericsparql":
      default:
        return CreateRemoteEndpointProvider(settings, providerType);
    }
  }

  private static ISparqlProvider CreateRemoteEndpointProvider(Settings settings, string providerType)
  {
    SparqlRemoteEndpoint endpoint = string.IsNullOrEmpty(settings.baseURI)
      ? new SparqlRemoteEndpoint(new Uri(settings.sparqlEndpoint))
      : new SparqlRemoteEndpoint(new Uri(settings.sparqlEndpoint), settings.baseURI);

    if (ShouldPreferJsonResults(settings, providerType))
    {
      endpoint.ResultsAcceptHeader = "application/sparql-results+json,application/json,text/json,application/sparql-results+xml;q=0.5";
    }
    if (settings.timeoutMilliseconds > 0)
    {
      endpoint.Timeout = settings.timeoutMilliseconds;
    }
    if (!string.IsNullOrEmpty(settings.username))
    {
      endpoint.SetCredentials(settings.username, settings.password);
    }

    return new SparqlRemoteEndpointProvider(endpoint, providerType);
  }

  private static bool ShouldPreferJsonResults(Settings settings, string providerType)
  {
    if (settings.preferJsonResults)
    {
      return true;
    }

    if (string.Equals(providerType, GraphDB, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (string.Equals(providerType, AllegroGraph, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (string.Equals(providerType, Fuseki, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (string.Equals(providerType, Stardog, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    Uri endpointUri;
    if (Uri.TryCreate(settings.sparqlEndpoint, UriKind.Absolute, out endpointUri))
    {
      string path = endpointUri.AbsolutePath.ToLowerInvariant();
      return path.Contains("/repositories/") || path.Contains("/catalogs/");
    }

    return false;
  }

  private static ISparqlProvider CreateAllegroGraphProvider(Settings settings)
  {
    if (string.IsNullOrEmpty(settings.repositoryId))
    {
      Debug.LogWarning("AllegroGraph provider selected without repositoryId. Falling back to generic SPARQL endpoint.");
      return CreateRemoteEndpointProvider(settings, GenericSparql);
    }

    string catalogId = string.IsNullOrEmpty(settings.catalogId) ? null : settings.catalogId;
    AllegroGraphConnector connector;
    if (string.IsNullOrEmpty(settings.username))
    {
      connector = catalogId == null
        ? new AllegroGraphConnector(settings.sparqlEndpoint, settings.repositoryId)
        : new AllegroGraphConnector(settings.sparqlEndpoint, catalogId, settings.repositoryId);
    }
    else
    {
      connector = catalogId == null
        ? new AllegroGraphConnector(settings.sparqlEndpoint, settings.repositoryId, settings.username, settings.password)
        : new AllegroGraphConnector(settings.sparqlEndpoint, catalogId, settings.repositoryId, settings.username, settings.password);
    }

    return new StorageSparqlProvider(connector, AllegroGraph);
  }

  private static ISparqlProvider CreateGraphDbProvider(Settings settings)
  {
    string repositoryId = settings.repositoryId?.Trim();
    string serverEndpoint = settings.sparqlEndpoint?.Trim();
    string extractedRepositoryId;

    if (TryExtractRepositoryEndpoint(serverEndpoint, out string extractedServerEndpoint, out extractedRepositoryId))
    {
      serverEndpoint = extractedServerEndpoint;
      if (string.IsNullOrEmpty(repositoryId))
      {
        repositoryId = extractedRepositoryId;
      }
    }

    if (string.IsNullOrEmpty(repositoryId))
    {
      Debug.LogWarning("GraphDB provider selected without repositoryId. Falling back to generic SPARQL endpoint.");
      return CreateRemoteEndpointProvider(settings, GraphDB);
    }

    Debug.Log($"GraphDB provider config | server: {serverEndpoint} | repository: {repositoryId}");

    SesameHttpProtocolVersion6Connector connector = string.IsNullOrEmpty(settings.username)
      ? new SesameHttpProtocolVersion6Connector(serverEndpoint, repositoryId)
      : new SesameHttpProtocolVersion6Connector(serverEndpoint, repositoryId, settings.username, settings.password);

    return new StorageSparqlProvider(connector, GraphDB);
  }

  private static ISparqlProvider CreateStardogProvider(Settings settings)
  {
    string databaseId = settings.repositoryId?.Trim();
    string serverEndpoint = NormalizeStardogServerEndpoint(settings.sparqlEndpoint?.Trim(), ref databaseId);

    if (string.IsNullOrEmpty(databaseId))
    {
      Debug.LogWarning("Stardog provider selected without repositoryId. Falling back to generic SPARQL endpoint.");
      return CreateRemoteEndpointProvider(settings, Stardog);
    }

    Debug.Log($"Stardog provider config | server: {serverEndpoint} | database: {databaseId}");

    StardogConnector connector = string.IsNullOrEmpty(settings.username)
      ? new StardogConnector(serverEndpoint, databaseId)
      : new StardogConnector(serverEndpoint, databaseId, settings.username, settings.password);

    return new StorageSparqlProvider(connector, Stardog);
  }

  private static bool TryExtractRepositoryEndpoint(string endpoint, out string serverEndpoint, out string repositoryId)
  {
    serverEndpoint = endpoint;
    repositoryId = "";

    Uri uri;
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri))
    {
      return false;
    }

    string path = uri.AbsolutePath;
    int repositoriesIndex = path.IndexOf("/repositories/", StringComparison.OrdinalIgnoreCase);
    if (repositoriesIndex < 0)
    {
      return false;
    }

    int repositoryStart = repositoriesIndex + "/repositories/".Length;
    int repositoryEnd = path.IndexOf('/', repositoryStart);
    string encodedRepositoryId = repositoryEnd < 0
      ? path.Substring(repositoryStart)
      : path.Substring(repositoryStart, repositoryEnd - repositoryStart);

    repositoryId = Uri.UnescapeDataString(encodedRepositoryId);
    string serverPath = path.Substring(0, repositoriesIndex);
    UriBuilder builder = new UriBuilder(uri)
    {
      Path = serverPath,
      Query = "",
      Fragment = ""
    };
    serverEndpoint = builder.Uri.ToString().TrimEnd('/');

    return !string.IsNullOrEmpty(repositoryId);
  }

  private static string NormalizeStardogServerEndpoint(string endpoint, ref string databaseId)
  {
    if (string.IsNullOrEmpty(endpoint))
    {
      return endpoint;
    }

    Uri uri;
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri))
    {
      return endpoint;
    }

    string[] pathSegments = uri.AbsolutePath.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    if (pathSegments.Length > 0 && string.IsNullOrEmpty(databaseId))
    {
      databaseId = Uri.UnescapeDataString(pathSegments[0]);
    }

    UriBuilder builder = new UriBuilder(uri)
    {
      Path = "",
      Query = "",
      Fragment = ""
    };

    return builder.Uri.ToString().TrimEnd('/');
  }
}
