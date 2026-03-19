// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
#if CREATOR
using Polytoria.Creator.Properties;
using Polytoria.Datamodel.Creator;
using System.IO;
#endif
using Polytoria.Datamodel;
using Polytoria.Datamodel.Resources;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Mesh = Godot.Mesh;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace Polytoria.Shared;

public sealed partial class Globals : Node
{
	public const string MainEndpoint = "https://polytoria.com/";
	public const string ApiEndpoint = "https://api.polytoria.com/";

	public const string ToolboxFolderName = "toolbox";
#if CREATOR
	public const string ProjectMetaFileName = "project.ptproj";
	public const string ProjectIndexName = "file-lock.json";
	public const string ProjectInputMapName = "input.json";
	public const string ModelFileExtension = "model";
	public static readonly string[] ScriptFileExtensions = ["lua", "luau"];
#endif

	public static Globals Singleton { get; private set; } = null!;

	public Globals()
	{
		Singleton = this;
	}

	public readonly static Dictionary<string, PackedScene> CachedScenes = [];
	private static FrozenDictionary<string, PackedScene> _scenesCache = null!;
#if CREATOR
	private static FrozenDictionary<string, PackedScene> _propertiesCache = null!;
	private static FrozenDictionary<string, PackedScene> _subViewPropertiesCache = null!;
#endif
	private static FrozenDictionary<string, Texture2D> _iconsCache = null!;
	private static FrozenDictionary<string, Texture2D> _uiIconsCache = null!;
	private static FrozenDictionary<string, (Mesh, Shape3D)> _shapesCache = null!;
	private static FrozenDictionary<string, Material> _materialsCache = null!;
	private static FrozenDictionary<string, Material> _skyboxesCache = null!;
	private static bool _isExiting = false;
	public const string BuiltInFontLocation = "res://assets/fonts/built-in";
	public const string BuiltInAudioLocation = "res://assets/audio/built-in";
	public const float MobileScale = 2.5f;
	public static string AppVersion { get; private set; } = "";
	public static string MajorAppVersion { get; private set; } = "2";
	public static Node? CurrentAppEntryNode { get; private set; }
	public static AppEntryEnum CurrentAppEntry { get; private set; }

	/// <summary>
	/// Determine RPC logging. "rpclog" can be set in feature flags to turn this on
	/// </summary>
	public static bool UseLogRPC { get; private set; } = false;
	/// <summary>
	/// Determine network stack trace logging in network errors, useful if you want to see where RPC was called from in the origin.
	/// "nettrace" can be set in feature flags to turn this on (only on the error issuer is needed). This do consume a portion of bandwidth
	/// </summary>
	public static bool UseNetTrace { get; private set; } = false;
	/// <summary>
	/// Determine no http mode, Can be used to disable http entirely
	/// "nohttp" can be set in feature flags to turn this on
	/// </summary>
	public static bool UseNoHttp { get; private set; } = false;
	/// <summary>
	/// Determine if node will be enabled, this can be disabled in non Godot environment (eg. unit tests)
	/// </summary>
	public static bool UseNodes { get; set; } = true;
	/// <summary>
	/// Check if is currently running inside Godot Editor
	/// </summary>
	public static bool IsInGDEditor { get; private set; } = false;
	/// <summary>
	/// Check if this build is a beta build
	/// </summary>
	public static bool IsBetaBuild { get; private set; } = false;
	/// <summary>
	/// Check if this build is a server build
	/// </summary>
	public static bool IsServerBuild { get; private set; } = false;
	/// <summary>
	/// Check if this build is a mobile build
	/// </summary>
	public static bool IsMobileBuild { get; private set; } = false;
	/// <summary>
	/// Check if Godot is available, this can be false in unit testing environments
	/// </summary>
	public static bool GDAvailable { get; private set; } = false;

	public static event Action? BeforeQuit;
	public static event Action<InputEvent>? GodotInputEvent;
	public static event Action<double>? GodotProcess;
	public static event Action<double>? GodotPhysicsProcess;
	public static event Action<int>? GodotNotification;

	private readonly static ConditionalWeakTable<string, Type> _typesCache = [];

	static Globals()
	{
		NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), DllImportResolver);

		// Register asset types
		// TODO: Maybe this could be automated via source generation?
		PTImageAsset.RegisterAsset();
		PTAudioAsset.RegisterAsset();
		PTMeshAsset.RegisterAsset();
		BuiltInAudioAsset.RegisterAsset();
		BuiltInFontAsset.RegisterAsset();
		FileLinkAsset.RegisterAsset();
		GradientImageAsset.RegisterAsset();
		PTMeshAnimationAsset.RegisterAsset();
	}

	public override void _EnterTree()
	{
		UseLogRPC = OS.HasFeature("rpclog");
		UseNetTrace = OS.HasFeature("nettrace");
		UseNoHttp = OS.HasFeature("nohttp");
		IsBetaBuild = OS.HasFeature("beta");
		IsServerBuild = OS.HasFeature("server");
		IsInGDEditor = OS.HasFeature("editor");
		IsMobileBuild = OS.HasFeature("mobile");

		GDAvailable = true;

		AppVersion = (string)ProjectSettings.GetSetting("application/config/version");

#if !PRODUCTION
		AppVersion += "+dev";
#endif

		PT.Print($"Polytoria v{AppVersion}");
		PT.Print("https://polytoria.com/");
		PT.Print("-- System Info --");
		PT.Print("OS Name: ", OS.GetName() + " " + OS.GetVersionAlias());
		PT.Print("Architecture: ", OS.GetProcessorName(), " cores: ", OS.GetProcessorCount());
		PT.Print("Video adapter: ", OS.GetVideoAdapterDriverInfo().Join(", "));
		PT.Print("----");

		GetTree().AutoAcceptQuit = false;
		GetTree().QuitOnGoBack = false;

		// Link with Polytoria's Private API Components
		// NOTE: If you wanted to implement your own, search for "MissingComponentException" to see which part requires it.
#if PT_PRIVATE_API
		Polytoria.Private.PrivateNode pv = new();
		AddChild(pv);
#endif

		// Initialize Native
		try
		{
			NativeInit.Init();
		}
		catch (Exception ex)
		{
			PT.PrintErr("Failure initializing native: ", ex);
		}

#if CREATOR
		string creatorPath = ProjectSettings.GlobalizePath("user://creator");
		if (!Directory.Exists(creatorPath))
		{
			Directory.CreateDirectory(creatorPath);
		}
#endif

		string scenesPath = "res://scenes/datamodel/";
#if CREATOR
		string propertiesPath = "res://scenes/creator/properties/";
		string subViewPropertiesPath = "res://scenes/creator/properties/subviews/";
		string iconsPath = "res://assets/textures/datamodel/";
#endif
		string meshesPath = "res://resources/shapes/meshes/";
		string materialsPath = "res://resources/materials/parts/";
		string skyboxesPath = "res://resources/materials/skyboxes/";
		string uiIconsPath = "res://assets/textures/ui-icons/";

		Dictionary<string, PackedScene> scenes = [];
		Dictionary<string, PackedScene> properties = [];
		Dictionary<string, PackedScene> subViewProperties = [];
		Dictionary<string, Texture2D> icons = [];
		Dictionary<string, (Mesh, Shape3D)> shapes = [];
		Dictionary<string, Material> materials = [];
		Dictionary<string, Material> skyboxes = [];
		Dictionary<string, Texture2D> uiIcons = [];

		foreach (string name in ResourceLoader.ListDirectory(scenesPath))
		{
			scenes[name[..^5]] = ResourceLoader.Load<PackedScene>(scenesPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}

		foreach (string name in ResourceLoader.ListDirectory(meshesPath))
		{
			string shapeName = name[..^5];
			Mesh mesh = ResourceLoader.Load<Mesh>(meshesPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
			Shape3D shape;

			if (shapeName == "Truss" || shapeName == "Frame")
			{
				shape = new BoxShape3D();
			}
			else
			{
				if (mesh is ArrayMesh)
				{
					ConcavePolygonShape3D concave = new();
					concave.SetFaces(mesh.GetFaces());
					shape = concave;
				}
				else
				{
					shape = mesh.CreateConvexShape();
				}

			}

			shapes[shapeName] = (
				mesh,
				shape
			);
		}

		foreach (string name in ResourceLoader.ListDirectory(materialsPath))
		{
			materials[name[..^5]] = ResourceLoader.Load<Material>(materialsPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}

		foreach (string name in ResourceLoader.ListDirectory(skyboxesPath))
		{
			skyboxes[name[..^5]] = ResourceLoader.Load<Material>(skyboxesPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}

		// Creator specific resources
#if CREATOR
		foreach (string name in ResourceLoader.ListDirectory(propertiesPath))
		{
			if (!name.EndsWith(".tscn")) { continue; }
			properties[name[..^13]] = ResourceLoader.Load<PackedScene>(propertiesPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}

		foreach (string name in ResourceLoader.ListDirectory(subViewPropertiesPath))
		{
			subViewProperties[name[..^12]] = ResourceLoader.Load<PackedScene>(subViewPropertiesPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}

		foreach (string name in ResourceLoader.ListDirectory(iconsPath))
		{
			icons[name[..^4]] = ResourceLoader.Load<Texture2D>(iconsPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}
#endif

		foreach (string name in ResourceLoader.ListDirectory(uiIconsPath))
		{
			uiIcons[name[..^4]] = ResourceLoader.Load<Texture2D>(uiIconsPath + name, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
		}

		_scenesCache = scenes.ToFrozenDictionary();
#if CREATOR
		_propertiesCache = properties.ToFrozenDictionary();
		_subViewPropertiesCache = subViewProperties.ToFrozenDictionary();
#endif
		_iconsCache = icons.ToFrozenDictionary();
		_shapesCache = shapes.ToFrozenDictionary();
		_materialsCache = materials.ToFrozenDictionary();
		_skyboxesCache = skyboxes.ToFrozenDictionary();
		_uiIconsCache = uiIcons.ToFrozenDictionary();
	}

	public override void _Process(double delta)
	{
		if (_isExiting) return;
		GodotProcess?.Invoke(delta);
		base._Process(delta);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isExiting) return;
		GodotPhysicsProcess?.Invoke(delta);
		base._PhysicsProcess(delta);
	}

	public override void _Input(InputEvent @event)
	{
		GodotInputEvent?.Invoke(@event);
		base._Input(@event);
	}

	public static T LoadInstance<T>(World? root = null) where T : Instance
	{
		return (T)LoadNetworkedObject(typeof(T).Name, root)!;
	}

	public static T? LoadInstance<T>(string className, World? root = null) where T : Instance
	{
		return (T?)LoadNetworkedObject(className, root);
	}

	[return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
	private static Type? GetTypeByName(string className)
	{
		if (_typesCache.TryGetValue(className, out Type? t))
			return t;

		string[] namespacesToCheck =
		[
			"Polytoria.Datamodel.",
		"Polytoria.Datamodel.Services.",
		"Polytoria.Datamodel.Creator.",
		"Polytoria.Datamodel.Resources.",
	];

		foreach (string ns in namespacesToCheck)
		{
			t = Type.GetType(ns + className);
			if (t != null)
			{
				_typesCache.AddOrUpdate(className, t);
				return t;
			}
		}
		return null;
	}

	public static NetworkedObject? LoadNetworkedObject(string className, World? root = null)
	{
		Type? type = GetTypeByName(className);
		if (type != null)
		{
			object? obj = Activator.CreateInstance(type);
			if (obj is NetworkedObject netObj)
			{
				netObj.NameOverride = className;
				netObj.Root = root!;
				return netObj;
			}
		}

		return null;
	}

	public static Node? LoadNetworkedObjectScene(string className)
	{
		Node? scene = _scenesCache.GetValueOrDefault(className)?.Instantiate<Node>();
		scene?.SceneFilePath = "";
		return scene;
	}

#if CREATOR
	public static IProperty LoadProperty(Type type)
	{
		string cacheToLoad = type.IsEnum ? "Enum" : type.Name;
		if (type.IsAssignableTo(typeof(BaseAsset)))
		{
			cacheToLoad = "BaseAsset";
		}
		else if (type.IsAssignableTo(typeof(Instance)))
		{
			cacheToLoad = "Instance";
		}
		return _propertiesCache[cacheToLoad].Instantiate<IProperty>();
	}

	public static IPropertySubview? LoadSubviewProperty(Type type)
	{
		string cacheToLoad = type.Name;

		if (_subViewPropertiesCache.TryGetValue(cacheToLoad, out var cachedValue))
		{
			return cachedValue.Instantiate<IPropertySubview>();
		}

		return null;
	}
#endif

	public static Texture2D LoadIcon(string className)
	{
		return _iconsCache.GetValueOrDefault(className, _iconsCache["Unknown"]);
	}

	public static Texture2D LoadUIIcon(string iconName)
	{
		return _uiIconsCache.GetValueOrDefault(iconName, _uiIconsCache["empty"]);
	}

	public static (Mesh, Shape3D) LoadShape(string shapeName)
	{
		return _shapesCache[shapeName];
	}

	public static Material LoadMaterial(string materialName)
	{
		return (Material)_materialsCache[materialName].Duplicate();
	}

	public static Material LoadSkybox(string materialName)
	{
		return _skyboxesCache[materialName];
	}

	public static Dictionary<string, string> ReadCmdArgs()
	{
		Dictionary<string, string> result = [];
		string[] args = OS.GetCmdlineArgs();

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];

			if (arg.StartsWith('-'))
			{
				string key = arg.TrimStart('-');
				string value = "";

				// If next arg exists and is not another flag, treat it as value
				if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
				{
					value = args[i + 1];
					i++;
				}

				result[key] = value;
			}
		}

		return result;
	}

	public override void _Notification(int what)
	{
		GodotNotification?.Invoke(what);
		if (what == NotificationWMCloseRequest)
		{
			Quit();
		}
		base._Notification(what);
	}

	public async Task WaitAsync(float time)
	{
		await ToSignal(GetTree().CreateTimer(time), "timeout");
	}

	public async Task WaitFrame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	public async void Quit(bool force = false)
	{
#if CREATOR
		// Request confirmation from interface
		if (CreatorService.Interface != null && force)
		{
			if (!await CreatorService.Interface.OnQuitRequested()) return;
		}
#endif

		// Starts quit the app
		_isExiting = true;

		try
		{
			BeforeQuit?.Invoke();
		}
		catch (Exception ex)
		{
			PT.PrintWarn("Error present when quitting: ", ex);
		}
		Callable.From(() =>
		{
			CurrentAppEntryNode?.QueueFree();
			Callable.From(() =>
			{
				GetTree().Quit();
			}).CallDeferred();
		}).CallDeferred();
	}

	public enum AppEntryEnum
	{
		Client,
		Creator,
		MobileUI,
		Renderer
	}

	public Node SwitchEntry(AppEntryEnum appEntry)
	{
		PT.Print("Switching entry to: ", appEntry);
		CurrentAppEntryNode?.QueueFree();
		CurrentAppEntry = appEntry;
		Node node = LoadEntry(appEntry);
		CurrentAppEntryNode = node;
		GetNode("/root/").AddChild(node);
		return node;
	}

	public static Node LoadEntry(AppEntryEnum appEntry)
	{
		string sceneToLoad = appEntry switch
		{
			AppEntryEnum.Client => "res://scenes/client/client.tscn",
			AppEntryEnum.Creator => "res://scenes/creator/creator.tscn",
			AppEntryEnum.MobileUI => "res://scenes/mobile/mobile.tscn",
			AppEntryEnum.Renderer => "res://scenes/renderer/renderer.tscn",
			_ => "res://scenes/client/client.tscn",
		};
		string? iconToLoad = appEntry switch
		{
			AppEntryEnum.Client => "client",
			AppEntryEnum.Creator => "creator",
			_ => null
		};

		// Set app icon
		if (iconToLoad != null)
		{
			string platform = "windows";

			if (OS.HasFeature("macos"))
			{
				platform = "mac";
			}

			if (OS.HasFeature("linux"))
			{
				platform = "linux";
			}

			string iconPath = $"res://assets/textures/logo/{iconToLoad}/{platform}.png";
			DisplayServer.SetIcon(GD.Load<Image>(iconPath));
		}

		PT.Print(appEntry, ": Loading Entry scene");
		Node node = CreateInstanceFromScene<Node>(sceneToLoad);
		return node;
	}

	private static IntPtr DllImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (!IsInGDEditor)
		{
			return IntPtr.Zero;
		}

		if (IsMobileBuild)
		{
			// Use the mobile default resolver
			return IntPtr.Zero;
		}

		if (!OS.HasFeature("x86_64"))
		{
			if (IsInGDEditor)
			{
				PT.PrintWarn("Unsupported platform for development");
			}
			return IntPtr.Zero;
		}

		string platform = ResolveCurrentPlatform();
		string? dllPath = ResolveDllPath(libraryName, platform);

		if (dllPath == null)
		{
			return IntPtr.Zero;
		}

		return NativeLibrary.Load(dllPath, assembly, searchPath);
	}

	internal static string ResolveCurrentPlatform()
	{
		string platform;

		if (OS.HasFeature("windows"))
		{
			platform = "windows";
		}
		else if (OS.HasFeature("macos"))
		{
			platform = "macos";
		}
		else if (OS.HasFeature("android"))
		{
			platform = "android";
		}
		else
		{
			platform = "linux";
		}

		return platform;
	}

	internal static string? ResolveDllPath(string libraryName, string platform)
	{
		Dictionary<string, string> platformExtensions = new()
		{
			["windows"] = "dll",
			["macos"] = "dylib",
			["linux"] = "so"
		};

		Dictionary<string, string> libraryPaths = new()
		{
			["discord_game_sdk"] = "native/discord",
			["Luau.Compiler"] = "native/Luau.Compiler",
			["Luau.VM"] = "native/Luau.VM",
		};

		if (!libraryPaths.TryGetValue(libraryName, out string? pathb))
		{
			return null;
		}

		if (!IsInGDEditor)
		{
			return $"{libraryName}.{platformExtensions[platform]}";
		}

		if (IsServerBuild)
		{
			return $"native/{platform}/{libraryName}.{platformExtensions[platform]}";
		}
		else
		{
			return $"{pathb}/{platform}/{libraryName}.{platformExtensions[platform]}";
		}
	}

	// Workaround for instance create
	public static T CreateInstanceFromScene<T>(string path) where T : Node
	{
		if (CachedScenes.ContainsKey(path) == false)
		{
			CachedScenes[path] = ResourceLoader.Load<PackedScene>(path, null, ResourceLoader.CacheMode.IgnoreDeep);
		}
		return CachedScenes[path].Instantiate<T>();
	}

	[JsonSerializable(typeof(string))]
	[JsonSerializable(typeof(bool))]
	[JsonSerializable(typeof(int))]
	[JsonSerializable(typeof(object))]
	internal partial class GenericJsonContext : JsonSerializerContext { }
}

public class MissingComponentException(string msg) : Exception(msg) { }
