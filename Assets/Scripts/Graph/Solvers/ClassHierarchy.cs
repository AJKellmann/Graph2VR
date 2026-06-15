using System.Collections.Generic;
using UnityEngine;

public class ClassHierarchy : BaseLayoutAlgorithm
{
  private static readonly string SUBCLASS_OF_PREDICATE = "http://www.w3.org/2000/01/rdf-schema#subclassof";
  private static readonly string TYPE_PREDICATE = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
  private static readonly string OWL_THING = "http://www.w3.org/2002/07/owl#thing";
  private static readonly string OWL_CLASS = "http://www.w3.org/2002/07/owl#class";
  private static readonly string RDFS_CLASS = "http://www.w3.org/2000/01/rdf-schema#class";
  private readonly float offsetSize = 0.3f;
  private bool running = false;
  private bool notAClassHiearchy = false;
  private int notAClassHiearchyPositionCounter = 0;

  private void Update()
  {
    if (running)
    {
      foreach (Node node in graph.nodeList)
      {
        if (!node.LockPosition)
        {
          node.transform.localPosition = Vector3.Lerp(node.transform.localPosition, node.hierarchicalSettings.targetLocation, Time.deltaTime * 2);
        }
      }
    }
  }

  public override void CalculateLayout()
  {
    ResetNodes();
    graph.SortNodes();
    MarkKnownClassNodes();
    SortNodeList();
    CalculateHierarchicalLevels();
    CalculatePositions();
    running = true;
  }

  private void ResetNodes()
  {
    graph.nodeList.ForEach((Node node) => node.hierarchicalSettings.Reset());
  }

  private void CalculateHierarchicalLevels()
  {
    notAClassHiearchy = false;
    notAClassHiearchyPositionCounter = 0;
    List<Edge> subClassOfEdgeList = graph.edgeList.FindAll(edge => edge.uri.ToLower() == SUBCLASS_OF_PREDICATE);
    Node initialNode = null;
    if (subClassOfEdgeList.Count == 0)
    {
      Debug.Log("Not a class hiearchy");
      notAClassHiearchy = true;
      return;
    }
    else
    {
      initialNode = FindInitialClassRoot(subClassOfEdgeList);
    }

    initialNode.hierarchicalSettings.SetLevel(0);
    SetHierarchicalLevels(initialNode);
    CalculateHierarchicalLevelsForMultipleRootNodes();
  }

  private void CalculateHierarchicalLevelsForMultipleRootNodes()
  {
    foreach (Node node in graph.nodeList)
    {
      if (!node.hierarchicalSettings.levelFound && IsKnownClassNode(node))
      {
        node.hierarchicalSettings.SetLevel(0);
        SetHierarchicalLevels(node);
      }
    }

    foreach (Node node in graph.nodeList)
    {
      if (!node.hierarchicalSettings.levelFound)
      {
        node.hierarchicalSettings.SetLevel(0);
        SetHierarchicalLevels(node);
      }
    }
  }

  private void CalculatePositions()
  {
    float offset = 0;
    foreach (Node node in graph.nodeList)
    {
      if (node.hierarchicalSettings.level == 0)
      {
        float previousOffset = offset;
        offset = PositionNodeLevels(node, 0, offset);
        if (Mathf.Approximately(previousOffset, offset))
        {
          offset += offsetSize;
        }
      }
    }
  }

  private void SortNodeList()
  {
    foreach (Node node in graph.nodeList)
    {
      node.connections.Sort((Edge a, Edge b) =>
      {
        int priority = GetEdgePriority(a).CompareTo(GetEdgePriority(b));
        if (priority != 0) return priority;

        Node partnerA = Utils.GetPartnerNode(node, a);
        Node partnerB = Utils.GetPartnerNode(node, b);
        return string.Compare(partnerA.textMesh.text, partnerB.textMesh.text);
      });
    }
  }

  private void SetHierarchicalLevels(Node node)
  {
    List<Node> nodesToCall = new();
    foreach (Edge edge in node.connections)
    {
      Node partnerNode = SetEdgeHierachicalLevels(node, edge);
      if (partnerNode != null)
      {
        nodesToCall.Add(partnerNode);
      }
    }

    foreach (Node n in nodesToCall)
    {
      SetHierarchicalLevels(n);
    }

    ShiftHierarchyLevels(GetLowestLevel());
  }

  private Node SetEdgeHierachicalLevels(Node node, Edge edge)
  {
    Node partnerNode = Utils.GetPartnerNode(node, edge);
    if (partnerNode.hierarchicalSettings.levelFound) return null;
    int edgeDirection = FindEdgeDirection(node, edge);

    if (IsClassDeclarationEdge(edge))
    {
      edge.displaySubject.hierarchicalSettings.hierarchicalType = Hierarchical.HierarchicalType.SubClassOf;
      return null;
    }

    if (IsSubClassOfEdge(edge))
    {
      SetSubClassOfSettings(node, edgeDirection, partnerNode);
    }
    else if (IsTypeEdge(edge))
    {
      if (Utils.IsSubjectNode(node, edge))
      {
        return null;
      }
      SetTypeSettings(node, edgeDirection, partnerNode);
    }
    else
    {
      if (IsKnownClassNode(partnerNode))
      {
        return null;
      }
      SetOtherSettings(node, partnerNode);
    }

    partnerNode.hierarchicalSettings.SetLevel(node.hierarchicalSettings.level + edgeDirection);
    return partnerNode;
  }

  private void ShiftHierarchyLevels(int lowestLevel)
  {
    foreach (Node currentNode in graph.nodeList)
    {
      currentNode.hierarchicalSettings.level -= lowestLevel;
    }
  }

  private int GetLowestLevel()
  {
    int lowestLevel = int.MaxValue;
    foreach (Node currentNode in graph.nodeList)
    {
      if (currentNode.hierarchicalSettings.level < lowestLevel) lowestLevel = currentNode.hierarchicalSettings.level;
    }
    return lowestLevel;
  }

  private static void SetOtherSettings(Node node, Node partnerNode)
  {
    partnerNode.hierarchicalSettings.hierarchicalType = Hierarchical.HierarchicalType.Other;
    partnerNode.hierarchicalSettings.parent = node;
    node.hierarchicalSettings.typeWithChildNodes = true;
    partnerNode.hierarchicalSettings.otherCount = 0;
  }

  private static void SetTypeSettings(Node node, int edgeDirection, Node partnerNode)
  {
    if (edgeDirection == 1)
    {
      partnerNode.hierarchicalSettings.hierarchicalType = Hierarchical.HierarchicalType.Type;
      partnerNode.hierarchicalSettings.parent = node;
    }
    else
    {
      partnerNode.hierarchicalSettings.hierarchicalType = Hierarchical.HierarchicalType.Other;
    }
    partnerNode.hierarchicalSettings.typeCount = 0;
    partnerNode.hierarchicalSettings.otherCount = 0;
  }

  private static void SetSubClassOfSettings(Node node, int edgeDirection, Node partnerNode)
  {
    partnerNode.hierarchicalSettings.hierarchicalType = Hierarchical.HierarchicalType.SubClassOf;
    if (edgeDirection == 1)
    {
      partnerNode.hierarchicalSettings.parent = node;
    }
    partnerNode.hierarchicalSettings.typeCount = 0;
    partnerNode.hierarchicalSettings.otherCount = 0;
  }

  private int FindEdgeDirection(Node node, Edge edge)
  {
    return (Utils.IsSubjectNode(node, edge) && IsSubClassOfEdge(edge)) ? -1 : 1;
  }

  private void MarkKnownClassNodes()
  {
    foreach (Node node in graph.nodeList)
    {
      if (IsKnownClassNode(node))
      {
        node.hierarchicalSettings.hierarchicalType = Hierarchical.HierarchicalType.SubClassOf;
      }
    }
  }

  private Node FindInitialClassRoot(List<Edge> subClassOfEdgeList)
  {
    Node owlThing = graph.nodeList.Find(node => NodeUri(node) == OWL_THING);
    if (owlThing != null) return owlThing;

    List<Node> classRoots = new();
    foreach (Edge edge in subClassOfEdgeList)
    {
      bool hasSuperClass = subClassOfEdgeList.Exists(otherEdge => otherEdge.displaySubject == edge.displayObject);
      if (!hasSuperClass && !classRoots.Contains(edge.displayObject))
      {
        classRoots.Add(edge.displayObject);
      }
    }

    classRoots.Sort((Node a, Node b) => string.Compare(a.textMesh.text, b.textMesh.text));
    return classRoots.Count > 0 ? classRoots[0] : subClassOfEdgeList[0].displayObject;
  }

  private int GetEdgePriority(Edge edge)
  {
    if (IsSubClassOfEdge(edge)) return 0;
    if (IsTypeEdge(edge)) return 1;
    return 2;
  }

  private bool IsKnownClassNode(Node node)
  {
    string uri = NodeUri(node);
    if (uri == OWL_THING || uri == OWL_CLASS || uri == RDFS_CLASS) return true;

    foreach (Edge edge in graph.edgeList)
    {
      if (IsSubClassOfEdge(edge) && (edge.displaySubject == node || edge.displayObject == node)) return true;
      if (IsClassDeclarationEdge(edge) && edge.displaySubject == node) return true;
    }
    return false;
  }

  private bool IsClassDeclarationEdge(Edge edge)
  {
    return IsTypeEdge(edge) && (NodeUri(edge.displayObject) == OWL_CLASS || NodeUri(edge.displayObject) == RDFS_CLASS);
  }

  private static bool IsSubClassOfEdge(Edge edge)
  {
    return edge.uri.ToLower() == SUBCLASS_OF_PREDICATE;
  }

  private static bool IsTypeEdge(Edge edge)
  {
    return edge.uri.ToLower() == TYPE_PREDICATE;
  }

  private static string NodeUri(Node node)
  {
    if (node == null || node.uri == null) return "";
    return node.uri.ToLower();
  }

  public float PositionNodeLevels(Node node, int level, float subClassOfOffset)
  {
    float newSubClassOfOffset = subClassOfOffset;
    if (notAClassHiearchy)
    {
      SetNodePosition(node, level, subClassOfOffset, 0, notAClassHiearchyPositionCounter++);
    }
    else if (!node.LockPosition)
    {
      Node parentNode = node.hierarchicalSettings.parent;
      if (node.hierarchicalSettings.hierarchicalType == Hierarchical.HierarchicalType.Type && parentNode != null)
      {
        SetTypePositionSettings(node, level, parentNode);

      }
      else if (node.hierarchicalSettings.hierarchicalType == Hierarchical.HierarchicalType.Other && parentNode != null)
      {
        SetOtherPositionSettings(node, level, parentNode);
      }
      else
      {
        newSubClassOfOffset = SetSubClassOffPositionSettings(node, level, subClassOfOffset, newSubClassOfOffset);
      }
      node.hierarchicalSettings.subClassOfOffset = subClassOfOffset;
      node.hierarchicalSettings.positionSet = true;
    }

    int nextLevel = level + 1;
    foreach (Edge edge in node.connections)
    {
      Node childNode = Utils.GetPartnerNode(node, edge);
      if (NodeOfLevelNeedsUpdate(childNode, nextLevel))
      {
        newSubClassOfOffset = PositionNodeLevels(childNode, nextLevel, newSubClassOfOffset);
      }
    }
    return newSubClassOfOffset;
  }

  private float SetSubClassOffPositionSettings(Node node, int level, float subClassOfOffset, float newSubClassOfOffset)
  {
    SetNodePosition(node, level, subClassOfOffset, 0, 0);
    if (node.hierarchicalSettings.hierarchicalType == Hierarchical.HierarchicalType.SubClassOf)
    {
      newSubClassOfOffset += offsetSize;
    }
    return newSubClassOfOffset;
  }

  private void SetOtherPositionSettings(Node node, int level, Node parentNode)
  {
    int typeDepth;
    Node typeParentNode = FindFirstNonOtherParent(node.hierarchicalSettings.parent);
    if (typeParentNode.hierarchicalSettings.parent != null)
    {
      typeDepth = typeParentNode.hierarchicalSettings.parent.hierarchicalSettings.typeCount;
    }
    else
    {
      typeDepth = typeParentNode.hierarchicalSettings.typeCount;
    }
    node.hierarchicalSettings.Test = typeParentNode;
    int otherDepth = ++typeParentNode.hierarchicalSettings.otherCount;
    float subClassOfOffset = typeParentNode.hierarchicalSettings.subClassOfOffset - offsetSize;
    SetNodePosition(node, typeParentNode.hierarchicalSettings.level + 1, subClassOfOffset, typeDepth, otherDepth);
  }

  private static Node FindFirstNonOtherParent(Node node)
  {
    Node previousNode = node;
    for (int i = 0; i < 10; i++)
    {
      if (previousNode.hierarchicalSettings.hierarchicalType != Hierarchical.HierarchicalType.Other || previousNode == previousNode.hierarchicalSettings.parent)
      {
        break;
      }
      previousNode = previousNode.hierarchicalSettings.parent;
    }
    return previousNode;
  }

  private void SetTypePositionSettings(Node node, int level, Node parentNode)
  {
    int typeDepth = ++parentNode.hierarchicalSettings.typeCount;
    float subClassOfOffset = parentNode.hierarchicalSettings.subClassOfOffset;

    // Reserve space for child nodes
    if (node.hierarchicalSettings.typeWithChildNodes)
    {
      parentNode.hierarchicalSettings.typeCount++;
    }
    SetNodePosition(node, level, subClassOfOffset, typeDepth, 0);
  }

  private void SetNodePosition(Node node, int level, float subClassOfOffset, int typeDepth, int otherDepth)
  {
    node.hierarchicalSettings.targetLocation =
      new Vector3(0, typeDepth * offsetSize, subClassOfOffset) + new Vector3((level * (offsetSize)) + (otherDepth * offsetSize), 0, 0);
  }

  private static bool NodeOfLevelNeedsUpdate(Node node, int level)
  {
    return node.hierarchicalSettings.level == level && !node.hierarchicalSettings.positionSet;
  }

  public override void Stop()
  {
    running = false;
  }
}
