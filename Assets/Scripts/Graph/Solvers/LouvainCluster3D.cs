using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LouvainCluster3D : BaseLayoutAlgorithm
{
  public float Temperature = 0f;
  public int communityPasses = 8;
  public float clusterSpacing = 1.2f;
  public float nodeSpacing = 0.28f;
  public float moveSpeed = 4f;
  public float coolingRate = 0.01f;
  public float minimumTemperature = 0.01f;
  public float resolution = 1f;

  private readonly Dictionary<Node, int> communities = new();
  private readonly Dictionary<Node, Vector3> targets = new();
  private readonly Dictionary<Node, Dictionary<Node, float>> adjacency = new();
  private readonly Dictionary<Node, float> nodeDegrees = new();
  private readonly Dictionary<int, float> communityDegrees = new();

  public override void CalculateLayout()
  {
    if (graph.nodeList.Count == 0)
    {
      Stop();
      return;
    }

    BuildCommunities();
    CalculateTargetPositions();
    Temperature = 1f;
  }

  public override void Stop()
  {
    Temperature = 0f;
  }

  private void Update()
  {
    if (Temperature <= minimumTemperature) return;

    foreach (Node node in graph.nodeList)
    {
      if (node == null || node.LockPosition || !targets.ContainsKey(node)) continue;

      Vector3 position = node.transform.localPosition;
      Vector3 target = targets[node];
      Vector3 movement = (target - position) * Mathf.Min(1f, moveSpeed * Time.deltaTime * Temperature);

      if (!float.IsNaN(movement.x) && !float.IsNaN(movement.y) && !float.IsNaN(movement.z))
      {
        node.transform.localPosition += movement;
      }
    }

    Temperature -= coolingRate * Time.deltaTime;
  }

  private void BuildCommunities()
  {
    communities.Clear();
    adjacency.Clear();
    nodeDegrees.Clear();
    communityDegrees.Clear();

    foreach (Node node in graph.nodeList)
    {
      communities[node] = communities.Count;
      adjacency[node] = new Dictionary<Node, float>();
      nodeDegrees[node] = 0f;
    }

    foreach (Edge edge in graph.edgeList)
    {
      if (edge.displaySubject == null || edge.displayObject == null) continue;
      if (edge.displaySubject == edge.displayObject) continue;
      if (!adjacency.ContainsKey(edge.displaySubject) || !adjacency.ContainsKey(edge.displayObject)) continue;

      AddUndirectedWeight(edge.displaySubject, edge.displayObject, 1f);
    }

    RecalculateCommunityDegrees();

    for (int pass = 0; pass < communityPasses; pass++)
    {
      bool moved = false;
      foreach (Node node in graph.nodeList.OrderBy(NodeKey))
      {
        int currentCommunity = communities[node];
        int bestCommunity = currentCommunity;
        float bestScore = CommunityScore(node, currentCommunity);

        foreach (int candidateCommunity in GetNeighborCommunities(node))
        {
          float score = CommunityScore(node, candidateCommunity);
          if (score > bestScore + 0.0001f)
          {
            bestScore = score;
            bestCommunity = candidateCommunity;
          }
        }

        if (bestCommunity != currentCommunity)
        {
          float degree = nodeDegrees[node];
          communityDegrees[currentCommunity] -= degree;
          communityDegrees[bestCommunity] = GetCommunityDegree(bestCommunity) + degree;
          communities[node] = bestCommunity;
          moved = true;
        }
      }

      if (!moved) break;
    }

    NormalizeCommunityIds();
  }

  private void AddUndirectedWeight(Node first, Node second, float weight)
  {
    adjacency[first][second] = GetNeighborWeight(first, second) + weight;
    adjacency[second][first] = GetNeighborWeight(second, first) + weight;
    nodeDegrees[first] += weight;
    nodeDegrees[second] += weight;
  }

  private void RecalculateCommunityDegrees()
  {
    communityDegrees.Clear();
    foreach (KeyValuePair<Node, int> item in communities)
    {
      communityDegrees[item.Value] = GetCommunityDegree(item.Value) + nodeDegrees[item.Key];
    }
  }

  private IEnumerable<int> GetNeighborCommunities(Node node)
  {
    HashSet<int> candidateCommunities = new() { communities[node] };
    foreach (Node neighbor in adjacency[node].Keys)
    {
      candidateCommunities.Add(communities[neighbor]);
    }
    return candidateCommunities;
  }

  private float CommunityScore(Node node, int community)
  {
    float linksToCommunity = 0f;
    foreach (KeyValuePair<Node, float> neighbor in adjacency[node])
    {
      if (communities[neighbor.Key] == community)
      {
        linksToCommunity += neighbor.Value;
      }
    }

    float totalWeight = Mathf.Max(1f, nodeDegrees.Values.Sum());
    float communityDegree = GetCommunityDegree(community);
    return linksToCommunity - resolution * nodeDegrees[node] * communityDegree / totalWeight;
  }

  private float GetNeighborWeight(Node node, Node neighbor)
  {
    return adjacency[node].TryGetValue(neighbor, out float weight) ? weight : 0f;
  }

  private float GetCommunityDegree(int community)
  {
    return communityDegrees.TryGetValue(community, out float degree) ? degree : 0f;
  }

  private void NormalizeCommunityIds()
  {
    Dictionary<int, int> normalizedIds = new();
    foreach (Node node in graph.nodeList.OrderBy(NodeKey))
    {
      int community = communities[node];
      if (!normalizedIds.ContainsKey(community))
      {
        normalizedIds[community] = normalizedIds.Count;
      }
      communities[node] = normalizedIds[community];
    }

    RecalculateCommunityDegrees();
  }

  private void CalculateTargetPositions()
  {
    targets.Clear();
    List<IGrouping<int, Node>> groupedCommunities = graph.nodeList
      .GroupBy(node => communities[node])
      .OrderByDescending(group => group.Count())
      .ThenBy(group => group.Key)
      .ToList();

    float clusterRadius = Mathf.Max(clusterSpacing, Mathf.Sqrt(groupedCommunities.Count) * clusterSpacing * 0.45f);

    for (int clusterIndex = 0; clusterIndex < groupedCommunities.Count; clusterIndex++)
    {
      List<Node> nodes = groupedCommunities[clusterIndex].OrderBy(NodeKey).ToList();
      Vector3 clusterCenter = groupedCommunities.Count == 1
        ? Vector3.zero
        : FibonacciSpherePoint(clusterIndex, groupedCommunities.Count, clusterRadius);

      float localRadius = Mathf.Max(nodeSpacing, Mathf.Pow(nodes.Count, 1f / 3f) * nodeSpacing);
      for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
      {
        Vector3 localOffset = nodes.Count == 1
          ? Vector3.zero
          : FibonacciSpherePoint(nodeIndex, nodes.Count, localRadius);
        targets[nodes[nodeIndex]] = clusterCenter + localOffset;
      }
    }
  }

  private Vector3 FibonacciSpherePoint(int index, int count, float radius)
  {
    if (count <= 1) return Vector3.zero;

    float offset = 2f / count;
    float increment = Mathf.PI * (3f - Mathf.Sqrt(5f));
    float y = ((index * offset) - 1f) + (offset * 0.5f);
    float ringRadius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
    float phi = index * increment;

    return new Vector3(
      Mathf.Cos(phi) * ringRadius,
      y,
      Mathf.Sin(phi) * ringRadius
    ) * radius;
  }

  private string NodeKey(Node node)
  {
    return node == null ? "" : node.uri;
  }
}
