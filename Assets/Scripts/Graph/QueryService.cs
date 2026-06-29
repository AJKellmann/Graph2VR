using Dweiss;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using UnityEngine;
using VDS.RDF;
using VDS.RDF.Parsing;
using VDS.RDF.Query;
using VDS.RDF.Query.Patterns;

public class QueryService : MonoBehaviour
{
  public int queryLimit = 25;
  public static int searchResultsLimit = 100;

  public const string PREFIXES = @"
    prefix rdf: <http://www.w3.org/1999/02/22-rdf-syntax-ns#>
    prefix owl: <http://www.w3.org/2002/07/owl#>
    prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#>
    prefix skos: <http://www.w3.org/2004/02/skos/core#>";

  public string GetLimitClause()
  {
    return queryLimit > 0 ? $"LIMIT {queryLimit}" : "";
  }

  private string GetSelectedGraphClause()
  {
    if (string.IsNullOrEmpty(Settings.Instance.baseURI) || sparqlProvider == null)
    {
      return "";
    }

    string providerName = sparqlProvider.ProviderName;
    bool providerNeedsInlineGraph =
      string.Equals(providerName, SparqlProviderFactory.GraphDB, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(providerName, SparqlProviderFactory.AllegroGraph, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(providerName, SparqlProviderFactory.Stardog, StringComparison.OrdinalIgnoreCase);

    return providerNeedsInlineGraph ? $"FROM <{Settings.Instance.baseURI}>" : "";
  }

  public INamespaceMapper defaultNamespace = new NamespaceMapper(true);

  ISparqlProvider sparqlProvider = null;
  private void Awake()
  {
    SetupSingelton();
    AddDefaultNamespaces();
    SwitchEndpoint();
  }

  public void SwitchEndpoint()
  {
    this.sparqlProvider = GetProvider();
    Debug.Log($"SPARQL provider selected | provider: {sparqlProvider.ProviderName} | endpoint: {Settings.Instance.sparqlEndpoint} | baseURI: {Settings.Instance.baseURI}");
  }

  private void AddDefaultNamespaces()
  {
    defaultNamespace.AddNamespace("rdf", new Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#"));
    defaultNamespace.AddNamespace("owl", new Uri("http://www.w3.org/2002/07/owl#"));
    // For nice demo's
    defaultNamespace.AddNamespace("dbpedia", new Uri("http://dbpedia.org/resource/"));
    defaultNamespace.AddNamespace("dbpedia/ontology", new Uri("http://dbpedia.org/ontology/"));
  }

  public void ExpandGraph(Node node, string uri, bool isOutgoingLink, Action<IGraph, IGraph, object> queryCallback)
  {
    string refinmentQuery = GetExpandGraphQuery(node, uri, isOutgoingLink);
    string dataquery = GetSimpleExpandGraphQuery(node, uri, isOutgoingLink);

    Debug.Log($"ExpandGraph {(isOutgoingLink ? "outgoing" : "incoming")} | node: {node.graph.RealNodeValue(node.graphNode)} | predicate: <{uri}> | limit: {queryLimit}\nData query:\n{dataquery}\nRefinement query:\n{refinmentQuery}");
    QuerySessionLogger.Log("ExpandGraph refinement " + (isOutgoingLink ? "outgoing" : "incoming"), refinmentQuery);
    sparqlProvider.QueryWithResultGraph(refinmentQuery, (completeGraph, state) =>
    {
      if (state != null)
      {
        LogQueryState("ExpandGraph remote query", state);
      }

      if (completeGraph == null)
      {
        Debug.LogWarning($"ExpandGraph returned no refinement graph. Direction: {(isOutgoingLink ? "outgoing" : "incoming")}, node: {node.graph.RealNodeValue(node.graphNode)}, predicate: <{uri}>");
        QueryDataGraphFallback(dataquery, queryCallback);
        return;
      }

      IGraph dataGraph = null;
      try
      {
        dataGraph = completeGraph.ExecuteQuery(dataquery) as IGraph;
      }
      catch (Exception exception)
      {
        Debug.LogError($"ExpandGraph local data query failed. Direction: {(isOutgoingLink ? "outgoing" : "incoming")}, node: {node.graph.RealNodeValue(node.graphNode)}, predicate: <{uri}>\n{exception}");
      }

      queryCallback(dataGraph, completeGraph, state);
    }, state: null);
  }

  private void QueryDataGraphFallback(string dataquery, Action<IGraph, IGraph, object> queryCallback)
  {
    Debug.LogWarning("ExpandGraph refinement query failed. Falling back to data-only query.");
    Debug.Log($"ExpandGraph data-only fallback query:\n{dataquery}");
    QuerySessionLogger.Log("ExpandGraph data-only fallback", dataquery);
    sparqlProvider.QueryWithResultGraph(dataquery, (dataGraph, dataState) =>
    {
      if (dataState != null)
      {
        LogQueryState("ExpandGraph data-only fallback", dataState);
      }
      queryCallback(dataGraph, null, dataState);
    }, state: null);
  }


  private string GetSimpleExpandGraphQuery(Node node, string uri, bool isOutgoingLink)
  {
    string value = node.graph.RealNodeValue(node.graphNode);
    string anchorValue = IsBlankNode(node) ? "?graph2vrAnchor" : value;
    string anchorFilter = GetBlankNodeFilter(node, "?graph2vrAnchor");
    if (isOutgoingLink)
    {

      // Select with label
      return $@"
            {PREFIXES}
            construct {{
                {anchorValue} <{uri}> ?object .
            }}
            {GetSelectedGraphClause()}
            where {{
                {anchorValue} <{uri}> ?object .
                {anchorFilter}
            }} 
            {GetLimitClause()}";
    }
    else
    {
      return $@"
            {PREFIXES}
            construct {{
                ?subject <{uri}> {anchorValue} .
            }}
            {GetSelectedGraphClause()}
            where {{
                ?subject <{uri}> {anchorValue}
                {anchorFilter}
            }}  
            {GetLimitClause()}";
    }
  }


  private string GetExpandGraphQuery(Node node, string uri, bool isOutgoingLink)
  {
    string value = node.graph.RealNodeValue(node.graphNode);
    string anchorValue = IsBlankNode(node) ? "?graph2vrAnchor" : value;
    string anchorFilter = GetBlankNodeFilter(node, "?graph2vrAnchor");

    if (isOutgoingLink)
    {

      // Select with label
      return $@"
        {PREFIXES}
        construct {{
            {anchorValue} <{uri}> ?object .
            ?object ?graph2vrlabel ?label .
            ?object ?graph2vrimage ?image .
            ?object ?graph2vrmodel ?model .
            ?object a ?type .
        }}
        {GetSelectedGraphClause()}
        where {{
            {anchorValue} <{uri}> ?object .
            {anchorFilter}
            {GetOptionalGraphQuery("?object")}
        }} 
        {GetLimitClause()}";
    }
    else
    {
      return $@"
        {PREFIXES}
        construct {{
            ?subject <{uri}> {anchorValue} .
            ?subject ?graph2vrlabel ?label .
            ?subject ?graph2vrimage  ?image .
            ?subject ?graph2vrmodel ?model .
            ?subject a ?type .
        }}
        {GetSelectedGraphClause()}
        where {{
            ?subject <{uri}> {anchorValue}
            {anchorFilter}
            {GetOptionalGraphQuery("?subject")}
        }}  
        {GetLimitClause()}";
    }
  }

  private bool IsBlankNode(Node node)
  {
    return node != null && node.graphNode != null && node.graphNode.NodeType == NodeType.Blank;
  }

  private string GetBlankNodeFilter(Node node, string variableName)
  {
    if (!IsBlankNode(node))
    {
      return "";
    }

    return $"FILTER(isBlank({variableName})).";
  }

  private string GetOptionalGraphQuery(string variable)
  {
    string imagePredicates = "";
    string modelPredicates = "";
    bool isFirstPredicate = true;
    foreach (string predicate in Settings.Instance.imagePredicates)
    {
      imagePredicates += (isFirstPredicate ? "" : "|") + " <" + predicate + "> ";
      isFirstPredicate = false;
    }
    isFirstPredicate = true;
    if (Settings.Instance.modelPredicates != null)
    {
      foreach (string predicate in Settings.Instance.modelPredicates)
      {
        modelPredicates += (isFirstPredicate ? "" : "|") + " <" + predicate + "> ";
        isFirstPredicate = false;
      }
    }

    string modelQuery = string.IsNullOrEmpty(modelPredicates) ? "" : $@"
        Optional{{
          Select {variable} (<http://graph2vr.org/model> AS ?graph2vrmodel) (sample(?model) as ?model)
          where {{
            {variable} ({modelPredicates}) ?model .
            FILTER( strStarts( STR(?model), 'http://' ) || strStarts( STR(?model), 'https://' ) || strStarts( STR(?model), 'file://') || strStarts( STR(?model), 'ftp://')).
          }}
          GROUP BY {variable}
        }}
";

    return $@"
        Optional{{
          Select {variable} (<http://graph2vr.org/label> AS ?graph2vrlabel) (sample(STR(?label)) as ?label)
          where {{
            {variable} rdfs:label ?label .
            {LanguageFilterString("?label")}
          }}
          GROUP BY {variable}
        }}

        Optional{{
          Select {variable} (<http://graph2vr.org/image> AS ?graph2vrimage) (sample(?image) as ?image)
          where {{
            {variable} ({imagePredicates}) ?image .
            FILTER( strStarts( STR(?image), 'http://' ) || strStarts( STR(?image), 'https://' ) || strStarts( STR(?image), 'file://') || strStarts( STR(?image), 'ftp://')).
          }}
          GROUP BY {variable}
        }}

        {modelQuery}

        OPTIONAL {{
          {variable} a ?type .
          FILTER(?type = owl:Thing || ?type = owl:Class || ?type = rdfs:subClassOf || ?type = rdf:Property)
        }}
    ";
  }
  public void ExecuteQuery(string query, Action<IGraph, bool> callback, bool additiveMode = false)
  {
    try
    {
      QuerySessionLogger.Log("Execute construct query", query);
      sparqlProvider.QueryWithResultGraph(query, (IGraph results, object state) =>
      {
        callback(results, additiveMode);
      }, state: null);
    }
    catch (RdfQueryException error)
    {
      Debug.Log("No database connection found");
      Debug.Log(error);
    }
  }

  public void QueryByTriples(string triples, Action<IGraph, bool> callback, bool additiveMode = false)
  {
    string query = $@"
            {PREFIXES}
            construct {{
                {removeOptional(triples)} 
            }}
            {GetSelectedGraphClause()}
            where {{
                {triples} 
            }} {GetLimitClause()}";
    if (IsConstructSparqlQuery(query))
    {
      ExecuteQuery(query, callback, additiveMode);
    }
    else
    {
      Debug.Log("Please use a Construct query");
    }
  }

  private string removeOptional(string triples)
  {
    triples = triples.Replace("OPTIONAL {", "");
    triples = triples.Replace("}\n", "");
    return triples;
  }

  Action<List<string>> getGraphsOnSelectedServerCallback;
  public void GetGraphsOnSelectedServer(Action<List<string>> callback)
  {
    getGraphsOnSelectedServerCallback = callback;
    string query = $@"
      SELECT DISTINCT ?graph
      WHERE {{ 
        GRAPH ?graph {{ ?s ?p ?o }}
      }}";

    ISparqlProvider graphListProvider = SparqlProviderFactory.Create(Settings.Instance, ignoreSelectedGraph: true);
    graphListProvider.QueryWithResultSet(query, (results, state) =>
    {
      if (state != null)
      {
        LogQueryState("GetGraphsOnSelectedServer", state);
      }

      List<string> graphNames = new List<string>();
      if (results != null)
      {
        foreach (SparqlResult result in results)
        {
          if (result.TryGetValue("graph", out INode graphNode))
          {
            graphNames.Add(graphNode.ToString());
          }
        }
      }

      UnityMainThreadDispatcher.Instance().Enqueue(() =>
      {
        getGraphsOnSelectedServerCallback(graphNames);
      });
    }, state: null);
  }

  public void GetOutgoingPredicats(string URI, SparqlResultsCallback sparqlResultsCallback)
  {
    string query = $@"
      {PREFIXES}
      select ?p (STR(COUNT(?o)) AS ?count) (SAMPLE(STR(?predicateLabel)) AS ?label)
      {GetSelectedGraphClause()}
      where {{
        <{URI}> ?p ?o .
        OPTIONAL {{
          ?p rdfs:label ?predicateLabel .
          {LanguageFilterString("?predicateLabel")}
        }}
      }}
      GROUP BY ?p
      ORDER BY ?label ?p LIMIT 100";
  //  Debug.Log("GetOutgoingPredicats: "+ query);
    Debug.Log($"GetOutgoingPredicats | uri: {URI}\n{query}");
    QuerySessionLogger.Log("Outgoing predicates", query);
    sparqlProvider.QueryWithResultSet(query, (results, state) =>
    {
      LogPredicateQueryResult("GetOutgoingPredicats", results, state);
      sparqlResultsCallback(results, state);
    }, state: null);
  }

  public void GetIncomingPredicats(string objectValue, SparqlResultsCallback sparqlResultsCallback)
  {
    string query = $@"
      {PREFIXES}
      select ?p (STR(COUNT(?s)) AS ?count) (SAMPLE(STR(?predicateLabel)) AS ?label)
      {GetSelectedGraphClause()}
      where {{ 
        ?s ?p {objectValue} . 
        OPTIONAL {{
          ?p rdfs:label ?predicateLabel .
          {LanguageFilterString("?predicateLabel")}
        }}
      }} 
      GROUP BY ?p
      ORDER BY ?label ?p LIMIT 100";
 //   Debug.Log("GetIncomingPredicats: " + query);
    Debug.Log($"GetIncomingPredicats | object: {objectValue}\n{query}");
    QuerySessionLogger.Log("Incoming predicates", query);
    sparqlProvider.QueryWithResultSet(query, (results, state) =>
    {
      LogPredicateQueryResult("GetIncomingPredicats", results, state);
      sparqlResultsCallback(results, state);
    }, state: null);
  }

  private void LogPredicateQueryResult(string context, SparqlResultSet results, object state)
  {
    if (state != null)
    {
      LogQueryState(context, state);
    }

    if (results == null)
    {
      Debug.LogWarning($"{context} returned no result set.");
      return;
    }

    Debug.Log($"{context} returned {results.Count} predicate rows.");
  }

  public void GetLabelForPredicate(string uri, SparqlResultsCallback callback)
  {
    string query = $@"
        {PREFIXES}
        SELECT (STR(?predicateLabel) AS ?label)
        {GetSelectedGraphClause()}
        WHERE {{
            <{uri}> rdfs:label ?predicateLabel .
            {LanguageFilterString("?predicateLabel")}
        }}
        LIMIT 1";
    sparqlProvider.QueryWithResultSet(query, (SparqlResultSet resultSet, object state) =>
    {
      if (resultSet == null)
      {
        callback(null, state);
        return;
      }
      if (resultSet.Count > 0 && resultSet[0].HasValue("label"))
      {
        callback(resultSet, state);
      }
      else
      {
        callback(null, state);
      }
    }, state: null);
  }

  private Boolean IsConstructSparqlQuery(string query)
  {
    SparqlQuery sparqlQuery = GetSparqlQuery(query);
    return sparqlQuery != null && sparqlQuery.QueryType == SparqlQueryType.Construct;
  }

  private SparqlQuery GetSparqlQuery(string query)
  {
    try
    {
      SparqlQueryParser parser = new();
      SparqlQuery sparqlQuery = null;
      sparqlQuery = parser.ParseFromString(query);

      GraphPattern graphPattern = sparqlQuery.RootGraphPattern;
      return sparqlQuery;
    }
    catch (RdfParseException error)
    {
      Debug.Log("Error parsing query");
      Debug.Log(error);
      return null;
    }
  }

  public void GetDescriptionAsync(string URI, GraphCallback callback)
  {
    string query = "describe <" + URI + ">";
    sparqlProvider.QueryWithResultGraph(query, callback, null);
  }

  public void QuerySimilarPatternsMultipleLayers(string triples, string triplesWithOptional, OrderedDictionary orderByList, List<string> groupByList, bool additiveMode, Action<SparqlResultSet, string, string, bool> callback)
  {
    string query = GetSimilarPatternsQuery(triplesWithOptional, orderByList, groupByList);
    QuerySessionLogger.Log("Query similar patterns", query);
    sparqlProvider.QueryWithResultSet(query, (SparqlResultSet results, object state) =>
    {
      if (state != null)
      {
        LogQueryState("QuerySimilarPatterns", state);
      }
      callback(results, query, triples, additiveMode);
    }, null);
  }

  public string GetSimilarPatternsQuery(string triplesWithOptional, OrderedDictionary orderByList, List<string> groupByList)
  {
    string order = groupByList.Count > 0 ? "" : GetOrderByString(orderByList);
    return $@"
      {PREFIXES}
      select distinct *
      {GetSelectedGraphClause()}
      where {{
        {triplesWithOptional}
      }} {order} {GetLimitClause()}";
  }

  public void CountQuerySimilarPatternsMultipleLayers(Graph graph, string triplesWithOptional, List<string> groupByList, Action<int> callback, string optionalVariable="*")
  {
    string countExpression = optionalVariable == "*" ? "*" : "DISTINCT " + optionalVariable;
    string query = $@"
      {PREFIXES}
      SELECT (COUNT({countExpression}) AS ?count)
      {GetSelectedGraphClause()}
      WHERE {{
        {triplesWithOptional}
      }}";
    Debug.Log($"CountQuerySimilarPatterns | variable: {optionalVariable}\n{query}");
    QuerySessionLogger.Log("Count similar patterns", query);
    sparqlProvider.QueryWithResultSet(query, (SparqlResultSet results, object state) =>
    {
      if (state != null)
      {
        LogQueryState("CountQuerySimilarPatterns", state);
      }
      UnityMainThreadDispatcher.Instance().Enqueue(() =>
      {
        int count = 0;
        if (results == null)
        {
          Debug.LogWarning("CountQuerySimilarPatterns returned no result set.");
          callback(count);
          return;
        }

        foreach (SparqlResult result in results)
        {
          result.TryGetValue("count", out INode iNode);
          ILiteralNode countNode = iNode as ILiteralNode;
          if (countNode != null)
          {
            count = int.Parse(countNode.Value.ToString());
          }
        }
        callback(count);
      });
    }, null);
  }
  

  public void AutocompleteSearch(string searchTerm, SparqlResultsCallback callback, Node variableNode = null)
  {
    if (searchTerm.Length > 3)
    {
      string query = GetAutoCompleteQuery(searchTerm, variableNode);
      sparqlProvider.QueryWithResultSet(query, callback, state: null);
    }
    else
    {
      callback(null, null);
    }
  }

  private string GetAutoCompleteQuery(string searchTerm, Node variableNode)
  {
    if (Settings.Instance.databaseSupportsBifContains)
    {
      return GetAutoCompleteBifQuery(searchTerm, variableNode);
    }
    else
    {
      return GetAutoCompleteNonBifQuery(searchTerm, variableNode);
    }
  }

  private static string LanguageFilterString(string variableName)
  {
    if (Main.instance.languageCode == "")
    {
      return "";
    }
    else
    {
      return $"FILTER(LANG({variableName}) = '' || LANGMATCHES(LANG({variableName}), '{Main.instance.languageCode}')).";
    }
  }
  private static string GetAutoCompleteNonBifQuery(string searchTerm, Node variableNode)
  {
    if (variableNode != null)
    {
      return $@"
              {PREFIXES}
              select distinct {variableNode.GetQueryLabel()} AS ?uri (SAMPLE(?name) AS ?name)
              where {{
                {variableNode.graph.GetTriplesString()}
                {variableNode.GetQueryLabel()} rdfs:label ?name.
                ?uri(^(<>| !<>) | rdfs:label | skos:altLabel) ?entity.
                BIND(STR(?entity) AS ?name).
                FILTER REGEX(?name, '{searchTerm}', 'i').
              }}
              LIMIT {searchResultsLimit}";
    }
    else
    {
      return $@"
              {PREFIXES}
              select distinct ?uri (SAMPLE(?name) AS ?name) 
              where {{
                 ?uri(^(<>| !<>) | rdfs:label | skos:altLabel) ?entity.
                 BIND(STR(?entity) AS ?name).
                 FILTER REGEX(?name, '{searchTerm}', 'i')
              }}
              LIMIT {searchResultsLimit}";
    }
  }

  private string GetAutoCompleteBifQuery(string searchTerm, Node variableNode)
  {
    if (variableNode != null)
    {
      return $@"
               {PREFIXES}
               select distinct {variableNode.GetQueryLabel()} AS ?uri (SAMPLE(?name) AS ?name) 
               where {{
                 {variableNode.graph.GetTriplesString()}
                 {variableNode.GetQueryLabel()} rdfs:label ?name.
                 ?name bif:contains ""'{AddStar(searchTerm)}'"".
               }}
               LIMIT {searchResultsLimit}";
    }
    else
    {
      return $@"
              {PREFIXES}
              select distinct ?uri (SAMPLE(?name) AS ?name) 
              where {{
                ?uri rdfs:label ?name.
                ?name bif:contains ""'{AddStar(searchTerm)}'"".
              }}
              LIMIT {searchResultsLimit}";
    }
  }

  private string AddStar(string searchTerms)
  {
    if (searchTerms.Split(' ').Last().Length > 3)
    {
      return searchTerms + '*';
    }
    else
    {
      return searchTerms;
    }
  }

  public Dictionary<string, List<string>> RefineNode(IGraph refinmentGraph, string uri)
  {
    string query = $@"
            select ?label ?image ?model
            where{{
                optional{{
                  <{uri}> <http://graph2vr.org/label> ?label .
                }}
                optional{{
                  <{uri}> <http://graph2vr.org/image> ?image .
                }}
                optional{{
                  <{uri}> <http://graph2vr.org/model> ?model .
                }}
            }}";

    SparqlResultSet data = refinmentGraph.ExecuteQuery(query) as SparqlResultSet;
    List<string> labels = new();
    List<string> images = new();
    List<string> models = new();

    foreach (SparqlResult result in data)
    {
      string label = ExtractDisplayLabelFrom(result, "label");
      string image = ExtractVariableFrom(result, "image");
      string model = ExtractVariableFrom(result, "model");

      if (label != null)
      {
        labels.Add(label);
      }
      if (image != null)
      {
        images.Add(image);
      }
      if (model != null)
      {
        models.Add(model);
      }
    }

    return new Dictionary<string, List<string>>
    {
      { "labels", labels },
      { "images", images },
      { "models", models },
    };
  }

  private string ExtractVariableFrom(SparqlResult result, string variable)
  {
    if (result.HasValue(variable))
    {
      return result.Value(variable).ToString();
    }
    return null;
  }

  private string ExtractDisplayLabelFrom(SparqlResult result, string variable)
  {
    if (!result.HasValue(variable))
    {
      return null;
    }

    INode node = result.Value(variable);
    if (node is ILiteralNode literalNode)
    {
      return literalNode.Value;
    }

    string value = node.ToString();
    if (Uri.IsWellFormedUriString(value, UriKind.Absolute))
    {
      return Utils.GetShortLabelFromUri(value);
    }

    return value;
  }

  public static string GetOrderByString(OrderedDictionary orderByList)
  {
    if (orderByList.Count > 0)
    {
      string result = "Order By ";
      foreach (DictionaryEntry order in orderByList)
      {
        result += $"{order.Value}({order.Key}) ";
      }
      return result;
    }
    return "";
  }

  private static string GetGroupByString(List<string> groupByList)
  {
    if (groupByList.Count > 0)
    {
      string result = "Group By ";
      foreach (string group in groupByList)
      {
        result += $"{group} ";
      }
      return result;
    }
    return "";
  }

  private ISparqlProvider GetProvider()
  {
    return SparqlProviderFactory.Create(Settings.Instance);
  }

  private void LogQueryState(string context, object state)
  {
    AsyncError asyncError = state as AsyncError;
    if (asyncError != null)
    {
      Debug.LogWarning($"{context} returned AsyncError. Original state: {asyncError.State}\n{asyncError.Error}");
      if (asyncError.Error != null && asyncError.Error.InnerException != null)
      {
        Debug.LogWarning($"{context} inner exception:\n{asyncError.Error.InnerException}");
      }
      return;
    }

    Debug.LogWarning($"{context} returned state: {state}");
  }

  #region  Singleton
  public static QueryService _instance;
  public static QueryService Instance { get { return _instance; } }
  private void SetupSingelton()
  {
    if (_instance != null)
    {
      Debug.LogError("Error in settings. Multiple singletons exists: " + _instance.name + " and now " + this.name);
    }
    else
    {
      _instance = this;
    }
  }
  #endregion
}
