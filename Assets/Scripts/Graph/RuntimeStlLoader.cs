using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public static class RuntimeStlLoader
{
  public static bool CanLoad(string uri)
  {
    string path = uri.Split('?')[0].Split('#')[0];
    return path.EndsWith(".stl", StringComparison.OrdinalIgnoreCase);
  }

  public static bool TryCreateGameObject(byte[] stlBytes, string name, Material material, out GameObject obj)
  {
    obj = null;
    Mesh mesh;
    try
    {
      mesh = LooksLikeAscii(stlBytes) ? ParseAscii(Encoding.UTF8.GetString(stlBytes), name) : ParseBinary(stlBytes, name);
    }
    catch (Exception exception)
    {
      Debug.LogWarning($"STL parse failed: {exception.Message}");
      return false;
    }

    if (mesh == null)
    {
      return false;
    }

    obj = new GameObject(name);
    MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
    MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
    meshFilter.sharedMesh = mesh;
    meshRenderer.sharedMaterial = material;
    return true;
  }

  private static bool LooksLikeAscii(byte[] bytes)
  {
    if (bytes.Length < 84) return true;

    string header = Encoding.ASCII.GetString(bytes, 0, Mathf.Min(bytes.Length, 80)).TrimStart();
    if (!header.StartsWith("solid", StringComparison.OrdinalIgnoreCase)) return false;

    string sample = Encoding.ASCII.GetString(bytes, 0, Mathf.Min(bytes.Length, 512));
    return sample.Contains("facet") && sample.Contains("vertex");
  }

  private static Mesh ParseAscii(string stlText, string name)
  {
    List<Vector3> vertices = new();
    List<Vector3> normals = new();
    List<int> triangles = new();
    Vector3 currentNormal = Vector3.zero;

    string[] lines = stlText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (string rawLine in lines)
    {
      string line = rawLine.Trim();
      string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 0) continue;

      if (parts[0] == "facet" && parts.Length >= 5)
      {
        currentNormal = new Vector3(ParseFloat(parts[2]), ParseFloat(parts[3]), ParseFloat(parts[4]));
      }
      else if (parts[0] == "vertex" && parts.Length >= 4)
      {
        vertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
        normals.Add(currentNormal);
        triangles.Add(vertices.Count - 1);
      }
    }

    return BuildMesh(name, vertices, normals, triangles);
  }

  private static Mesh ParseBinary(byte[] stlBytes, string name)
  {
    if (stlBytes.Length < 84) return null;

    uint triangleCount = BitConverter.ToUInt32(stlBytes, 80);
    int expectedLength = 84 + (int)triangleCount * 50;
    if (stlBytes.Length < expectedLength)
    {
      throw new FormatException("Binary STL is shorter than its triangle count declares.");
    }

    List<Vector3> vertices = new();
    List<Vector3> normals = new();
    List<int> triangles = new();
    int offset = 84;

    for (int i = 0; i < triangleCount; i++)
    {
      Vector3 normal = ReadVector(stlBytes, offset);
      offset += 12;

      for (int vertex = 0; vertex < 3; vertex++)
      {
        vertices.Add(ReadVector(stlBytes, offset));
        normals.Add(normal);
        triangles.Add(vertices.Count - 1);
        offset += 12;
      }

      offset += 2;
    }

    return BuildMesh(name, vertices, normals, triangles);
  }

  private static Mesh BuildMesh(string name, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
  {
    if (vertices.Count == 0 || triangles.Count == 0) return null;

    Mesh mesh = new Mesh();
    mesh.name = name;
    if (vertices.Count > 65535)
    {
      mesh.indexFormat = IndexFormat.UInt32;
    }
    mesh.SetVertices(vertices);
    mesh.SetTriangles(triangles, 0);

    if (HasUsableNormals(normals))
    {
      mesh.SetNormals(normals);
    }
    else
    {
      mesh.RecalculateNormals();
    }
    mesh.RecalculateBounds();
    return mesh;
  }

  private static Vector3 ReadVector(byte[] bytes, int offset)
  {
    return new Vector3(
      BitConverter.ToSingle(bytes, offset),
      BitConverter.ToSingle(bytes, offset + 4),
      BitConverter.ToSingle(bytes, offset + 8)
    );
  }

  private static float ParseFloat(string value)
  {
    return float.Parse(value, CultureInfo.InvariantCulture);
  }

  private static bool HasUsableNormals(List<Vector3> normals)
  {
    foreach (Vector3 normal in normals)
    {
      if (normal == Vector3.zero)
      {
        return false;
      }
    }
    return normals.Count > 0;
  }
}
