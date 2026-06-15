using VDS.RDF;

public interface ISparqlProvider
{
  string ProviderName { get; }

  void QueryWithResultGraph(string query, GraphCallback callback, object state = null);

  void QueryWithResultSet(string query, SparqlResultsCallback callback, object state = null);
}
