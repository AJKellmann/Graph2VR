using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using VDS.RDF;
using VDS.RDF.Query;

public class Graph : MonoBehaviour
{
  public bool loading = false;
  public string GUID;
  public BaseLayoutAlgorithm layout = null;
  public BoundingSphere boundingSphere;
  public GameObject edgePrefab;
  public GameObject nodePrefab;
  public Canvas menu;

  public List<Edge> edgeList = new();
  public List<Node> nodeList = new();
  public List<string> translatablePredicates = new();
  public OrderedDictionary orderBy = new();
  public List<string> groupBy = new();
  public VariableNameManager variableNameManager;
  public List<Edge> selection = new();

  public List<Graph> subGraphs = new();
  public Graph parentGraph = null;
  public string creationQuery = "";
  public enum Layout : ushort { FruchtermanReingold = 0, SpatialGrid2D, HierarchicalView, ClassHierarchy, SemanticPlanes, BarnesHut3D, LouvainCluster3D }
  public Layout currentLayout = Layout.BarnesHut3D;

  public ApplicationState.GraphState graphState;
  private Canvas queryPreviewPanel;

  private class GroupedQueryResult
  {
    public string key;
    public List<SparqlResult> rows = new();
  }

  public Graph QuerySimilarWithTriples(string triples, Vector3 position, Quaternion rotation, Graph graphToUse = null, bool additiveMode = false)
  {
    Graph graph = graphToUse;
    if (graph == null)
    {
      graph = Main.instance.CreateGraph();
      graph.parentGraph = this;
      subGraphs.Add(graph);
      graph.transform.position = position;
      graph.transform.rotation = rotation;
    }
    graph.CreateGraphByTriples(triples, additiveMode);
    return graph;
  }

  public string GetTriplesString()
  {
    return selection.Aggregate(string.Empty, (accum, edge) => accum += edge.GetQueryString());
  }
  public string GetTriplesStringWithOptional()
  {
    return selection.Aggregate(string.Empty, (accum, edge) => accum += GetEdgeTrippleWithOptional(edge));
  }

  private string GetEdgeTrippleWithOptional(Edge edge)
  {
    if (edge.IsOptional)
    {
      return "OPTIONAL {" + edge.GetQueryString() + " BIND (true as ?optionalTripelExists" + edge.optionalTripleCounter + ")}\n";
    }
    return edge.GetQueryString();
  }

  public string RealNodeValue(INode node)
  {
    if (node == null) return "";
    return node.NodeType switch
    {
      NodeType.GraphLiteral or NodeType.Literal => GetLiteralValue(node),
      NodeType.Uri => $"<{GetUriNodeValue(node)}>",
      NodeType.Blank => GetBlankNodeValue(node),
      NodeType.Variable => (node as IVariableNode).VariableName,
      _ => "",
    };
  }

  private string GetBlankNodeValue(INode node)
  {
    IBlankNode blankNode = node as IBlankNode;
    string internalId = blankNode?.InternalID;
    if (string.IsNullOrEmpty(internalId))
    {
      internalId = "blankNode";
    }

    string safeId = Regex.Replace(internalId, @"[^A-Za-z0-9_]", "_");
    if (!Regex.IsMatch(safeId, @"^[A-Za-z_]"))
    {
      safeId = "b" + safeId;
    }
    return "_:" + safeId;
  }

  private string GetLiteralValue(INode node)
  {
    ILiteralNode literal = (node as ILiteralNode);
    string dataType = literal.DataType?.ToString();
    string language = literal.Language?.ToString();
    string value = literal.Value?.ToString();

    string result = $"'{value}'";
    if (language != "" && language != null)
    {
      result += $"@{language}";
    }
    if (dataType != "" && dataType != null)
    {
      result += $"^^<{dataType}>";
    }
    return result;
  }

  private string GetNodeValue(INode node)
  {
    if (node == null)
    {
      return "";
    }

    return node.NodeType == NodeType.Uri ? GetUriNodeValue(node) : node.ToString();
  }

  private string GetUriNodeValue(INode node)
  {
    return (node as IUriNode).Uri.AbsoluteUri;
  }

  public void QuerySimilarPatternsSingleLayer()
  {
    bool selectionContainsOptional = false;
    foreach (Edge triple in selection)
    {
      if (triple.isOptional) selectionContainsOptional = true;
    }

    if (selectionContainsOptional)
    {
      QueryService.Instance.QuerySimilarPatternsMultipleLayers(GetTriplesString(), GetTriplesStringWithOptional(), orderBy, groupBy, true, QuerySimilarPatternsCallback);
    }
    else
    {
      QuerySimilarWithTriples(GetTriplesString(), new Vector3(0, 0, 2), Quaternion.identity);
    }
  }

  public void QuerySimilarPatternsMultipleLayers()
  {
    QueryService.Instance.QuerySimilarPatternsMultipleLayers(GetTriplesString(), GetTriplesStringWithOptional(), orderBy, groupBy, false, QuerySimilarPatternsCallback);
  }

  public void CountQuerySimilarPatternsMultipleLayers(Action<int> callback, string optionalVariable="*")
  {
    QueryService.Instance.CountQuerySimilarPatternsMultipleLayers(this, GetTriplesStringWithOptional(), groupBy, callback, optionalVariable);
  }

  void QuerySimilarPatternsCallback(SparqlResultSet results, string query, string triples, bool additiveMode)
  {
    UnityMainThreadDispatcher.Instance().Enqueue(() =>
    {
      if (results == null)
      {
        Debug.LogWarning("QuerySimilarPatterns returned no result set.");
        return;
      }

      List<SparqlResult> sortedResults = SortQueryResults(results);
      if (groupBy.Count > 0)
      {
        BuildGroupedQueryResults(results, query, triples);
        return;
      }

      Graph additiveModeGraph = null;
      if (additiveMode)
      {
        additiveModeGraph = Main.instance.CreateGraph();
        additiveModeGraph.parentGraph = this;
        subGraphs.Add(additiveModeGraph);
        additiveModeGraph.transform.position = new Vector3(0, 1, 0);
        additiveModeGraph.transform.rotation = Quaternion.identity;
      }

      Quaternion rotation = Camera.main.transform.rotation;
      Vector3 offset = transform.position + (rotation * new Vector3(0, 0, 1 + boundingSphere.size));
      foreach (SparqlResult result in sortedResults)
      {
        string preSelectedQuery = BuildPreSelectedQuery(triples, result);
        Graph newGraph = QuerySimilarWithTriples(preSelectedQuery, offset, Quaternion.identity, additiveModeGraph, additiveMode);
        if (!additiveMode)
        {
          offset += rotation * new Vector3(0, 0, 0.5f);
          SetupNewGraph(newGraph, query, rotation, results);
        }
      }
    });
  }

  public string GetSparqlQueryPreview()
  {
    if (selection == null || selection.Count == 0)
    {
      return "# Select one or more triples to preview a SPARQL query.";
    }

    bool selectionContainsOptional = selection.Exists(edge => edge.isOptional);
    if (selectionContainsOptional || groupBy.Count > 0 || orderBy.Count > 0)
    {
      return QueryService.Instance.GetSimilarPatternsQuery(GetTriplesStringWithOptional(), orderBy, groupBy);
    }

    string triples = GetTriplesString();
    return $@"
      {QueryService.PREFIXES}
      construct {{
        {triples}
      }} where {{
        {triples}
      }} {QueryService.Instance.GetLimitClause()}";
  }

  public void ToggleQueryPreviewPanel()
  {
    if (queryPreviewPanel == null)
    {
      queryPreviewPanel = Instantiate<Canvas>(Resources.Load<Canvas>("UI/ContextMenu"));
      queryPreviewPanel.renderMode = RenderMode.WorldSpace;
      queryPreviewPanel.worldCamera = GameObject.Find("Controller (right)").GetComponentInChildren<Camera>();
      queryPreviewPanel.gameObject.AddComponent<PanelGrabInteraction>();
      QueryPreviewPanel preview = queryPreviewPanel.gameObject.AddComponent<QueryPreviewPanel>();
      preview.Initiate(this);
    }
    else
    {
      queryPreviewPanel.gameObject.SetActive(!queryPreviewPanel.gameObject.activeSelf);
    }

    PositionQueryPreviewPanel();
  }

  private void PositionQueryPreviewPanel()
  {
    queryPreviewPanel.transform.SetParent(null);
    queryPreviewPanel.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
    queryPreviewPanel.transform.SetParent(transform, true);
    queryPreviewPanel.transform.position = transform.position;
    queryPreviewPanel.transform.rotation = Quaternion.LookRotation(queryPreviewPanel.transform.position - Camera.main.transform.position, Vector3.up);
    queryPreviewPanel.transform.position += queryPreviewPanel.transform.rotation * new Vector3(0.8f, 0.2f, 0);
  }

  private void BuildGroupedQueryResults(SparqlResultSet results, string query, string triples)
  {
    Quaternion rotation = Camera.main.transform.rotation;
    Vector3 offset = transform.position + (rotation * new Vector3(0, 0, 1 + boundingSphere.size));
    List<GroupedQueryResult> groupedResults = GroupQueryResults(results);

    foreach (GroupedQueryResult group in groupedResults)
    {
      Graph groupGraph = Main.instance.CreateGraph();
      groupGraph.parentGraph = this;
      subGraphs.Add(groupGraph);
      groupGraph.transform.position = offset;
      groupGraph.transform.rotation = Quaternion.identity;
      groupGraph.creationQuery = query;

      IGraph resultGraph = BuildGraphFromResults(group.rows);
      if (resultGraph.Triples.Any())
      {
        groupGraph.BuildByIGraph(resultGraph);
        SetupNewGraph(groupGraph, query, rotation, results);
        offset += rotation * new Vector3(0, 0, GetGroupedGraphSpacing(groupGraph));
      }
      else
      {
        Destroy(groupGraph.gameObject);
        subGraphs.Remove(groupGraph);
      }
    }
  }

  private float GetGroupedGraphSpacing(Graph groupGraph)
  {
    float radius = CalculateGraphRadius(groupGraph);
    return Mathf.Max(2.0f, (radius * 2.0f) + 0.75f);
  }

  private float CalculateGraphRadius(Graph graph)
  {
    if (graph == null || graph.nodeList.Count == 0)
    {
      return 1.0f;
    }

    Vector3 center = Vector3.zero;
    foreach (Node node in graph.nodeList)
    {
      center += node.transform.position;
    }
    center /= graph.nodeList.Count;

    float radius = 0.0f;
    foreach (Node node in graph.nodeList)
    {
      radius = Mathf.Max(radius, Vector3.Distance(center, node.transform.position));
    }

    return radius;
  }

  private IGraph BuildGraphFromResults(List<SparqlResult> results)
  {
    IGraph resultGraph = new VDS.RDF.Graph();
    resultGraph.NamespaceMap.Import(QueryService.Instance.defaultNamespace);

    foreach (SparqlResult result in results)
    {
      foreach (Edge edge in selection)
      {
        if (edge.isOptional && !result.HasBoundValue("optionalTripelExists" + edge.optionalTripleCounter))
        {
          continue;
        }

        INode subject = ResolveQueryNode(edge.displaySubject, result);
        INode predicate = ResolveQueryPredicate(edge, result);
        INode graphObject = ResolveQueryNode(edge.displayObject, result);

        if (subject == null || predicate == null || graphObject == null)
        {
          continue;
        }

        resultGraph.Assert(new Triple(CopyNodeToGraph(subject, resultGraph), CopyNodeToGraph(predicate, resultGraph), CopyNodeToGraph(graphObject, resultGraph)));
      }
    }

    return resultGraph;
  }

  private INode CopyNodeToGraph(INode node, IGraph graph)
  {
    return Tools.CopyNode(node, graph);
  }

  private INode ResolveQueryNode(Node node, SparqlResult result)
  {
    if (!node.IsVariable)
    {
      return node.graphNode;
    }
    return ResultNode(result, node.GetQueryLabel());
  }

  private INode ResolveQueryPredicate(Edge edge, SparqlResult result)
  {
    if (!edge.IsVariable)
    {
      return edge.graphPredicate;
    }
    INode predicate = ResultNode(result, edge.variableName);
    return predicate != null && predicate.NodeType == NodeType.Uri ? predicate : null;
  }

  private List<GroupedQueryResult> GroupQueryResults(SparqlResultSet results)
  {
    Dictionary<string, GroupedQueryResult> resultByGroupKey = new();
    foreach (SparqlResult result in results)
    {
      string groupKey = GroupKey(result);
      if (!resultByGroupKey.TryGetValue(groupKey, out GroupedQueryResult group))
      {
        group = new GroupedQueryResult { key = groupKey };
        resultByGroupKey.Add(groupKey, group);
      }
      group.rows.Add(result);
    }

    List<GroupedQueryResult> groupedResults = resultByGroupKey.Values.ToList();
    foreach (GroupedQueryResult group in groupedResults)
    {
      group.rows.Sort(CompareQueryResults);
    }
    groupedResults.Sort(CompareGroupedQueryResults);
    return groupedResults;
  }

  private List<SparqlResult> SortQueryResults(SparqlResultSet results)
  {
    List<SparqlResult> sortedResults = results.ToList();
    sortedResults.Sort(CompareQueryResults);
    return sortedResults;
  }

  private int CompareQueryResults(SparqlResult left, SparqlResult right)
  {
    foreach (string group in groupBy)
    {
      int compare = String.Compare(ResultValue(left, group), ResultValue(right, group), StringComparison.OrdinalIgnoreCase);
      if (compare != 0) return compare;
    }

    foreach (DictionaryEntry order in orderBy)
    {
      int compare = CompareResultValues(left, right, order.Key.ToString());
      if (compare != 0)
      {
        return order.Value.ToString() == "DESC" ? -compare : compare;
      }
    }

    return 0;
  }

  private int CompareGroupedQueryResults(GroupedQueryResult left, GroupedQueryResult right)
  {
    SparqlResult leftFirst = left.rows.FirstOrDefault();
    SparqlResult rightFirst = right.rows.FirstOrDefault();
    if (leftFirst != null && rightFirst != null)
    {
      foreach (DictionaryEntry order in orderBy)
      {
        int compare = CompareResultValues(leftFirst, rightFirst, order.Key.ToString());
        if (compare != 0)
        {
          return order.Value.ToString() == "DESC" ? -compare : compare;
        }
      }
    }

    return String.Compare(left.key, right.key, StringComparison.OrdinalIgnoreCase);
  }

  private int CompareResultValues(SparqlResult left, SparqlResult right, string variable)
  {
    INode leftValue = ResultNode(left, variable);
    INode rightValue = ResultNode(right, variable);

    if (TryGetLiteralDouble(leftValue, out double leftNumber) && TryGetLiteralDouble(rightValue, out double rightNumber))
    {
      return leftNumber.CompareTo(rightNumber);
    }

    if (TryGetLiteralDate(leftValue, out DateTime leftDate) && TryGetLiteralDate(rightValue, out DateTime rightDate))
    {
      return leftDate.CompareTo(rightDate);
    }

    return String.Compare(ResultValue(left, variable), ResultValue(right, variable), StringComparison.OrdinalIgnoreCase);
  }

  private bool TryGetLiteralDouble(INode node, out double value)
  {
    value = 0;
    return node is ILiteralNode literal
      && double.TryParse(literal.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
  }

  private bool TryGetLiteralDate(INode node, out DateTime value)
  {
    value = default;
    return node is ILiteralNode literal
      && DateTime.TryParse(literal.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
  }

  private string GroupKey(SparqlResult result)
  {
    return groupBy.Aggregate("", (key, variable) => key + "|" + ResultValue(result, variable));
  }

  private string ResultValue(SparqlResult result, string variable)
  {
    INode value = ResultNode(result, variable);
    return value != null ? value.ToString() : "";
  }

  private INode ResultNode(SparqlResult result, string variable)
  {
    string variableName = variable.TrimStart('?');
    return result.TryGetValue(variableName, out INode value) ? value : null;
  }

  private string BuildPreSelectedQuery(string triples, SparqlResult result)
  {
    string preSelectedQuery = "";
    foreach (string line in triples.Split(" .\n"))
    {
      if (line == "") continue;
      Edge selectedEdge = null;

      foreach (Edge selected in selection)
      {
        string selectedLine = selected.GetQueryString();
        if ((line + " .\n").Trim().CompareTo(selectedLine.Trim()) == 0)
        {
          selectedEdge = selected;
        }
      }

      bool removeLine = false;
      if (selectedEdge != null && selectedEdge.isOptional)
      {
        bool optionalExistsInResult = result.HasBoundValue("optionalTripelExists" + selectedEdge.optionalTripleCounter);
        if (!optionalExistsInResult) removeLine = true;
      }

      if (!removeLine)
      {
        preSelectedQuery += line + " .\n";
      }
    }

    foreach (var node in result)
    {
      if (RealNodeValue(node.Value) != "")
      {
        preSelectedQuery = ReplaceSparqlVariable(preSelectedQuery, node.Key, RealNodeValue(node.Value));
      }
    }

    return preSelectedQuery;
  }

  private string ReplaceSparqlVariable(string query, string variableName, string value)
  {
    string pattern = $@"(?<![A-Za-z0-9_])\?{Regex.Escape(variableName)}(?![A-Za-z0-9_])";
    return Regex.Replace(query, pattern, value);
  }

  private void SetupNewGraph(Graph newGraph, string query, Quaternion rotation, SparqlResultSet results)
  {
    newGraph.creationQuery = query;
    SemanticPlanes planes = newGraph.gameObject.GetComponent<SemanticPlanes>();
    planes.lookDirection = rotation;
    planes.parentGraph = this;
    planes.variableNameLookup = results;
    planes.enabled = true;
    newGraph.layout = planes;
    newGraph.boundingSphere.lookDirection = rotation;
    newGraph.SetLayout(Layout.SemanticPlanes);
    newGraph.boundingSphere.unhideOnFirstResult = false;
  }

  public void AddToSelection(Edge toAdd)
  {
    selection.Add(toAdd);
  }

  public void RemoveFromSelection(Edge toRemove)
  {
    selection.Remove(toRemove);
  }

  public void GetOutgoingPredicats(string URI, SparqlResultsCallback sparqlResultsCallback)
  {
    if (URI == "") return;
    QueryService.Instance.GetOutgoingPredicats(URI, sparqlResultsCallback);
  }

  public void GetIncomingPredicats(string objectValue, SparqlResultsCallback sparqlResultsCallback)
  {
    if (objectValue == "") return;
    QueryService.Instance.GetIncomingPredicats(objectValue, sparqlResultsCallback);
  }

  public void CollapseGraph(Node node)
  {
    creationQuery = "";
    CollapseIncomingGraph(node);
    CollapseOutgoingGraph(node);
  }

  public void CollapseIncomingGraph(Node node)
  {
    creationQuery = "";
    CollapseGraph(node, RemoveIncoming);
  }

  private void RemoveIncoming(Node node, Edge edge)
  {
    if (edge.displayObject == node && edge.displaySubject.connections.Count == 1)
    {
      creationQuery = "";
      RemoveNode(edge.displaySubject);
      node.connections.Remove(edge);
    }
  }

  public void CollapseOutgoingGraph(Node node)
  {
    creationQuery = "";
    CollapseGraph(node, RemoveOutgoing);
  }

  private void RemoveOutgoing(Node node, Edge edge)
  {
    if (edge.displaySubject == node && edge.displayObject.connections.Count == 1)
    {
      creationQuery = "";
      RemoveNode(edge.displayObject);
      node.connections.Remove(edge);
    }
  }

  private void CollapseGraph(Node node, Action<Node, Edge> removeFunction)
  {
    creationQuery = "";
    // Reverse iterate so we can savely remove items from the list while doing the iteration
    for (int i = node.connections.Count - 1; i >= 0; i--)
    {
      Edge edge = node.connections[i];
      removeFunction(node, edge);
    }
  }

  public void ExpandGraph(Node node, string uri, bool isOutgoingLink)
  {
    creationQuery = "";
    QueryService.Instance.ExpandGraph(node, uri, isOutgoingLink, ((graph, refinmentGraph, state) =>
    {
      if (state != null)
      {
        if (state is AsyncError asyncError)
        {
          Debug.LogWarning(asyncError.Error);
        }
        else
        {
          Debug.LogWarning($"ExpandGraph returned state: {state}");
        }
      }

      if (graph == null || graph.Triples == null || graph.Triples.Count == 0)
      {
        Debug.LogWarning($"ExpandGraph returned no data triples. Node: {node.uri}, predicate: {uri}, direction: {(isOutgoingLink ? "outgoing" : "incoming")}, limit: {QueryService.Instance.queryLimit}");
        return;
      }

      // To draw new elements to unity we need to be on the main Thread
      UnityMainThreadDispatcher.Instance().Enqueue(() =>
      {
        foreach (Triple triple in graph.Triples)
        {
          AddTriple(triple);
        }

        foreach (Node nodeToRefine in nodeList)
        {
          if (nodeToRefine.graphNode.NodeType != NodeType.Uri) continue;

          if (refinmentGraph != null)
          {
            Dictionary<string, List<string>> results = QueryService.Instance.RefineNode(refinmentGraph, nodeToRefine.uri);
            List<string> images = results["images"];
            List<string> models = results["models"];
            if (results["labels"].Count > 0)
            {
              nodeToRefine.SetLabel(results["labels"].First());
            }
            nodeToRefine.SetImageFromList(images);
            nodeToRefine.SetModelFromList(models);
          }
        }
      });
    }));
  }

  private void AddTriple(Triple triple)
  {
    creationQuery = "";
    AddObjects(triple);
    AddSubjects(triple);
    AddEdges(triple);
    if (!loading) layout.CalculateLayout();
  }

  private void AddEdges(Triple triple)
  {
    if (!edgeList.Find(edge => edge.Equals(triple.Subject, triple.Predicate, triple.Object)))
    {
      CreateEdge(triple.Subject, triple.Predicate, triple.Object);
    }
  }

  private void AddSubjects(Triple triple)
  {
    if (IsNonExistantNode(triple.Subject))
    {
      CreateNode(GetNodeValue(triple.Subject), triple.Subject);
    }
  }

  private void AddObjects(Triple triple)
  {
    if (IsNonExistantNode(triple.Object))
    {
      CreateNode(GetNodeValue(triple.Object), triple.Object);
    }
  }

  private bool IsNonExistantNode(INode node)
  {
    return !nodeList.Find(graphicalNode => graphicalNode.graphNode.Equals(node));
  }

  public string GetShortName(string uri)
  {
    string queryLabel = Utils.GetQueryParameterLabel(uri);
    if (!string.IsNullOrEmpty(queryLabel))
    {
      return queryLabel;
    }

    if (QueryService.Instance.defaultNamespace.ReduceToQName(uri, out string qName))
    {
      return qName;
    }

    string[] splittedHashUri = uri.Split('#');
    if (splittedHashUri.Length != 1)
    {
      return Utils.DecodeUriLabel(splittedHashUri[^1]);
    }

    string[] splittedBackslashUri = uri.Split('/');
    if (splittedBackslashUri.Length != 1)
    {
      return Utils.DecodeUriLabel(splittedBackslashUri[^1]);
    }
    else
    {
      return uri;
    }
  }

  public string CleanInfo(string str)
  {
    return str.TrimStart('<', '\'').TrimEnd('>', '\'');
  }

  public void CreateGraphByTriples(string triples, bool additiveMode = false)
  {
    QueryService.Instance.QueryByTriples(triples, RebuildGraphCallback, additiveMode);
  }

  public void CreateGraphBySparqlQuery(string query)
  {
    QueryService.Instance.ExecuteQuery(query, RebuildGraphCallback);
  }

  private void RebuildGraphCallback(IGraph resultGraph, bool additiveMode = false)
  {
    if (resultGraph == null || resultGraph.Triples == null || resultGraph.Triples.Count == 0)
    {
      if (!additiveMode)
      {
        Destroy(gameObject);
      }
    }
    else
    {
      resultGraph.NamespaceMap.Import(QueryService.Instance.defaultNamespace);
      UnityMainThreadDispatcher.Instance().Enqueue(() =>
      {
        BuildByIGraph(resultGraph, additiveMode);

        SemanticPlanes plane = gameObject.GetComponent<SemanticPlanes>();
        if (plane.enabled && this.layout == plane)
        {
          if (!loading) plane.CalculateLayout();
        }
      });
    }
  }

  // Builds a new graph out of an IGraph, deletes the old one
  private void BuildByIGraph(IGraph iGraph, bool additiveMode = false)
  {
    if (!additiveMode)
    {
      Clear();
    }

    foreach (INode node in iGraph.Nodes)
    {
      if (!additiveMode || IsNonExistantNode(node))
      {
        CreateNode(GetNodeValue(node), node);
      }
    }

    foreach (Triple triple in iGraph.Triples)
    {
      AddEdges(triple);
    }

    if (!loading) layout.CalculateLayout();
  }

  public void Clear()
  {
    foreach (Node node in nodeList)
    {
      Destroy(node.gameObject);
    }

    foreach (Edge edge in edgeList)
    {
      Destroy(edge.gameObject);
    }

    nodeList.Clear();
    edgeList.Clear();
  }

  private void Awake()
  {
    GUID = Guid.NewGuid().ToString();
    variableNameManager = new VariableNameManager();
  }

  public Edge CreateEdge(Node from, string uri, Node to)
  {
    Edge edge = InitializeEdge(uri, from, to);
    edgeList.Add(edge);
    to.AddConnection(edge);
    from.AddConnection(edge);
    return edge;
  }

  public void CreateEdge(INode from, INode uri, INode to)
  {
    Node fromNode = GetByINode(from);
    Node toNode = GetByINode(to);

    if (fromNode == null || toNode == null)
    {
      Debug.Log("The Subject and Object needs to be defined to create a edge");
      return;
    }

    Edge edge = InitializeEdge(GetNodeValue(uri), fromNode, toNode);
    edge.graphPredicate = uri;
    edge.graphSubject = from;
    edge.graphObject = to;

    edgeList.Add(edge);
    toNode.AddConnection(edge);
    fromNode.AddConnection(edge);
  }

  private Edge InitializeEdge(string uri, Node from, Node to)
  {
    GameObject clone = GetEdgeClone();
    clone.name = "Edge: " + uri;
    Edge edge = clone.GetComponent<Edge>();
    edge.graph = this;
    edge.uri = uri;
    edge.displaySubject = from;
    edge.displayObject = to;

    NodeFactory nodeFactory = new();

    edge.graphSubject = from.graphNode;
    edge.graphObject = to.graphNode;
    edge.graphPredicate = nodeFactory.CreateUriNode(new Uri(uri));

    // Check if its self referencing
    if (from.uri != null && from.uri != "" && from.uri == to.uri)
    {
      edge.lineType = Edge.LineType.Circle;
    }
    else
    {
      edge.UpdateEdgeLines();
    }

    return edge;
  }

  private GameObject GetEdgeClone()
  {
    GameObject clone = Instantiate<GameObject>(edgePrefab);
    clone.transform.SetParent(transform);
    clone.transform.localPosition = Vector3.zero;
    clone.transform.localRotation = Quaternion.identity;
    clone.transform.localScale = Vector3.one;
    return clone;
  }

  public Node GetExistingNode(string nodeName)
  {
    return nodeList.Find((Node node) => node.name == nodeName);
  }

  public Node CreateNode(string value, INode iNode)
  {
    string name = "Node: " + value;
    Node existingNode = GetExistingNode(name);
    if (existingNode != null)
    {
      return existingNode;
    }

    GameObject clone = Instantiate<GameObject>(nodePrefab);
    clone.name = "Node: " + value;
    clone.transform.SetParent(transform);
    clone.transform.localPosition = UnityEngine.Random.insideUnitSphere * 3f;
    clone.transform.localRotation = Quaternion.identity;
    clone.transform.localScale = Vector3.one * 0.05f;
    Node node = CreateNodeFromClone(value, clone);
    node.graphNode = iNode;
    return node;
  }

  public Node CreateNode(string value, Vector3 position, string literalDateType = "", string literalLang = "")
  {
    string name = "Node: " + value;
    Node existingNode = GetExistingNode(name);
    if (existingNode != null)
    {
      return existingNode;
    }

    GameObject clone = Instantiate<GameObject>(nodePrefab);
    clone.name = name;
    clone.transform.SetParent(transform);
    clone.transform.position = position;
    clone.transform.localRotation = Quaternion.identity;
    clone.transform.localScale = Vector3.one * 0.05f;
    Node node = CreateNodeFromClone(value, clone);
    NodeFactory nodeFactory = new();


    if (Uri.IsWellFormedUriString(value, UriKind.Absolute))
    {
      node.graphNode = nodeFactory.CreateUriNode(new Uri(value));
    }
    else
    {
      if (Uri.IsWellFormedUriString(literalDateType, UriKind.Absolute))
      {
        node.graphNode = nodeFactory.CreateLiteralNode(value, new Uri(literalDateType));
      }
      else if (literalLang != "")
      {
        node.graphNode = nodeFactory.CreateLiteralNode(value, literalLang);
      }
    }

    return node;
  }

  private Node CreateNodeFromClone(string value, GameObject clone)
  {
    Node node = clone.AddComponent<Node>();

    node.graph = this;
    node.SetURI(value);
    node.SetLabel(Uri.IsWellFormedUriString(value, UriKind.Absolute) ? Utils.GetShortLabelFromUri(value) : value);
    if (nodeList.Count == 0 && boundingSphere.unhideOnFirstResult)
    {
      boundingSphere.Show();
    }
    nodeList.Add(node);
    return node;
  }

  public void AddNodeFromDatabase(Node variableNode = null)
  {
    AutocompleteHandeler.Instance.SearchForNode((string label, string uri) =>
    {
      Vector3 nodeSpawnPosition = GameObject.FindGameObjectWithTag("LeftController").transform.position;
      Node preExistingNode = null;
      foreach (Node node in nodeList)
      {
        if (node.uri != "" && uri == node.uri)
        {
          preExistingNode = node;
        }
        else if (node.uri == "" && label == node.label)
        {
          preExistingNode = node;
        }
      }
      if (preExistingNode != null)
      {
        preExistingNode.gameObject.transform.position = nodeSpawnPosition;
      }
      else
      {
        Node newNode = CreateNode(uri, nodeSpawnPosition);
        newNode.SetLabel(label);
      }
    }, variableNode);
  }

  public Node GetByINode(INode iNode)
  {
    return nodeList.Find((Node node) => node.graphNode.Equals(iNode));
  }

  // Removes a node from the graph. This will also remove all the edges leading to this node.
  // Settings to removeEmptyLeaves to true will remove any connected node that only has this node as a connection.
  public void RemoveNode(Node node, bool removeEmptyLeaves = false)
  {
    if (node != null)
    {
      creationQuery = "";
      // Reverse iterate so we can savely remove items from the list while doing the iteration
      for (int i = node.connections.Count - 1; i >= 0; i--)
      {
        Edge edge = node.connections[i];
        if (edge.IsSelected)
        {
          edge.IsSelected = false;
          edge.graph.RemoveFromSelection(edge);
          // In case there is an edge between two graphs, this should also be removed.
          this.RemoveFromSelection(edge);
        }
        edge.displayObject.connections.Remove(edge);
        edge.displaySubject.connections.Remove(edge);
        edgeList.Remove(edge);
        Destroy(edge.gameObject);
        Node otherNode = edge.displayObject == node ? edge.displaySubject : edge.displayObject;
        if (removeEmptyLeaves && otherNode.connections.Count == 0)
        {
          RemoveNode(otherNode);
        }
      }

      nodeList.Remove(node);
      Destroy(node.gameObject);
    }
  }

  public void RemoveEdge(Edge edge)
  {
    creationQuery = "";
    if (edge.IsSelected)
    {
      edge.IsSelected = false;
      edge.graph.RemoveFromSelection(edge);
      this.RemoveFromSelection(edge);
    }
    edgeList.Remove(edge);
    edge.Remove();

    Destroy(edge.gameObject);
  }

  public void Remove()
  {
    if (boundingSphere != null)
    {
      Destroy(boundingSphere.gameObject);
    }
    if (gameObject != null)
    {
      Destroy(gameObject);
    }
  }

  public void RemoveSubGraphs()
  {
    foreach (Graph graph in subGraphs)
    {
      if (graph != null) graph.Remove();
    }
    subGraphs.Clear();
  }

  public void RemoveGraphsOfSameQuery()
  {
    if (parentGraph != null)
    {
      List<Graph> subGraphsToRemove = new(parentGraph.subGraphs);
      foreach (Graph graph in subGraphsToRemove)
      {
        if (graph != this && graph.creationQuery != null && graph.creationQuery == creationQuery)
        {
          graph.Remove();
          parentGraph.subGraphs.Remove(graph);
        }
      }
      this.creationQuery = null;
    }
  }

  public Layout GetLayout()
  {
    return currentLayout;
  }

  public void SetLayout(Layout layout)
  {
    switch (layout)
    {
      case Layout.FruchtermanReingold:
        SwitchLayout<FruchtermanReingold>();
        boundingSphere.isFlat = false;
        break;
      case Layout.SpatialGrid2D:
        SwitchLayout<SpatialGrid2D>();
        boundingSphere.isFlat = true;
        break;
      case Layout.HierarchicalView:
        SwitchLayout<HierarchicalView>();
        boundingSphere.isFlat = false;
        break;
      case Layout.ClassHierarchy:
        SwitchLayout<ClassHierarchy>();
        boundingSphere.isFlat = false;
        break;
      case Layout.SemanticPlanes:
        SwitchLayout<SemanticPlanes>();
        boundingSphere.isFlat = true;
        break;
      case Layout.BarnesHut3D:
        SwitchLayout<BarnesHut3D>();
        boundingSphere.isFlat = false;
        break;
      case Layout.LouvainCluster3D:
        SwitchLayout<LouvainCluster3D>();
        boundingSphere.isFlat = false;
        break;
    }
    currentLayout = layout;
    if (!loading) this.layout.CalculateLayout();
  }

  private void SwitchLayout<T>() where T : BaseLayoutAlgorithm
  {
    foreach (BaseLayoutAlgorithm baseLayout in GetComponents<BaseLayoutAlgorithm>())
    {
      baseLayout.Stop();
      baseLayout.enabled = false;
    }

    BaseLayoutAlgorithm activeLayout = GetComponent<T>();
    if (activeLayout == null)
    {
      activeLayout = gameObject.AddComponent<T>();
    }
    layout = activeLayout;
    activeLayout.enabled = true;
  }

  public void SortNodes()
  {
    nodeList.Sort((Node a, Node b) => String.Compare(a.textMesh.text, b.textMesh.text));
  }

  public void PinAllNodes(bool pin)
  {
    foreach (Node nodeToPin in nodeList)
    {
      LeanTween.cancel(nodeToPin.gameObject);
      if (pin)
      {
        LeanTween.value(nodeToPin.gameObject, 0.4f, 0.2f, 0.5f).setOnUpdate(value => nodeToPin.transform.Find("Nail").GetComponent<NailRotation>().offset = value);
        {
          nodeToPin.LockPosition = pin;
        }
      }
      else
      {
        LeanTween.value(nodeToPin.gameObject, 0.2f, 0.4f, 0.3f).setOnUpdate(value => nodeToPin.transform.Find("Nail").GetComponent<NailRotation>().offset = value).setOnComplete(() =>
        {
          nodeToPin.LockPosition = pin;
        });
      }
    }
  }
}
