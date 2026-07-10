using System;
using System.Collections.Generic;
using System.IO;
using Dweiss;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class HighResolutionScreenshot
{
  private const string FilePrefix = "Graph2VR_Screenshot";
  private const float MinimumSecondsBetweenCaptures = 1f;
  private static readonly string[] ControllerTags = { "LeftController", "RightController" };
  private static float lastCaptureTime = -MinimumSecondsBetweenCaptures;

  public static string Capture()
  {
    if (Time.unscaledTime - lastCaptureTime < MinimumSecondsBetweenCaptures)
    {
      Debug.Log("High-resolution screenshot skipped: capture cooldown is still active.");
      return null;
    }

    Camera camera = Camera.main;
    if (camera == null)
    {
      Debug.LogWarning("High-resolution screenshot failed: no main camera found.");
      return null;
    }

    int width = Settings.Instance == null ? 7680 : Mathf.Max(1, Settings.Instance.screenshotWidth);
    int height = Settings.Instance == null ? 4320 : Mathf.Max(1, Settings.Instance.screenshotHeight);
    string filePath = GetNextFilePath();
    if (filePath == null)
    {
      Debug.Log("High-resolution screenshot skipped: a screenshot for this second already exists.");
      return null;
    }

    CaptureCamera(camera, filePath, width, height);
    lastCaptureTime = Time.unscaledTime;
    SendCaptureFeedback();

    return filePath;
  }

  private static void CaptureCamera(Camera camera, string filePath, int width, int height)
  {
    RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
    {
      antiAliasing = 1
    };
    Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);

    RenderTexture previousActive = RenderTexture.active;
    RenderTexture previousTargetTexture = camera.targetTexture;
    List<ControllerVisibilityState> controllerStates = null;

    UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
    bool restorePostProcessing = false;
    bool previousPostProcessing = false;

    try
    {
      if (Settings.Instance != null && Settings.Instance.screenshotHideControllers)
      {
        controllerStates = SetControllerVisibility(false);
      }

      if (Settings.Instance != null && Settings.Instance.screenshotDisablePostProcessing && cameraData != null)
      {
        previousPostProcessing = cameraData.renderPostProcessing;
        cameraData.renderPostProcessing = false;
        restorePostProcessing = true;
      }

      camera.targetTexture = renderTexture;
      RenderTexture.active = renderTexture;
      camera.Render();

      image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
      image.Apply();
      File.WriteAllBytes(filePath, image.EncodeToPNG());

      Debug.Log($"High-resolution screenshot saved: {filePath} ({width}x{height})");
    }
    finally
    {
      if (restorePostProcessing)
      {
        cameraData.renderPostProcessing = previousPostProcessing;
      }

      RestoreControllerVisibility(controllerStates);
      camera.targetTexture = previousTargetTexture;
      RenderTexture.active = previousActive;

      renderTexture.Release();
      UnityEngine.Object.Destroy(image);
      UnityEngine.Object.Destroy(renderTexture);
    }
  }

  private static List<ControllerVisibilityState> SetControllerVisibility(bool visible)
  {
    List<ControllerVisibilityState> states = new List<ControllerVisibilityState>();
    foreach (string tag in ControllerTags)
    {
      GameObject controller = FindGameObjectWithTag(tag);
      if (controller == null)
      {
        continue;
      }

      states.Add(new ControllerVisibilityState(controller, controller.activeSelf));
      controller.SetActive(visible);
    }

    return states;
  }

  private static void RestoreControllerVisibility(List<ControllerVisibilityState> states)
  {
    if (states == null)
    {
      return;
    }

    foreach (ControllerVisibilityState state in states)
    {
      if (state.GameObject != null)
      {
        state.GameObject.SetActive(state.WasActive);
      }
    }
  }

  private static GameObject FindGameObjectWithTag(string tag)
  {
    try
    {
      return GameObject.FindGameObjectWithTag(tag);
    }
    catch (UnityException)
    {
      return null;
    }
  }

  private static string GetNextFilePath()
  {
    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    string directory = Application.persistentDataPath;
    string filePath = Path.Combine(directory, $"{FilePrefix}_{timestamp}.png");

    if (!File.Exists(filePath))
    {
      return filePath;
    }

    return null;
  }

  private static void SendCaptureFeedback()
  {
    if (ControlerInput.instance == null)
    {
      return;
    }

    ControlerInput.instance.VibrateLeft();
    ControlerInput.instance.VibrateRight();
  }

  private struct ControllerVisibilityState
  {
    public readonly GameObject GameObject;
    public readonly bool WasActive;

    public ControllerVisibilityState(GameObject gameObject, bool wasActive)
    {
      GameObject = gameObject;
      WasActive = wasActive;
    }
  }
}
