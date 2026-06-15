using UnityEngine;

public class PanelGrabInteraction : MonoBehaviour, IGrabInterface
{
  private Transform originalParent;
  private Vector3 originalScale;
  private Canvas canvas;

  private void Awake()
  {
    canvas = GetComponent<Canvas>();
    EnsureGrabCollider();
  }

  private void EnsureGrabCollider()
  {
    BoxCollider boxCollider = GetComponent<BoxCollider>();
    if (boxCollider == null && GetComponent<Collider>() != null)
    {
      return;
    }

    if (boxCollider == null)
    {
      boxCollider = gameObject.AddComponent<BoxCollider>();
    }

    RectTransform rectTransform = GetComponent<RectTransform>();
    Vector2 size = rectTransform != null ? rectTransform.rect.size : new Vector2(800, 600);
    if (size.x <= 1 || size.y <= 1)
    {
      size = new Vector2(800, 600);
    }

    if (boxCollider.size.x <= 1 || boxCollider.size.y <= 1 || boxCollider.size.z < 1)
    {
      boxCollider.size = new Vector3(size.x, size.y, 20);
      boxCollider.center = Vector3.zero;
    }
  }

  void IGrabInterface.ControllerEnter() { }

  void IGrabInterface.ControllerExit() { }

  void IGrabInterface.ControllerGrabBegin(GameObject parent)
  {
    originalParent = transform.parent;
    originalScale = transform.localScale;
    transform.SetParent(parent.transform, true);
  }

  void IGrabInterface.ControllerGrabEnd()
  {
    transform.SetParent(originalParent, true);
    transform.localScale = originalScale;
    if (canvas != null && Camera.main != null)
    {
      transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position, Vector3.up);
    }
  }
}
