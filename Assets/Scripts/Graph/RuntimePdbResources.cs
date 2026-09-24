using System.Collections.Generic;
using UnityEngine;

// The molecule owns its generated GPU resources, rather than leaking them on reload.
public sealed class RuntimePdbResources : MonoBehaviour
{
  public PdbStructure Structure { get; set; }
  public readonly List<Mesh> Meshes = new List<Mesh>();
  public readonly List<Material> Materials = new List<Material>();

  private void OnDestroy()
  {
    foreach (Mesh mesh in Meshes) if (mesh != null) Destroy(mesh);
    foreach (Material material in Materials) if (material != null) Destroy(material);
  }
}
