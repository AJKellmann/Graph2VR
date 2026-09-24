using System;
using System.Collections.Generic;
using UnityEngine;

public enum PdbRepresentation { Bonds = 0, Atoms = 1, Residues = 2, Chains = 3 }

public static class RuntimePdbLoader
{
  public static bool CanLoad(string uri)
  {
    if (string.IsNullOrEmpty(uri)) return false;
    return uri.Split('?')[0].Split('#')[0].EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);
  }

  public static bool TryCreateGameObject(string text, string name, Material fallback, out GameObject model,
    PdbRepresentation representation = PdbRepresentation.Bonds)
  {
    model = null;
    try { return TryCreateGameObject(PdbStructure.Parse(text), name, fallback, representation, out model); }
    catch (Exception exception) { Debug.LogWarning("PDB parse failed: " + exception.Message); return false; }
  }

  public static bool TryCreateGameObject(PdbStructure structure, string name, Material fallback,
    PdbRepresentation representation, out GameObject model)
  {
    model = null;
    try
    {
      model = new GameObject(name);
      var resources = model.AddComponent<RuntimePdbResources>();
      resources.Structure = structure;
      var batches = new Dictionary<string, Batch>();
      Vector3 center = Vector3.zero;
      foreach (var atom in structure.Atoms) center += new Vector3(atom.X, atom.Y, atom.Z);
      center /= structure.Atoms.Count;
      var positions = new Vector3[structure.Atoms.Count];
      for (int i = 0; i < positions.Length; i++)
      {
        var atom = structure.Atoms[i];
        // Negate Z to preserve the molecular handedness in Unity's coordinate convention.
        Vector3 p = new Vector3(atom.X - center.x, atom.Y - center.y, -(atom.Z - center.z));
        positions[i] = p;
        if (representation == PdbRepresentation.Atoms || representation == PdbRepresentation.Bonds)
          GetBatch(atom.Element, batches, model, fallback, resources).Sphere(p, Radius(atom.Element));
      }
      if (representation == PdbRepresentation.Bonds)
      foreach (var bond in structure.Bonds)
      {
        Vector3 a = positions[bond.First], b = positions[bond.Second], midpoint = (a + b) * 0.5f;
        GetBatch(structure.Atoms[bond.First].Element, batches, model, fallback, resources).Cylinder(a, midpoint);
        GetBatch(structure.Atoms[bond.Second].Element, batches, model, fallback, resources).Cylinder(midpoint, b);
      }
      if (representation == PdbRepresentation.Residues || representation == PdbRepresentation.Chains)
        DrawBackbone(structure, positions, representation, batches, model, fallback, resources);
      foreach (Batch batch in batches.Values) batch.Flush();
      foreach (string warning in structure.Warnings) Debug.LogWarning("PDB: " + warning);
      Debug.Log($"PDB loaded: {structure.Atoms.Count} atoms, {structure.Bonds.Count} bonds ({representation}).");
      return true;
    }
    catch (Exception exception)
    {
      if (model != null) UnityEngine.Object.Destroy(model);
      model = null;
      Debug.LogWarning("PDB import failed: " + exception.Message);
      return false;
    }
  }

  private static void DrawBackbone(PdbStructure structure, Vector3[] positions, PdbRepresentation representation,
    Dictionary<string, Batch> batches, GameObject model, Material fallback, RuntimePdbResources resources)
  {
    List<List<int>> paths = structure.GetBackboneSegments();
    if (paths.Count == 0) throw new FormatException("No standard protein residues with C-alpha atoms for this representation.");
    var chains = new Dictionary<string, int>();
    foreach (var atom in structure.Atoms)
      if (!chains.ContainsKey(atom.Chain)) chains.Add(atom.Chain, chains.Count);
    foreach (var path in paths)
    {
      string chain = structure.Atoms[path[0]].Chain;
      string key = "Chain " + chain;
      Batch batch = GetBatch(key, batches, model, fallback, resources);
      batch.SetColor(Color.HSVToRGB((chains[chain] * 0.618034f) % 1f, 0.65f, 0.95f));
      if (representation == PdbRepresentation.Residues)
      {
        foreach (int index in path) batch.Sphere(positions[index], 0.65f);
        for (int i = 1; i < path.Count; i++) batch.Cylinder(positions[path[i - 1]], positions[path[i]], 0.18f);
        continue;
      }
      Vector3 last = positions[path[0]];
      batch.Sphere(last, 0.32f);
      for (int i = 0; i < path.Count - 1; i++)
      {
        Vector3 p0 = positions[path[Math.Max(0, i - 1)]], p1 = positions[path[i]];
        Vector3 p2 = positions[path[i + 1]], p3 = positions[path[Math.Min(path.Count - 1, i + 2)]];
        for (int step = 1; step <= 6; step++)
        {
          float t = step / 6f;
          Vector3 next = 0.5f * ((2f * p1) + (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
          batch.Cylinder(last, next, 0.32f);
          batch.Sphere(next, 0.32f);
          last = next;
        }
      }
    }
  }

  private static Batch GetBatch(string element, Dictionary<string, Batch> batches, GameObject root, Material fallback, RuntimePdbResources resources)
  {
    if (!batches.TryGetValue(element, out Batch batch))
    {
      var material = new Material(fallback) { name = "PDB " + element, color = ElementColor(element), mainTexture = null };
      resources.Materials.Add(material);
      batch = new Batch(root, material, resources);
      batches.Add(element, batch);
    }
    return batch;
  }

  private static Color ElementColor(string element)
  {
    switch (element)
    {
      case "C": return new Color(0.28f, 0.28f, 0.30f);
      case "N": return new Color(0.15f, 0.30f, 0.95f);
      case "O": return new Color(0.95f, 0.12f, 0.12f);
      case "S": return new Color(1f, 0.82f, 0.12f);
      case "P": return new Color(1f, 0.45f, 0.08f);
      case "H": case "D": return Color.white;
      case "CL": case "F": return new Color(0.2f, 0.85f, 0.2f);
      case "FE": return new Color(0.8f, 0.4f, 0.15f);
      case "ZN": return new Color(0.55f, 0.6f, 0.7f);
      default: return new Color(0.7f, 0.45f, 0.75f);
    }
  }

  // Display radii in Angstroms, deliberately smaller than van der Waals radii.
  private static float Radius(string element)
  {
    switch (element)
    {
      case "H": case "D": return 0.22f;
      case "O": return 0.34f;
      case "N": return 0.36f;
      case "S": case "P": return 0.46f;
      default: return 0.40f;
    }
  }

  private sealed class Batch
  {
    private readonly GameObject root;
    private readonly Material material;
    private readonly RuntimePdbResources resources;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<int> indices = new List<int>();
    private const int Sides = 10, Rings = 7;

    public Batch(GameObject root, Material material, RuntimePdbResources resources)
    { this.root = root; this.material = material; this.resources = resources; }

    public void Sphere(Vector3 center, float radius)
    {
      EnsureCapacity((Rings + 1) * (Sides + 1));
      int start = vertices.Count;
      for (int ring = 0; ring <= Rings; ring++)
      {
        float phi = Mathf.PI * ring / Rings;
        for (int side = 0; side <= Sides; side++)
        {
          float theta = 2 * Mathf.PI * side / Sides;
          var normal = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
          vertices.Add(center + normal * radius); normals.Add(normal);
        }
      }
      for (int ring = 0; ring < Rings; ring++)
        for (int side = 0; side < Sides; side++)
        {
          int a = start + ring * (Sides + 1) + side, b = a + Sides + 1;
          Triangle(a, a + 1, b); Triangle(a + 1, b + 1, b);
        }
    }

    public void SetColor(Color color) { material.color = color; }

    public void Cylinder(Vector3 from, Vector3 to, float radius = 0.12f)
    {
      Vector3 axis = to - from;
      if (axis.sqrMagnitude < 0.000001f) return;
      axis.Normalize();
      Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
      Vector3 v = Vector3.Cross(axis, u);
      EnsureCapacity((Sides + 1) * 2);
      int start = vertices.Count;
      for (int side = 0; side <= Sides; side++)
      {
        float angle = 2 * Mathf.PI * side / Sides;
        Vector3 normal = Mathf.Cos(angle) * u + Mathf.Sin(angle) * v;
        vertices.Add(from + normal * radius); normals.Add(normal);
        vertices.Add(to + normal * radius); normals.Add(normal);
      }
      for (int side = 0; side < Sides; side++)
      {
        int a = start + 2 * side;
        Triangle(a, a + 2, a + 1); Triangle(a + 2, a + 3, a + 1);
      }
    }

    private void Triangle(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
    private void EnsureCapacity(int count) { if (vertices.Count + count > 60000) Flush(); }
    public void Flush()
    {
      if (vertices.Count == 0) return;
      var mesh = new Mesh { name = material.name };
      resources.Meshes.Add(mesh);
      mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
      mesh.UploadMeshData(true);
      var child = new GameObject(material.name);
      child.transform.SetParent(root.transform, false);
      child.AddComponent<MeshFilter>().sharedMesh = mesh;
      child.AddComponent<MeshRenderer>().sharedMaterial = material;
      vertices.Clear(); normals.Clear(); indices.Clear();
    }
  }
}
