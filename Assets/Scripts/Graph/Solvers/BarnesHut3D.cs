using System.Collections.Generic;
using UnityEngine;

public class BarnesHut3D : BaseLayoutAlgorithm
{
  public float Temperature = 0;
  public float theta = 0.7f;
  public float repulsionStrength = 0.0008f;
  public float attractionStrength = 0.001f;
  public float desiredEdgeLength = 0.35f;
  public float centerForce = 0f;
  public float coolingRate = 0.005f;
  public float minimumTemperature = 0.01f;
  public float softening = 0.05f;

  private readonly Dictionary<Node, Vector3> displacements = new();

  public override void CalculateLayout()
  {
    Temperature = 0.05f;
  }

  public override void Stop()
  {
    Temperature = 0f;
  }

  private void Update()
  {
    if (Temperature > minimumTemperature)
    {
      BarnesHutIteration();
    }
  }

  private void BarnesHutIteration()
  {
    if (graph.nodeList.Count == 0)
    {
      Stop();
      return;
    }

    Octree tree = new(GetBounds(graph.nodeList));
    foreach (Node node in graph.nodeList)
    {
      tree.Insert(node);
      displacements[node] = Vector3.zero;
    }

    foreach (Node node in graph.nodeList)
    {
      Vector3 displacement = tree.CalculateRepulsion(node, theta, repulsionStrength, softening);
      displacement += -node.transform.localPosition * centerForce * Time.deltaTime;
      displacements[node] = displacement;
    }

    foreach (Edge edge in graph.edgeList)
    {
      if (edge.displayObject == null || edge.displaySubject == null) continue;

      Vector3 delta = edge.displayObject.transform.localPosition - edge.displaySubject.transform.localPosition;
      float distance = Mathf.Max(delta.magnitude, softening);
      Vector3 force = delta.normalized * ((distance - desiredEdgeLength) * attractionStrength);

      displacements[edge.displayObject] -= force;
      displacements[edge.displaySubject] += force;
    }

    foreach (Node node in graph.nodeList)
    {
      if (node == null || node.LockPosition) continue;

      Vector3 displacement = displacements[node];
      float displacementMagnitude = displacement.magnitude;
      if (displacementMagnitude <= 0f) continue;

      Vector3 movement = (displacement / displacementMagnitude) * Mathf.Min(displacementMagnitude, Temperature);
      if (!float.IsNaN(movement.x) && !float.IsNaN(movement.y) && !float.IsNaN(movement.z))
      {
        node.transform.localPosition += movement;
      }
    }

    Temperature -= coolingRate * Time.deltaTime;
  }

  private Bounds GetBounds(List<Node> nodes)
  {
    Bounds bounds = new(nodes[0].transform.localPosition, Vector3.one * 0.1f);
    foreach (Node node in nodes)
    {
      bounds.Encapsulate(node.transform.localPosition);
    }

    float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, softening) * 1.1f;
    bounds.size = Vector3.one * size;
    return bounds;
  }

  private class Octree
  {
    private const int MaxDepth = 12;

    private readonly Bounds bounds;
    private readonly int depth;
    private readonly Octree[] children = new Octree[8];
    private Node node;
    private Vector3 centerOfMass;
    private int mass;

    public Octree(Bounds bounds, int depth = 0)
    {
      this.bounds = bounds;
      this.depth = depth;
    }

    public void Insert(Node nodeToInsert)
    {
      UpdateMass(nodeToInsert.transform.localPosition);

      if (node == null && IsLeaf())
      {
        node = nodeToInsert;
        return;
      }

      if (IsLeaf() && CanSplit())
      {
        Node existingNode = node;
        node = null;
        InsertIntoChild(existingNode);
      }

      if (CanSplit())
      {
        InsertIntoChild(nodeToInsert);
      }
    }

    public Vector3 CalculateRepulsion(Node target, float theta, float strength, float softening)
    {
      if (mass == 0 || (mass == 1 && node == target)) return Vector3.zero;

      Vector3 delta = target.transform.localPosition - centerOfMass;
      float distance = Mathf.Max(delta.magnitude, softening);
      float width = bounds.size.x;

      if (IsLeaf() || width / distance < theta)
      {
        return delta.normalized * ((strength * mass) / (distance * distance));
      }

      Vector3 force = Vector3.zero;
      foreach (Octree child in children)
      {
        if (child != null)
        {
          force += child.CalculateRepulsion(target, theta, strength, softening);
        }
      }
      return force;
    }

    private void UpdateMass(Vector3 position)
    {
      centerOfMass = ((centerOfMass * mass) + position) / (mass + 1);
      mass++;
    }

    private bool IsLeaf()
    {
      for (int i = 0; i < children.Length; i++)
      {
        if (children[i] != null) return false;
      }
      return true;
    }

    private bool CanSplit()
    {
      return depth < MaxDepth && bounds.size.x > 0.001f;
    }

    private void InsertIntoChild(Node nodeToInsert)
    {
      int index = GetChildIndex(nodeToInsert.transform.localPosition);
      if (children[index] == null)
      {
        children[index] = new Octree(GetChildBounds(index), depth + 1);
      }
      children[index].Insert(nodeToInsert);
    }

    private int GetChildIndex(Vector3 position)
    {
      int index = 0;
      if (position.x >= bounds.center.x) index |= 1;
      if (position.y >= bounds.center.y) index |= 2;
      if (position.z >= bounds.center.z) index |= 4;
      return index;
    }

    private Bounds GetChildBounds(int index)
    {
      Vector3 childSize = bounds.size * 0.5f;
      Vector3 offset = new(
        (index & 1) == 0 ? -childSize.x * 0.5f : childSize.x * 0.5f,
        (index & 2) == 0 ? -childSize.y * 0.5f : childSize.y * 0.5f,
        (index & 4) == 0 ? -childSize.z * 0.5f : childSize.z * 0.5f
      );
      return new Bounds(bounds.center + offset, childSize);
    }
  }
}
