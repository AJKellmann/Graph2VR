using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QueryPreviewPanel : MonoBehaviour
{
  private Graph graph;
  private TextMeshProUGUI queryText;
  private string displayedQuery = "";

  public void Initiate(Graph graph)
  {
    this.graph = graph;
    GetComponentInChildren<Button>().onClick.AddListener(() => gameObject.SetActive(false));

    ContextMenuHandler contextMenu = GetComponent<ContextMenuHandler>();
    contextMenu.labelPrefab = Resources.Load<GameObject>("UI/Label");

    TextMeshProUGUI titleText = contextMenu.AddLabel("Current SPARQL query", 30).GetComponentInChildren<TextMeshProUGUI>();
    titleText.fontSize = 30;

    queryText = contextMenu.AddLabel("", 14).GetComponentInChildren<TextMeshProUGUI>();
    queryText.fontSize = 14;
    queryText.enableWordWrapping = true;

    Refresh();
  }

  private void Update()
  {
    Refresh();
  }

  private void Refresh()
  {
    if (graph == null || queryText == null)
    {
      return;
    }

    string query = graph.GetSparqlQueryPreview();
    if (query == displayedQuery)
    {
      return;
    }

    displayedQuery = query;
    queryText.text = query;
  }
}
