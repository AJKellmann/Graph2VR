using VDS.RDF;
using VDS.RDF.Query;

public class SparqlRemoteEndpointProvider : ISparqlProvider
{
  private readonly SparqlRemoteEndpoint endpoint;

  public string ProviderName { get; private set; }

  public SparqlRemoteEndpointProvider(SparqlRemoteEndpoint endpoint, string providerName)
  {
    this.endpoint = endpoint;
    ProviderName = providerName;
  }

  public void QueryWithResultGraph(string query, GraphCallback callback, object state = null)
  {
    endpoint.QueryWithResultGraph(query, callback, state);
  }

  public void QueryWithResultSet(string query, SparqlResultsCallback callback, object state = null)
  {
    endpoint.QueryWithResultSet(query, callback, state);
  }
}
