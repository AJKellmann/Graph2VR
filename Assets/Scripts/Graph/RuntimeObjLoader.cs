using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

public static class RuntimeObjLoader
{
  public class ObjMaterialDefinition
  {
    public string name = "";
    public Color color = Color.white;
    public string textureUri = "";
  }

  private class ObjParseResult
  {
    public Mesh mesh;
    public List<string> materialNames = new();
  }

  private struct ObjVertexKey
  {
    public int vertex;
    public int texture;
    public int normal;

    public ObjVertexKey(int vertex, int texture, int normal)
    {
      this.vertex = vertex;
      this.texture = texture;
      this.normal = normal;
    }
  }

  public static bool CanLoad(string uri)
  {
    string path = uri.Split('?')[0].Split('#')[0];
    return path.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);
  }

  public static List<string> GetMaterialLibraryUris(string objText, string objUri)
  {
    List<string> materialUris = new();
    string[] lines = objText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

    foreach (string rawLine in lines)
    {
      string line = rawLine.Trim();
      if (!line.StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase)) continue;

      string libraries = line.Substring("mtllib ".Length).Trim();
      foreach (string library in SplitMaterialLibraries(libraries))
      {
        materialUris.Add(ResolveUri(objUri, library));
      }
    }

    return materialUris;
  }

  public static Dictionary<string, ObjMaterialDefinition> ParseMaterialLibrary(string mtlText, string mtlUri)
  {
    Dictionary<string, ObjMaterialDefinition> materials = new();
    ObjMaterialDefinition current = null;
    string[] lines = mtlText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

    foreach (string rawLine in lines)
    {
      string line = rawLine.Trim();
      if (line.Length == 0 || line.StartsWith("#")) continue;

      string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 0) continue;

      switch (parts[0])
      {
        case "newmtl":
          current = new ObjMaterialDefinition { name = line.Substring("newmtl".Length).Trim() };
          materials[current.name] = current;
          break;
        case "Kd":
          if (current != null && parts.Length >= 4)
          {
            current.color = new Color(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]), current.color.a);
          }
          break;
        case "d":
          if (current != null && parts.Length >= 2)
          {
            current.color.a = Mathf.Clamp01(ParseFloat(parts[1]));
          }
          break;
        case "Tr":
          if (current != null && parts.Length >= 2)
          {
            current.color.a = 1f - Mathf.Clamp01(ParseFloat(parts[1]));
          }
          break;
        case "map_Kd":
          if (current != null && parts.Length >= 2)
          {
            current.textureUri = ResolveUri(mtlUri, TexturePathFromMapLine(parts));
          }
          break;
      }
    }

    return materials;
  }

  public static bool TryCreateGameObject(string objText, string name, Material fallbackMaterial, Dictionary<string, Material> materials, out GameObject obj)
  {
    obj = null;
    ObjParseResult result;
    try
    {
      result = Parse(objText, name);
    }
    catch (Exception exception)
    {
      Debug.LogWarning($"OBJ parse failed: {exception.Message}");
      return false;
    }

    if (result == null || result.mesh == null)
    {
      return false;
    }

    obj = new GameObject(name);
    MeshFilter meshFilter = obj.AddComponent<MeshFilter>();
    MeshRenderer meshRenderer = obj.AddComponent<MeshRenderer>();
    meshFilter.sharedMesh = result.mesh;

    Material[] sharedMaterials = new Material[Mathf.Max(1, result.mesh.subMeshCount)];
    for (int i = 0; i < sharedMaterials.Length; i++)
    {
      string materialName = i < result.materialNames.Count ? result.materialNames[i] : "";
      if (materials != null && materials.TryGetValue(materialName, out Material material))
      {
        sharedMaterials[i] = material;
      }
      else
      {
        sharedMaterials[i] = fallbackMaterial;
      }
    }
    meshRenderer.sharedMaterials = sharedMaterials;
    return true;
  }

  private static ObjParseResult Parse(string objText, string name)
  {
    List<Vector3> sourceVertices = new();
    List<Vector2> sourceUvs = new();
    List<Vector3> sourceNormals = new();
    List<Vector3> vertices = new();
    List<Vector2> uvs = new();
    List<Vector3> normals = new();
    Dictionary<ObjVertexKey, int> vertexLookup = new();
    Dictionary<string, List<int>> trianglesByMaterial = new();
    List<string> materialOrder = new();
    string currentMaterial = "";

    string[] lines = objText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (string rawLine in lines)
    {
      string line = rawLine.Trim();
      if (line.Length == 0 || line.StartsWith("#")) continue;

      string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 0) continue;

      switch (parts[0])
      {
        case "v":
          if (parts.Length >= 4)
          {
            sourceVertices.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
          }
          break;
        case "vt":
          if (parts.Length >= 3)
          {
            sourceUvs.Add(new Vector2(ParseFloat(parts[1]), ParseFloat(parts[2])));
          }
          break;
        case "vn":
          if (parts.Length >= 4)
          {
            sourceNormals.Add(new Vector3(ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3])));
          }
          break;
        case "usemtl":
          currentMaterial = line.Substring("usemtl".Length).Trim();
          EnsureMaterialSlot(currentMaterial, trianglesByMaterial, materialOrder);
          break;
        case "f":
          AddFace(parts, currentMaterial, sourceVertices, sourceUvs, sourceNormals, vertices, uvs, normals, trianglesByMaterial, materialOrder, vertexLookup);
          break;
      }
    }

    if (vertices.Count == 0 || materialOrder.Count == 0)
    {
      return null;
    }

    Mesh mesh = new Mesh();
    mesh.name = name;
    if (vertices.Count > 65535)
    {
      mesh.indexFormat = IndexFormat.UInt32;
    }
    mesh.SetVertices(vertices);
    mesh.SetUVs(0, uvs);

    List<string> usedMaterialNames = new();
    foreach (string materialName in materialOrder)
    {
      if (trianglesByMaterial[materialName].Count > 0)
      {
        usedMaterialNames.Add(materialName);
      }
    }

    mesh.subMeshCount = usedMaterialNames.Count;
    for (int i = 0; i < usedMaterialNames.Count; i++)
    {
      mesh.SetTriangles(trianglesByMaterial[usedMaterialNames[i]], i);
    }

    if (HasUsableNormals(normals))
    {
      mesh.SetNormals(normals);
    }
    else
    {
      mesh.RecalculateNormals();
    }
    mesh.RecalculateBounds();

    return new ObjParseResult
    {
      mesh = mesh,
      materialNames = usedMaterialNames
    };
  }

  private static void AddFace(
    string[] parts,
    string materialName,
    List<Vector3> sourceVertices,
    List<Vector2> sourceUvs,
    List<Vector3> sourceNormals,
    List<Vector3> vertices,
    List<Vector2> uvs,
    List<Vector3> normals,
    Dictionary<string, List<int>> trianglesByMaterial,
    List<string> materialOrder,
    Dictionary<ObjVertexKey, int> vertexLookup)
  {
    if (parts.Length < 4) return;

    List<int> triangles = EnsureMaterialSlot(materialName, trianglesByMaterial, materialOrder);
    List<int> faceIndices = new();
    for (int i = 1; i < parts.Length; i++)
    {
      faceIndices.Add(GetOrCreateVertex(parts[i], sourceVertices, sourceUvs, sourceNormals, vertices, uvs, normals, vertexLookup));
    }

    for (int i = 1; i < faceIndices.Count - 1; i++)
    {
      triangles.Add(faceIndices[0]);
      triangles.Add(faceIndices[i]);
      triangles.Add(faceIndices[i + 1]);
    }
  }

  private static List<int> EnsureMaterialSlot(string materialName, Dictionary<string, List<int>> trianglesByMaterial, List<string> materialOrder)
  {
    if (!trianglesByMaterial.TryGetValue(materialName, out List<int> triangles))
    {
      triangles = new List<int>();
      trianglesByMaterial[materialName] = triangles;
      materialOrder.Add(materialName);
    }
    return triangles;
  }

  private static int GetOrCreateVertex(
    string token,
    List<Vector3> sourceVertices,
    List<Vector2> sourceUvs,
    List<Vector3> sourceNormals,
    List<Vector3> vertices,
    List<Vector2> uvs,
    List<Vector3> normals,
    Dictionary<ObjVertexKey, int> vertexLookup)
  {
    string[] parts = token.Split('/');
    int vertexIndex = ResolveIndex(parts, 0, sourceVertices.Count);
    if (vertexIndex < 0)
    {
      throw new FormatException($"Face token has no valid vertex index: {token}");
    }

    int uvIndex = ResolveIndex(parts, 1, sourceUvs.Count);
    int normalIndex = ResolveIndex(parts, 2, sourceNormals.Count);
    ObjVertexKey key = new ObjVertexKey(vertexIndex, uvIndex, normalIndex);

    if (vertexLookup.TryGetValue(key, out int existingIndex))
    {
      return existingIndex;
    }

    int newIndex = vertices.Count;
    vertexLookup[key] = newIndex;
    vertices.Add(sourceVertices[vertexIndex]);
    uvs.Add(uvIndex >= 0 ? sourceUvs[uvIndex] : Vector2.zero);
    normals.Add(normalIndex >= 0 ? sourceNormals[normalIndex] : Vector3.zero);
    return newIndex;
  }

  private static int ResolveIndex(string[] parts, int partIndex, int count)
  {
    if (count <= 0)
    {
      return -1;
    }

    if (partIndex >= parts.Length || string.IsNullOrEmpty(parts[partIndex]))
    {
      return -1;
    }

    int rawIndex = int.Parse(parts[partIndex], CultureInfo.InvariantCulture);
    int resolvedIndex = rawIndex > 0 ? rawIndex - 1 : count + rawIndex;
    return Mathf.Clamp(resolvedIndex, 0, count - 1);
  }

  private static List<string> SplitMaterialLibraries(string libraries)
  {
    List<string> result = new();
    int start = 0;

    while (start < libraries.Length)
    {
      while (start < libraries.Length && char.IsWhiteSpace(libraries[start]))
      {
        start++;
      }

      if (start >= libraries.Length)
      {
        break;
      }

      int end = libraries.IndexOf(".mtl", start, StringComparison.OrdinalIgnoreCase);
      if (end < 0)
      {
        result.Add(libraries.Substring(start).Trim());
        break;
      }

      end += ".mtl".Length;
      string library = libraries.Substring(start, end - start).Trim();
      if (!string.IsNullOrEmpty(library))
      {
        result.Add(library);
      }
      start = end;
    }

    return result;
  }

  private static string ResolveUri(string baseUri, string relativeUri)
  {
    if (Uri.TryCreate(relativeUri, UriKind.Absolute, out Uri absoluteUri))
    {
      return absoluteUri.ToString();
    }

    if (Uri.TryCreate(baseUri, UriKind.Absolute, out Uri baseAbsoluteUri))
    {
      return new Uri(baseAbsoluteUri, relativeUri).ToString();
    }

    return relativeUri;
  }

  private static string TexturePathFromMapLine(string[] parts)
  {
    return parts[parts.Length - 1];
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
