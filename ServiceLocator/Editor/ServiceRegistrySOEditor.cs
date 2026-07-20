using CupkekGames.EditorUI;
using CupkekGames.AssetFinder;
using CupkekGames.AssetFinder.Editor;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace CupkekGames.Services.Editor
{
  [CustomEditor(typeof(ServiceRegistrySO))]
  [InitializeOnLoad]
  public class ServiceRegistrySOEditor : UnityEditor.Editor
  {
    private List<ServiceProviderSO> _previousProviders = new();
    private readonly List<(Object instance, string registerAsType)> _previousServiceEntries = new();

    static ServiceRegistrySOEditor()
    {
      EditorApplication.delayCall += RegisterEditorServices;
      EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>
    /// Maps Unity play-mode transitions (see <see cref="PlayModeStateChange"/>) to locator hygiene:
    /// <list type="bullet">
    /// <item><description><b>ExitingEditMode</b> — last moment before Play Mode; <see cref="ServiceLocator.ClearAll"/>
    /// so <c>RegisterInEditor</c> never stacks with runtime <see cref="ServiceRegistry"/>.</description></item>
    /// <item><description><b>EnteredEditMode</b> — next editor update after leaving Play Mode; <see cref="ServiceLocator.ClearAll"/>
    /// again (runtime teardown / lazy descriptors may leave residue), then <see cref="RegisterEditorServices"/> on
    /// <see cref="EditorApplication.delayCall"/>.</description></item>
    /// <item><description><b>EnteredPlayMode</b> / <b>ExitingPlayMode</b> — not used: the edit→play boundary is
    /// <see cref="PlayModeStateChange.ExitingEditMode"/>; the play→edit boundary is safest as
    /// <see cref="PlayModeStateChange.EnteredEditMode"/> after objects have finished tearing down, not
    /// <see cref="PlayModeStateChange.ExitingPlayMode"/> while play is still unwinding.</description></item>
    /// </list>
    /// </summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
      if (state == PlayModeStateChange.ExitingEditMode)
        ServiceLocator.ClearAll();
      else if (state == PlayModeStateChange.EnteredEditMode)
      {
        ServiceLocator.ClearAll();
        EditorApplication.delayCall += RegisterEditorServices;
      }
    }

    private static void RegisterEditorServices()
    {
      // With Domain Reload enabled, entering Play re-runs this class's [InitializeOnLoad] static
      // ctor INSIDE play mode, and the queued delayCall would land right after scene Awake --
      // RetriggerEditorRegistrationsAll's UnregisterAll would then strip every runtime-registered
      // SO service one frame after the boot sequence registered them. Editor bootstrap is
      // edit-mode-only (same guard as EditorAutoCatalogHost).
      if (EditorApplication.isPlayingOrWillChangePlaymode)
        return;
      ServiceRegistryEditorBootstrap.RegisterEditorServicesAutomatic();
    }

    public override VisualElement CreateInspectorGUI()
    {
      VisualElement root = new VisualElement();
      var config = (ServiceRegistrySO)target;

      // Cache initial lists
      CachePreviousState(config);

      // Draw Register In Editor field
      var registerInEditorProp = serializedObject.FindProperty("_registerInEditor");
      root.Add(new PropertyField(registerInEditorProp));

      // Draw Providers section with toolbar
      var providersProp = serializedObject.FindProperty("_providers");
      root.Add(CreateListFieldWithToolbar(
        providersProp,
        typeof(ServiceProviderSO),
        "ServiceRegistrySO_providers"
      ));

      // Service entries (typed registration)
      var serviceEntriesProp = serializedObject.FindProperty("_serviceEntries");
      root.Add(CreateServiceEntriesSection(serviceEntriesProp));

      // Track value changes for registration logic
      root.TrackPropertyValue(registerInEditorProp, prop =>
      {
        if (prop.boolValue)
        {
          config.RegisterAll();
          CachePreviousState(config);
        }
        else
        {
          UnregisterPrevious();
        }
      });

      root.TrackPropertyValue(providersProp, _ =>
      {
        if (config.RegisterInEditor)
        {
          UnregisterPrevious();
          config.RegisterAll();
          CachePreviousState(config);
        }
      });

      root.TrackPropertyValue(serviceEntriesProp, _ =>
      {
        if (config.RegisterInEditor)
        {
          UnregisterPrevious();
          config.RegisterAll();
          CachePreviousState(config);
        }
      });

      return root;
    }

    private VisualElement CreateServiceEntriesSection(SerializedProperty listProp)
    {
      var container = new VisualElement();
      var header = new Label(
        "ScriptableObject services with optional register-as interface.\nEach entry: asset + interface (or concrete).")
      {
        style =
        {
          whiteSpace = WhiteSpace.Normal,
          backgroundColor = EditorColorPalette.SurfaceWeak,
          color = EditorColorPalette.TextSecondary,
          borderTopColor = EditorColorPalette.BorderMedium,
          borderBottomColor = EditorColorPalette.BorderMedium,
          borderLeftColor = EditorColorPalette.BorderMedium,
          borderRightColor = EditorColorPalette.BorderMedium,
          borderTopWidth = 1f,
          borderBottomWidth = 1f,
          borderLeftWidth = 1f,
          borderRightWidth = 1f,
          borderTopLeftRadius = 4f,
          borderTopRightRadius = 4f,
          borderBottomLeftRadius = 4f,
          borderBottomRightRadius = 4f,
          paddingTop = 5,
          paddingBottom = 5,
          paddingLeft = 10,
          paddingRight = 10,
          marginTop = 8,
          marginBottom = 4
        }
      };
      container.Add(header);

      var listField = new PropertyField(listProp);
      container.Add(listField);

      var toolbarConfig = new AssetFinderToolbarConfig
      {
        AssetType = typeof(ScriptableObject),
        PersistenceKey = "ServiceRegistrySO_serviceEntries"
      };

      var toolbar = new AssetFinderToolbar(toolbarConfig);
      toolbar.OnAssetsFound += assets => AddServiceEntriesToList(listProp, assets);
      toolbar.OnClear += () => ClearList(listProp);
      container.Add(toolbar);

      return container;
    }

    private static void AddServiceEntriesToList(SerializedProperty listProp, List<Object> assets)
    {
      int addedCount = 0;
      foreach (Object asset in assets)
      {
        if (asset is not ScriptableObject so)
          continue;

        bool exists = false;
        for (int i = 0; i < listProp.arraySize; i++)
        {
          SerializedProperty el = listProp.GetArrayElementAtIndex(i);
          if (el.FindPropertyRelative(nameof(ServiceEntry.Instance)).objectReferenceValue == so)
          {
            exists = true;
            break;
          }
        }

        if (!exists)
        {
          listProp.InsertArrayElementAtIndex(listProp.arraySize);
          SerializedProperty newEl = listProp.GetArrayElementAtIndex(listProp.arraySize - 1);
          newEl.FindPropertyRelative(nameof(ServiceEntry.Instance)).objectReferenceValue = so;
          newEl.FindPropertyRelative(nameof(ServiceEntry.RegisterAsType)).stringValue = "";
          addedCount++;
        }
      }

      listProp.serializedObject.ApplyModifiedProperties();
      Debug.Log($"Added {addedCount} service entries.");
    }

    private VisualElement CreateListFieldWithToolbar(SerializedProperty listProp, System.Type assetType,
      string persistenceKey)
    {
      var container = new VisualElement();

      // Draw the list with PropertyField (handles MultiLineHeader attribute etc)
      var listField = new PropertyField(listProp);
      container.Add(listField);

      // Add the toolbar
      var toolbarConfig = new AssetFinderToolbarConfig
      {
        AssetType = assetType,
        PersistenceKey = persistenceKey
      };

      var toolbar = new AssetFinderToolbar(toolbarConfig);
      toolbar.OnAssetsFound += assets => AddToList(listProp, assets);
      toolbar.OnClear += () => ClearList(listProp);
      container.Add(toolbar);

      return container;
    }

    private void AddToList(SerializedProperty property, List<Object> assets)
    {
      int addedCount = 0;
      foreach (var asset in assets)
      {
        bool exists = false;
        for (int i = 0; i < property.arraySize; i++)
        {
          if (property.GetArrayElementAtIndex(i).objectReferenceValue == asset)
          {
            exists = true;
            break;
          }
        }

        if (!exists)
        {
          property.InsertArrayElementAtIndex(property.arraySize);
          property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = asset;
          addedCount++;
        }
      }

      property.serializedObject.ApplyModifiedProperties();
      Debug.Log($"Added {addedCount} assets to list.");
    }

    private void ClearList(SerializedProperty property)
    {
      property.ClearArray();
      property.serializedObject.ApplyModifiedProperties();
      Debug.Log("Cleared list.");
    }

    private void CachePreviousState(ServiceRegistrySO config)
    {
      _previousProviders.Clear();
      foreach (var provider in config.Providers)
      {
        if (provider != null)
          _previousProviders.Add(provider);
      }

      _previousServiceEntries.Clear();
      foreach (ServiceEntry entry in config.ServiceEntries)
      {
        if (entry?.Instance != null)
          _previousServiceEntries.Add((entry.Instance, entry.RegisterAsType ?? ""));
      }
    }

    private void UnregisterPrevious()
    {
      foreach (var provider in _previousProviders)
      {
        if (provider != null)
          provider.UnregisterServices();
      }

      _previousProviders.Clear();

      foreach ((Object instance, string registerAsType) in _previousServiceEntries)
      {
        if (instance is ScriptableObject so)
        {
          System.Type t = ServiceEntry.ResolveServiceType(so, registerAsType);
          if (t != null)
            ServiceLocator.Remove(t);
        }
      }

      _previousServiceEntries.Clear();
    }
  }
}
