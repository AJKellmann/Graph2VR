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

  public static ISparqlProvider Create(Settings settings)
  {
    string providerType = string.IsNullOrEmpty(settings.providerType) ? GenericSparql : settings.providerType.Trim();

    switch (providerType.ToLowerInvariant())
    {
      case "allegrograph":
        return CreateAllegroGraphProvider(settings);
      case "graphdb":
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
}
