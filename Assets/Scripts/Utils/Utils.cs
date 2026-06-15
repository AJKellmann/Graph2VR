using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VDS.RDF;
using VDS.RDF.Query;

public class Utils
{
  static public Texture2D ClampTextureSize(Texture2D source, int targetWidth, int targetHeight)
  {
    int scaleWidth;
    int scaleHeight;
    float aspect = (float)source.width / source.height;
    if (source.width > targetWidth)
    {
      scaleWidth = targetWidth;
      scaleHeight = (int)(targetHeight / aspect);
      if (scaleHeight > targetHeight)
      {
        scaleWidth = (int)(targetWidth * aspect);
        scaleHeight = targetHeight;
      }
      return ScaleTexture(source, scaleWidth, scaleHeight);
    }
    return source;
  }

  static public Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
  {
    Texture2D result = new Texture2D(targetWidth, targetHeight, source.format, false);
    for (int i = 0; i < result.height; ++i)
    {
      for (int j = 0; j < result.width; ++j)
      {
        Color newColor = source.GetPixelBilinear((float)j / (float)result.width, (float)i / (float)result.height);
        result.SetPixel(j, i, newColor);
      }
    }
    result.filterMode = FilterMode.Bilinear;
    result.Apply(true, false);
    return result;
  }


  static public void GetStringFromVRKeyboard(Action<string> callback, string initialValue = "", string placeHolder = "...")
  {
    Main.instance.keyboard.GetComponent<GetStringFromKeyboardHandler>().GetString(callback, initialValue, placeHolder);
  }

  static public GameObject FindClosestGraph(Vector3 position)
  {
    GameObject closest = null;
    GameObject[] graphs = GameObject.FindGameObjectsWithTag("Graph");
    float closestDistance = float.MaxValue;
    foreach (GameObject graph in graphs)
    {
      float distance = Vector3.Distance(position, graph.GetComponent<Graph>().boundingSphere.transform.position);
      if (distance < closestDistance)
      {
        closestDistance = distance;
        closest = graph;
      }
    }
    return closest;
  }

  static public Node GetPartnerNode(Node node, Edge edge)
  {
    if (IsSubjectNode(node, edge))
    {
      return edge.displayObject;
    }
    else
    {
      return edge.displaySubject;
    }
  }

  static public bool IsSubjectNode(Node node, Edge edge)
  {
    return node.graph.RealNodeValue(node.graphNode) == node.graph.RealNodeValue(edge.graphSubject);
  }

  static public Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
  {
    float u = 1 - t;
    float tt = t * t;
    float uu = u * u;
    float uuu = uu * u;
    float ttt = tt * t;

    Vector3 p = uuu * p0;
    p += 3 * uu * t * p1;
    p += 3 * u * tt * p2;
    p += ttt * p3;
    return p;
  }

  static public string GetShortLabelFromUri(string uri)
  {
    string queryLabel = GetQueryParameterLabel(uri);
    if (!string.IsNullOrEmpty(queryLabel))
    {
      return queryLabel;
    }

    var list = uri.Split('/', '#');
    if (list.Length > 0)
    {
      return DecodeUriLabel(list[list.Length - 1]);
    }
    else
    {
      return uri;
    }
  }

  static public string GetQueryParameterLabel(string uri)
  {
    if (string.IsNullOrEmpty(uri))
    {
      return "";
    }

    Uri parsedUri;
    if (!Uri.TryCreate(uri, UriKind.Absolute, out parsedUri) || string.IsNullOrEmpty(parsedUri.Query))
    {
      return "";
    }

    Dictionary<string, string> parameters = new Dictionary<string, string>();
    string query = parsedUri.Query.TrimStart('?');
    foreach (string pair in query.Split('&'))
    {
      if (string.IsNullOrEmpty(pair))
      {
        continue;
      }

      string[] keyValue = pair.Split(new[] { '=' }, 2);
      string key = DecodeUriLabel(keyValue[0]).ToLowerInvariant();
      string value = keyValue.Length > 1 ? DecodeUriLabel(keyValue[1]) : "";
      if (!parameters.ContainsKey(key))
      {
        parameters.Add(key, value);
      }
    }

    string[] preferredKeys = { "name", "label", "target variable", "variable", "id" };
    foreach (string key in preferredKeys)
    {
      if (parameters.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
      {
        return value;
      }
    }

    return "";
  }

  static public string DecodeUriLabel(string value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return value;
    }

    return Uri.UnescapeDataString(value.Replace("+", " "));
  }

  static public Dictionary<string, Tuple<string, int>> GetPredicatsList(SparqlResultSet sparqlResults)
  {
    if (sparqlResults == null)
    {
      return new Dictionary<string, Tuple<string, int>>();
    }

    return sparqlResults.Aggregate(new Dictionary<string, Tuple<string, int>>(), (accum, result) =>
    {
      result.TryGetValue("p", out INode predicate);
      result.TryGetValue("count", out INode countNode);
      result.TryGetValue("label", out INode labelNode);

      string label = labelNode != null ? labelNode.ToString() : "";

      if (predicate != null)
      {
        string predicateString = predicate.ToString();
        int count = 0;
        if (countNode != null)
        {
          int.TryParse(countNode.ToString(), out count);
        }
        Tuple<string, int> value = new Tuple<string, int>(label, count);
        if (!accum.ContainsKey(predicateString))
        {
          accum.Add(predicateString, value);
        }
        else
        {
          // Why overwrite and not just skip?
          accum[predicateString] = value;
        }
      }
      return accum;
    });
  }
}

