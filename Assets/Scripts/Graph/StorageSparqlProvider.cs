using System;
using System.Threading;
using VDS.RDF;
using VDS.RDF.Query;
using VDS.RDF.Storage;

public class StorageSparqlProvider : ISparqlProvider
{
  private readonly IQueryableStorage queryableStorage;
  private readonly IAsyncQueryableStorage asyncQueryableStorage;

  public string ProviderName { get; private set; }

  public StorageSparqlProvider(IQueryableStorage queryableStorage, string providerName)
  {
    this.queryableStorage = queryableStorage;
    asyncQueryableStorage = queryableStorage as IAsyncQueryableStorage;
    ProviderName = providerName;
  }

  public void QueryWithResultGraph(string query, GraphCallback callback, object state = null)
  {
    Query(query, result =>
    {
      IGraph graph = result as IGraph;
      if (graph != null)
      {
        callback(graph, state);
        return;
      }

      callback(null, CreateAsyncError("The SPARQL query did not return a graph result.", state));
    }, callback, state);
  }

  public void QueryWithResultSet(string query, SparqlResultsCallback callback, object state = null)
  {
    Query(query, result =>
    {
      SparqlResultSet resultSet = result as SparqlResultSet;
      if (resultSet != null)
      {
        callback(resultSet, state);
        return;
      }

      callback(null, CreateAsyncError("The SPARQL query did not return a result set.", state));
    }, callback, state);
  }

  private void Query(string query, Action<object> onSuccess, Delegate callback, object state)
  {
    if (asyncQueryableStorage != null)
    {
      asyncQueryableStorage.Query(query, (sender, args, callbackState) =>
      {
        if (!args.WasSuccessful)
        {
          InvokeErrorCallback(callback, args.Error, callbackState);
          return;
        }

        onSuccess(args.QueryResults);
      }, state);
      return;
    }

    ThreadPool.QueueUserWorkItem(_ =>
    {
      try
      {
        onSuccess(queryableStorage.Query(query));
      }
      catch (Exception exception)
      {
        InvokeErrorCallback(callback, exception, state);
      }
    });
  }

  private void InvokeErrorCallback(Delegate callback, Exception exception, object state)
  {
    AsyncError error = new AsyncError(exception, state);
    GraphCallback graphCallback = callback as GraphCallback;
    if (graphCallback != null)
    {
      graphCallback(null, error);
      return;
    }

    SparqlResultsCallback resultsCallback = callback as SparqlResultsCallback;
    if (resultsCallback != null)
    {
      resultsCallback(null, error);
    }
  }

  private AsyncError CreateAsyncError(string message, object state)
  {
    return new AsyncError(new RdfQueryException(message), state);
  }
}
