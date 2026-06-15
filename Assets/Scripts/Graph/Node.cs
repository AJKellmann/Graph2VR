using Dweiss;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using VDS.RDF;

public class Node : MonoBehaviour
{
  public Hierarchical hierarchicalSettings = new();
  public Graph graph;
  private Canvas infoPanel;

  public string uri = ""; // Full URI, empty if literal
  public string label = "";
  public string cachedNodeLabel = ""; // label of the node, (before it gets converted to variable)
  public bool lockPosition = false;

  public INode graphNode;
  public List<Edge> connections = new();

  public TMPro.TextMeshPro textMesh;
  
  // Variables for the Force-directed algorithm
  public Vector3 displacement;

  private bool isVariable = false;
  private bool isActiveInMenu = false;
  private bool isPointerHovered = false;
  private bool isControllerHovered = false;
  private bool isControllerGrabbed = false;
  private Texture2D cachedImage = null;
  private string cachedModelUri = "";
  private float modelDisplaySize = -1f;
  private GameObject modelObject = null;

  public bool LockPosition
  {
    get => lockPosition;
    set
    {
      lockPosition = value;
      transform.Find("Nail").gameObject.SetActive(lockPosition);
    }
  }

  public bool IsActiveInMenu
  {
    get => isActiveInMenu;
    set
    {
      isActiveInMenu = value;
      UpdateColor();
    }
  }

  public bool IsVariable
  {
    get => isVariable;
    set
    {
      isVariable = value;
      UpdateColor();
    }
  }

  public bool IsPointerHovered
  {
    get => isPointerHovered;
    set
    {
      isPointerHovered = value;
      UpdateColor();
    }
  }

  public bool IsControllerHovered
  {
    get => isControllerHovered;
    set
    {
      isControllerHovered = value;
      UpdateColor();
    }
  }

  public bool IsControllerGrabbed
  {
    get => isControllerGrabbed;
    set
    {
      isControllerGrabbed = value;
      UpdateColor();
    }
  }

  public void Awake()
  {
    textMesh = GetComponentInChildren<TMPro.TextMeshPro>(true);
  }

  public void Start()
  {
    InvokeRepeating("UpdateDisplay", 1, 1);
    UpdateColor();

    ShowFocus(Color.white);
    LTDescr focusTween = LeanTween.value(gameObject, (float value) => transform.Find("Selected").gameObject.GetComponentInChildren<Renderer>().material.color = new Color32(1, 1, 1, (byte)(value * 255)), 1f, 0f, 3f);
    if (focusTween != null)
    {
      focusTween.setOnComplete(() =>
      {
        transform.Find("Selected").gameObject.GetComponentInChildren<Renderer>().material.color = Color.black;
        HideFocus();
      });
    }
    else
    {
      transform.Find("Selected").gameObject.GetComponentInChildren<Renderer>().material.color = Color.black;
      HideFocus();
    }
    if (!isVariable)
    {
      RefineNode();
    }
  }

  public void RefineNode()
  {
    if (graphNode == null)
    {
      return;
    }
    else
    {
      ConnectLabelToNode();
      ConnectImageToNode();
      ConnectModelToNode();
    }
  }

  private void ConnectLabelToNode()
  {
    Edge labelEdge = connections.Find(edge => edge.displaySubject == this && IsLabelPredicate(edge.uri));
    if (labelEdge != null)
    {
      SetLabel(labelEdge.displayObject.uri);
    }
  }

  private void ConnectModelToNode()
  {
    List<string> modelPredicats = new List<string>();
    foreach (Edge edge in connections)
    {
      if (IsModelPredicate(edge.uri))
      {
        modelPredicats.Add(edge.displayObject.uri);
      }
    }
    if (modelPredicats.Count > 0)
    {
      SetModelFromList(modelPredicats);
    }
  }

  private void ConnectImageToNode()
  {
    List<string> imagePredicats = new List<string>();
    foreach (Edge edge in connections)
    {
      if (IsImagePredicate(edge.uri))
      {
        imagePredicats.Add(edge.displayObject.uri);
      }
    }
    if (imagePredicats.Count > 0)
    {
      SetImageFromList(imagePredicats);
    }
  }

  private bool IsLabelPredicate(string predicate)
  {
    return predicate == "http://www.w3.org/2000/01/rdf-schema#label";
  }

  private bool IsImagePredicate(string predicate)
  {
    foreach (string pred in Settings.Instance.imagePredicates)
    {
      if (predicate.Equals(pred))
      {
        return true;
      }
    }
    return false;
  }

  private bool IsModelPredicate(string predicate)
  {
    if (Settings.Instance.modelPredicates == null)
    {
      return false;
    }

    foreach (string pred in Settings.Instance.modelPredicates)
    {
      if (predicate.Equals(pred))
      {
        return true;
      }
    }
    return false;
  }

  public void AddConnection(Edge edge)
  {
    if (!connections.Contains(edge))
    {
      connections.Add(edge);
    }
  }

  private void UpdateColor()
  {
    if (IsControllerHovered || IsPointerHovered)
    {
      SetColor(ColorSettings.instance.edgeHoverColor);
    }
    else if (IsActiveInMenu)
    {
      SetColor(ColorSettings.instance.edgeGrabbedColor);
    }
    else if (IsControllerGrabbed)
    {
      SetColor(ColorSettings.instance.edgeGrabbedColor);
    }
    else if (IsVariable)
    {
      SetColor(ColorSettings.instance.variableColor);
    }
    else if (graphNode != null)
    {
      UpdateColorByNodeType();
    }
    else
    {
      SetColor(ColorSettings.instance.defaultNodeColor);
    }
  }

  public bool UpdateColorByVOWL()
  {
    List<Edge> nodeTypes = graph.edgeList.FindAll(
      edge => edge.displaySubject == this &&
      edge.uri == "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    );

    foreach (Edge edge in nodeTypes)
    {
      switch (edge.displayObject.uri)
      {
        case "http://www.w3.org/2002/07/owl#deprecated":
          SetColor(Settings.Instance.deprecatedColor);
          return true;
        case "http://www.w3.org/2002/07/owl#Class":
          SetColor(Settings.Instance.nodeOwlClassColor);
          return true;
        case "http://www.w3.org/2000/01/rdf-schema#Class":
          SetColor(Settings.Instance.nodeRdfsClassColor);
          return true;
        case "https://www.w3.org/1999/02/22-rdf-syntax-ns#Property":
          SetColor(Settings.Instance.nodeOwlDatatypeColor);
          return true;
      }
    }

     nodeTypes = graph.edgeList.FindAll(
      edge => (edge.displaySubject == this || edge.displayObject == this) && (
      edge.uri == "http://www.w3.org/2000/01/rdf-schema#subClassOf")
    );
    foreach (Edge edge in nodeTypes)
    {
      if (uri == "http://www.w3.org/2002/07/owl#Thing")
      {
        SetColor(Settings.Instance.nodeOwlClassColor);
      }
      else {SetColor(Settings.Instance.nodeRdfsClassColor);}
      return true;
    }
 
    return false;
  }

  private void UpdateColorByNodeType()
  {
    // First check VOWL color schema
    if (!UpdateColorByVOWL())
    {
      // If we didn't find a color in VOWL schema check the node type
      switch (graphNode.NodeType)
      {
        case NodeType.Variable:
          SetColor(ColorSettings.instance.variableColor);
          break;
        case NodeType.Blank:
          uri = "";
          SetColor(ColorSettings.instance.blankNodeColor);
          break;
        case NodeType.Literal:
          SetLabel(((ILiteralNode)graphNode).Value);
          uri = "";
          SetColor(Settings.Instance.literalColor);
          break;
        case NodeType.Uri:
          uri = ((IUriNode)graphNode).Uri.OriginalString;
          if (uri == "http://www.w3.org/2002/07/owl#Thing")
          {
            SetColor(Settings.Instance.nodeOwlClassColor);
          }
          else SetColor(ColorSettings.instance.uriColor);
          break;
      }
    }
  }

  void Update()
  {
    Quaternion cameraFacingRotation = Quaternion.LookRotation(Camera.main.transform.position - transform.position, Vector3.up);
    if (modelObject == null && string.IsNullOrEmpty(cachedModelUri))
    {
      transform.rotation = cameraFacingRotation;
    }
    else
    {
      textMesh.transform.rotation = cameraFacingRotation;
    }

    if (isControllerGrabbed || isPointerHovered)
    {
      textMesh.transform.localScale = Vector3.one * 0.6f;
    }
    else
    {
      textMesh.transform.localScale = Vector3.one * 0.3f;
    }

    // Clamp position
    if (transform.position.y < 0)
    {
      transform.position = new Vector3(transform.position.x, 0, transform.position.z);
    }
  }

  private void OnDestroy()
  {
    if (infoPanel != null)
    {
      Destroy(infoPanel.gameObject);
    }
  }

  public void UpdateSelectionStatus()
  {
    if (IsEdgePartOfSelectedTriple())
    {
      ShowFocus(ColorSettings.instance.nodeSelectedColor);
    }
    else
    {
      HideFocus();
      UpdateColor();
    }
  }

  private void ShowFocus(Color color)
  {
    transform.Find("Selected").gameObject.SetActive(true);
    transform.Find("Selected").gameObject.GetComponentInChildren<Renderer>().material.SetColor("_EmissionColor", color);
  }

  private void HideFocus()
  {
    transform.Find("Selected").gameObject.SetActive(false);
  }

  public void SetImageFromList(List<string> images)
  {
    StartCoroutine(FindAndSetWorkingTexture(images));
  }

  private IEnumerator FindAndSetWorkingTexture(List<string> images)
  {
    foreach (string uri in images)
    {
      UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(uri, false);
      yield return imageRequest.SendWebRequest();
      yield return new WaitForSeconds(Random.Range(0.0f, 5.0f));
      if (imageRequest.result != UnityWebRequest.Result.Success)
      {
        imageRequest.Dispose();
        continue;
      }
      else
      {
        Texture2D image = DownloadHandlerTexture.GetContent(imageRequest);

        image = Utils.ClampTextureSize(image, Settings.Instance.savedMaximumImageWidth, Settings.Instance.savedMaximumImageHeight);
        SetTexture(image, image.width, image.height);

        imageRequest.Dispose();
        break;
      }
    }
    Resources.UnloadUnusedAssets();
  }

  public void SetTexture(Texture2D image, int width, int height)
  {
    cachedImage = image;
    Color color = GetComponent<Renderer>().material.color;
    GameObject borderObject = transform.Find("Border").gameObject;
    GameObject imageObject = borderObject.transform.Find("Image").gameObject;

    borderObject.SetActive(true);
    borderObject.GetComponent<Renderer>().material.color = color;
    gameObject.GetComponent<Renderer>().enabled = false;
    imageObject.GetComponent<Renderer>().material.mainTexture = image;

    float scale = 3.0f;
    float sizeX = 1.0f * scale;
    float aspect = ((float)height / width);
    float sizeY = sizeX / aspect;
    borderObject.transform.localScale = new Vector3(sizeY, sizeX, scale);
  }

  public Texture2D GetTexture()
  {
    return cachedImage;
  }

  public void SetModelFromList(List<string> models)
  {
    if (models == null || models.Count == 0) return;
    cachedModelUri = models.Find(model => !string.IsNullOrWhiteSpace(model)) ?? "";
    StartCoroutine(FindAndSetWorkingModel(models));
  }

  private IEnumerator FindAndSetWorkingModel(List<string> models)
  {
    foreach (string uri in models)
    {
      if (string.IsNullOrWhiteSpace(uri)) continue;

      string modelPath = uri.Split('?')[0].Split('#')[0];
      if (modelPath.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
      {
        Debug.LogWarning($"Runtime model loading for glTF/GLB is not connected yet: {uri}");
        continue;
      }

      bool canLoadObj = RuntimeObjLoader.CanLoad(uri);
      bool canLoadStl = RuntimeStlLoader.CanLoad(uri);
      if (!canLoadObj && !canLoadStl)
      {
        Debug.LogWarning($"Unsupported runtime model format: {uri}");
        continue;
      }

      UnityWebRequest modelRequest = UnityWebRequest.Get(uri);
      yield return modelRequest.SendWebRequest();

      if (modelRequest.result != UnityWebRequest.Result.Success)
      {
        Debug.LogWarning($"Could not load model '{uri}': {modelRequest.error}");
        modelRequest.Dispose();
        continue;
      }

      Material material = new Material(GetComponent<Renderer>().material);
      GameObject loadedModel = null;
      bool modelLoaded = false;

      if (canLoadObj)
      {
        string objText = modelRequest.downloadHandler.text;
        Dictionary<string, Material> materials = null;
        yield return LoadObjMaterials(objText, uri, material, loadedMaterials => materials = loadedMaterials);
        modelLoaded = RuntimeObjLoader.TryCreateGameObject(objText, "NodeModel", material, materials, out loadedModel);
      }
      else if (canLoadStl)
      {
        modelLoaded = RuntimeStlLoader.TryCreateGameObject(modelRequest.downloadHandler.data, "NodeModel", material, out loadedModel);
      }

      if (modelLoaded)
      {
        SetModelObject(loadedModel, uri);
        modelRequest.Dispose();
        break;
      }

      Debug.LogWarning($"Could not parse model: {uri}");
      modelRequest.Dispose();
    }
  }

  private IEnumerator LoadObjMaterials(string objText, string objUri, Material fallbackMaterial, System.Action<Dictionary<string, Material>> callback)
  {
    Dictionary<string, Material> materials = new Dictionary<string, Material>();
    foreach (string mtlUri in RuntimeObjLoader.GetMaterialLibraryUris(objText, objUri))
    {
      UnityWebRequest materialRequest = UnityWebRequest.Get(mtlUri);
      yield return materialRequest.SendWebRequest();

      if (materialRequest.result != UnityWebRequest.Result.Success)
      {
        Debug.LogWarning($"Could not load OBJ material library '{mtlUri}': {materialRequest.error}");
        materialRequest.Dispose();
        continue;
      }

      Dictionary<string, RuntimeObjLoader.ObjMaterialDefinition> definitions = RuntimeObjLoader.ParseMaterialLibrary(materialRequest.downloadHandler.text, mtlUri);
      materialRequest.Dispose();

      foreach (RuntimeObjLoader.ObjMaterialDefinition definition in definitions.Values)
      {
        Material material = new Material(fallbackMaterial);
        material.name = definition.name;
        material.color = definition.color;

        if (!string.IsNullOrEmpty(definition.textureUri))
        {
          UnityWebRequest textureRequest = UnityWebRequestTexture.GetTexture(definition.textureUri, false);
          yield return textureRequest.SendWebRequest();

          if (textureRequest.result == UnityWebRequest.Result.Success)
          {
            Texture2D texture = DownloadHandlerTexture.GetContent(textureRequest);
            material.mainTexture = texture;
          }
          else
          {
            Debug.LogWarning($"Could not load OBJ material texture '{definition.textureUri}': {textureRequest.error}");
          }
          textureRequest.Dispose();
        }

        materials[definition.name] = material;
      }
    }

    callback(materials);
  }

  private void SetModelObject(GameObject loadedModel, string uri)
  {
    if (modelObject != null)
    {
      Destroy(modelObject);
    }

    cachedModelUri = uri;
    modelObject = loadedModel;
    modelObject.transform.SetParent(transform, false);
    modelObject.transform.localRotation = Quaternion.identity;
    NormalizeModelObject(modelObject);

    Transform border = transform.Find("Border");
    if (border != null)
    {
      border.gameObject.SetActive(false);
    }
    GetComponent<Renderer>().enabled = false;
  }

  private void NormalizeModelObject(GameObject loadedModel)
  {
    Renderer renderer = loadedModel.GetComponentInChildren<Renderer>();
    if (renderer == null) return;

    Bounds bounds = renderer.bounds;
    foreach (Renderer childRenderer in loadedModel.GetComponentsInChildren<Renderer>())
    {
      bounds.Encapsulate(childRenderer.bounds);
    }

    float maxSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
    if (maxSize <= 0f) return;

    if (modelDisplaySize <= 0f)
    {
      modelDisplaySize = Settings.Instance.modelNodeSize;
    }

    float scale = modelDisplaySize / maxSize;
    Vector3 localCenter = loadedModel.transform.InverseTransformPoint(bounds.center);
    loadedModel.transform.localScale = Vector3.one * scale;
    loadedModel.transform.localPosition = -localCenter * scale;
  }

  public string GetModelUri()
  {
    return cachedModelUri;
  }

  public float GetModelDisplaySize()
  {
    return modelDisplaySize;
  }

  public void SetModelDisplaySize(float size)
  {
    modelDisplaySize = size;
  }

  public void MakeVariable()
  {
    isVariable = true;

    string newLabel = graph.variableNameManager.GetVariableName(graphNode);
    SetLabel(newLabel);
    UpdateColor();
  }

  public void UndoConversion()
  {
    isVariable = false;
    SetLabel(cachedNodeLabel);
    UpdateColor();
  }

  public void SetColor(Color color)
  {
    GetComponent<Renderer>().material.color = color;
    Transform image = transform.Find("Border");
    if (image)
    {
      image.GetComponent<Renderer>().material.color = color;
    }
  }

  public void SetLabel(string label)
  {
    if (isVariable)
    {
      this.label = GetVariableLabel(label);
    }
    else
    {
      if (Main.instance.languageCode == "")
      {
        this.label = label;
      }
      else
      {
        this.label = label.Replace("@" + Main.instance.languageCode, "");
      }
      cachedNodeLabel = this.label;
    }
    UpdateDisplay();
  }

  private string GetVariableLabel(string label)
  {
    return GetVariableLabelPrefix(label) + label.Replace("@" + Main.instance.languageCode, "");
  }

  private string GetVariableLabelPrefix(string label)
  {
    return label.StartsWith("?") ? "" : "?";
  }

  public string GetLabel()
  {
    return this.label;
  }

  public string GetURIAsString()
  {
    return this.uri;
  }

  public void SetURI(string uri)
  {
    this.uri = uri;
  }

  public System.Uri GetURI()
  {
    return VDS.RDF.UriFactory.Create(this.uri);
  }

  public string GetQueryLabel()
  {
    if (isVariable)
    {
      return GetLabel();
    }
    else
    {
      return graph.RealNodeValue(graphNode);
    }
  }

  private void UpdateDisplay()
  {
    textMesh.text = (label == "") ? uri : label;
  }

  public void ToggleInfoPanel()
  {
    if (infoPanel == null)
    {
      InitiateInfoPanel();
    }
    else
    {
      infoPanel.gameObject.SetActive(!infoPanel.gameObject.activeSelf);
    }
    PositionInfoPanel();
  }

  private void InitiateInfoPanel()
  {
    infoPanel = Instantiate<Canvas>(Resources.Load<Canvas>("UI/ContextMenu"));
    infoPanel.renderMode = RenderMode.WorldSpace;
    infoPanel.worldCamera = GameObject.Find("Controller (right)").GetComponentInChildren<Camera>();
    infoPanel.gameObject.AddComponent<PanelGrabInteraction>();
    ContextMenuHandler selectorHandler = infoPanel.GetComponent<ContextMenuHandler>();
    selectorHandler.Initiate(this);
  }

  private void PositionInfoPanel()
  {
    infoPanel.transform.SetParent(null);
    infoPanel.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);
    infoPanel.transform.SetParent(graph.transform, true);
    infoPanel.transform.position = transform.position;
    infoPanel.transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position, Vector3.up);
    infoPanel.transform.position += infoPanel.transform.rotation * new Vector3(0.5f, 0, 0);// * Mathf.Max(transform.lossyScale.x, gameObject.transform.lossyScale.y);
  }

  private bool IsEdgePartOfSelectedTriple()
  {
    return connections.Find(edge => edge.IsSelected);
  }
}
