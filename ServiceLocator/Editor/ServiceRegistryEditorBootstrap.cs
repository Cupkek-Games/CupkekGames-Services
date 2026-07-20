using CupkekGames.AssetFinder;
using CupkekGames.AssetFinder.Editor;
using System.Collections.Generic;
using CupkekGames.Services;
using UnityEditor;
using UnityEngine;

namespace CupkekGames.Services.Editor
{
  /// <summary>
  /// Centralizes editor-time <see cref="ServiceRegistrySO"/> bootstrap: unregister-then-register,
  /// bulk <c>Register In Editor</c> edits, and optional automatic re-apply after domain reload.
  /// </summary>
  public static class ServiceRegistryEditorBootstrap
  {
    public const string AutoRegisterOnEditorLoadKey = "CupkekGames.Services.AutoRegisterOnEditorLoad";

    public static bool AutoRegisterOnEditorLoad
    {
      get => EditorPrefs.GetBool(AutoRegisterOnEditorLoadKey, true);
      set => EditorPrefs.SetBool(AutoRegisterOnEditorLoadKey, value);
    }

    public static List<ServiceRegistrySO> FindAllServiceRegistryAssets()
    {
      var result = new List<ServiceRegistrySO>();
      string[] guids = AssetDatabase.FindAssets($"t:{nameof(ServiceRegistrySO)}");
      foreach (string guid in guids)
      {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        ServiceRegistrySO config = AssetDatabase.LoadAssetAtPath<ServiceRegistrySO>(path);
        if (config != null)
          result.Add(config);
      }

      return result;
    }

    /// <summary>
    /// Unregister then register each registry. Registration runs only when <see cref="ServiceRegistrySO.RegisterInEditor"/> is true.
    /// </summary>
    public static void RetriggerEditorRegistrations(IReadOnlyList<ServiceRegistrySO> registries)
    {
      if (registries == null)
        return;
      for (int i = 0; i < registries.Count; i++)
      {
        ServiceRegistrySO r = registries[i];
        if (r == null)
          continue;
        r.UnregisterAll();
        if (r.RegisterInEditor)
          r.RegisterAll();
      }
    }

    public static void RetriggerEditorRegistrationsFiltered(List<AssetFinderFilterConfig> filters)
    {
      List<ServiceRegistrySO> list = CupkekGames.AssetFinder.Editor.AssetFinder.FindAssets<ServiceRegistrySO>(filters ?? new List<AssetFinderFilterConfig>());
      RetriggerEditorRegistrations(list);
    }

    public static void RetriggerEditorRegistrationsAll()
    {
      RetriggerEditorRegistrations(FindAllServiceRegistryAssets());
    }

    public static void ClearAllServices()
    {
      ServiceLocator.ClearAll();
    }

    public static void SetRegisterInEditorBulk(bool value, IReadOnlyList<ServiceRegistrySO> registries)
    {
      if (registries == null)
        return;
      for (int i = 0; i < registries.Count; i++)
      {
        ServiceRegistrySO r = registries[i];
        if (r == null)
          continue;
        SerializedObject so = new SerializedObject(r);
        SerializedProperty prop = so.FindProperty("_registerInEditor");
        if (prop != null)
        {
          prop.boolValue = value;
          so.ApplyModifiedProperties();
        }

        if (!value)
          r.UnregisterAll();
      }

      AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Called from <see cref="ServiceRegistrySOEditor"/> when automatic editor bootstrap is enabled.
    /// Edit-mode only: the retrigger path starts with UnregisterAll on every registry, which must
    /// never run against a live play session's registrations.
    /// </summary>
    public static void RegisterEditorServicesAutomatic()
    {
      if (!AutoRegisterOnEditorLoad)
        return;
      if (EditorApplication.isPlayingOrWillChangePlaymode)
        return;
      RetriggerEditorRegistrationsAll();
    }
  }
}
