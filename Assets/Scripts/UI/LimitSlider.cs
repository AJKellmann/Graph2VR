using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class LimitSlider : MonoBehaviour
{
  private const int NoLimitValue = 0;
  private const int DefaultLimit = 25;

  public Slider limitSlider;
  public TextMeshProUGUI sliderDisplayValue;
  public TextMeshProUGUI startValue;
  public TextMeshProUGUI endValue;
  public List<int> sliderMarks = new List<int>();
  public RectTransform slider;
  public GameObject markPrefab;

  // Start is called before the first frame update
  void Start()
  {
    sliderMarks = new List<int> { NoLimitValue, 1, 5, 10, 25, 50, 100, 250, 500, 1000, 10000 };

    startValue.text = GetDisplayValue(sliderMarks[0]);
    endValue.text = GetDisplayValue(sliderMarks[sliderMarks.Count - 1]);

    limitSlider.wholeNumbers = true;
    limitSlider.minValue = 0;
    limitSlider.maxValue = sliderMarks.Count - 1;
    limitSlider.value = Mathf.Max(0, sliderMarks.IndexOf(DefaultLimit));

    float markXStep = (360) / (sliderMarks.Count - 1);
    for (int i = 0; i < sliderMarks.Count; i++)
    {
      GameObject clone = Instantiate(markPrefab, slider);
      RectTransform transform = clone.GetComponent<RectTransform>();

      transform.localPosition = new Vector3(-180 + (i * markXStep), 0, 0);
      //transform.offsetMin = new Vector2(-190 + (i * markXStep), 0);
      transform.sizeDelta = new Vector2(10, 20);
    }
  }

  // Update is called once per frame
  void Update()
  {
    int selectedValue = sliderMarks[(int)limitSlider.value];
    sliderDisplayValue.text = GetDisplayValue(selectedValue);
    QueryService.Instance.queryLimit = selectedValue;
  }

  private string GetDisplayValue(int value)
  {
    return value == NoLimitValue ? "All" : value.ToString();
  }
}
