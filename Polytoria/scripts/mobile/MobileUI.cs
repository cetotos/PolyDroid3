// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DeepLinkAddon;
using Godot;
using Polytoria.Client;
using Polytoria.Mobile.UI;
using Polytoria.Mobile.Utils;
using Polytoria.Schemas.API;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;

namespace Polytoria.Mobile;

public partial class MobileUI : Control
{
	public static MobileUI Singleton { get; private set; } = null!;
	public MobileUI()
	{
		Singleton = this;
	}

	public event Action<MobileViewEnum>? ViewPathSwitched;

	private Control _mainView = null!;
	public MobileViewBase? CurrentViewNode;
	public MobileViewEnum CurrentView;

	[Export] public StartupSplash? StartSplash { get; private set; }
	[Export] public NewUserSplash NewUserSplash = null!;
	[Export] public MobileLoadingScreen LoadingScreen = null!;

	private Deeplink _deepLink = new();
	private readonly Dictionary<MobileViewEnum, MobileViewBase> _viewCache = [];

	public override void _Ready()
	{
		Dictionary<string, string> cmdargs = Globals.ReadCmdArgs();
		cmdargs.TryGetValue("token", out string? mobileToken);
		cmdargs.TryGetValue("code", out string? mobileCode);
		cmdargs.TryGetValue("state", out string? mobileState);

		AddChild(_deepLink, true);

		GetTree().Root.ContentScaleFactor = (Globals.IsMobileBuild ? Globals.MobileScale : 1f) * MobileSettingsStore.UiScale;

		var initResult = _deepLink.Initialize();

		_deepLink.DeeplinkReceived += OnDeeplinkReceived;

		if (Globals.IsMobileBuild)
		{
			if (Engine.HasSingleton("PolytoriaPermissions"))
			{
				GodotObject perms = Engine.GetSingleton("PolytoriaPermissions");
				if (!(bool)perms.Call("hasAllFilesAccess"))
				{
					perms.Call("requestAllFilesAccess");
				}
			}
		}

		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		if (StartSplash != null)
		{
			StartSplash!.Visible = true;
		}

		PolyMobileAuthAPI.UserAuthenticated += OnUserAuthenticated;
		PolyMobileAuthAPI.AskForAuthentication += OnAskForAuthentication;

		PolyMobileAuthAPI.SetupClient();
		if (mobileToken != null)
		{
			_ = PolyMobileAuthAPI.LoginWithAuthToken(mobileToken);
		}

		if (mobileCode != null && mobileState != null)
		{
			_ = PolyMobileAuthAPI.LoginWithCodeAndState(mobileCode, mobileState);
		}

		ApplyThemeColor();
		MobileSettingsStore.ThemeColorChanged += ApplyThemeColor;

		_mainView = GetNode<Control>("Layout/MainView");
		if (Globals.IsMobileBuild)
		{
			DisplayServer.ScreenOrientation orientation = Globals.IsTablet
				? DisplayServer.ScreenOrientation.Landscape
				: DisplayServer.ScreenOrientation.Portrait;
			DisplayServer.ScreenSetOrientation(orientation);
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}

		bool ownsWindow = !Polytoria.Shared.XRBootstrap.IsActive && GetViewport() == GetTree().Root;
		if (ownsWindow)
		{
			if (Globals.IsInGDEditor)
			{
				DisplayServer.WindowSetSize((Vector2I)new Vector2(412, 700));
			}
			else if (!Globals.IsMobileBuild)
			{
				DisplayServer.WindowSetSize(new Vector2I(900, 1000));
			}
		}

		SwitchTo(MobileViewEnum.Home);

		_ = DismissSplash();
	}

	private async System.Threading.Tasks.Task DismissSplash()
	{
		await ToSignal(GetTree().CreateTimer(2.5), SceneTreeTimer.SignalName.Timeout);
		HideStartupSplash();
	}

	public override void _ExitTree()
	{
		PolyMobileAuthAPI.UserAuthenticated -= OnUserAuthenticated;
		PolyMobileAuthAPI.AskForAuthentication -= OnAskForAuthentication;
		_deepLink.DeeplinkReceived -= OnDeeplinkReceived;
		MobileSettingsStore.ThemeColorChanged -= ApplyThemeColor;
		base._ExitTree();
	}

	private void ApplyThemeColor()
	{
		StyleBoxFlat sb = new() { BgColor = MobileSettingsStore.GetBgColor() };
		AddThemeStyleboxOverride("panel", sb);
	}

	private void OnUserAuthenticated(APIMeResponse me)
	{
		HideStartupSplash();
		if (NewUserSplash != null && IsInstanceValid(NewUserSplash))
		{
			NewUserSplash.Visible = false;
		}
	}

	private void OnAskForAuthentication()
	{
		HideStartupSplash();
	}

	private void HideStartupSplash()
	{
		StartSplash?.HideSplash();
		StartSplash = null;
	}

	private async void OnDeeplinkReceived(DeeplinkURL url)
	{
		// Handle polytoria://auth link
		if (url.Host == "auth")
		{
			NameValueCollection authQuery = HttpUtility.ParseQueryString(url.Query);
			string code = authQuery.Get("code")!;
			string state = authQuery.Get("state")!;

			LoadingScreen.ShowScreen();
			await PolyMobileAuthAPI.LoginWithCodeAndState(code, state);
			LoadingScreen.HideScreen();
		}

		if (url.Host == "client" || url.Host == "clientbeta")
		{
			string token = url.Path.TrimStart('/');
			if (string.IsNullOrEmpty(token))
			{
				PT.PrintErr($"clientbeta deeplink missing token: {url}");
				return;
			}
			LaunchClientWithToken(token);
		}

		if (url.Host == "test")
		{
			NameValueCollection q = HttpUtility.ParseQueryString(url.Query);
			string? worldPath = q.Get("world");
			string? entryPath = q.Get("entry");
			string? debugID = q.Get("debug");
			int port = int.TryParse(q.Get("port"), out int p) ? p : 24221;
			if (string.IsNullOrEmpty(worldPath))
			{
				PT.PrintErr($"test deeplink missing world: {url}");
				return;
			}
			LaunchInProcessSoloTest(worldPath!, entryPath, debugID, port);
		}
	}

	private void LaunchInProcessSoloTest(string worldPath, string? entryPath, string? debugID, int port)
	{
		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ce.Entry(new ClientEntry.ClientEntryData
			{
				TestWorldPath = worldPath,
				TestEntryPath = entryPath,
				TestIsServer = true,
				TestIsSolo = true,
				TestDebugID = debugID,
				ConnectPort = port,
			});
		}
	}

	public void LaunchLocalServer(string worldPath)
	{
		if (Globals.IsMobileBuild)
		{
			LaunchInProcessSoloTest(worldPath, null, null, 24221);
			return;
		}

		int port = (int)GD.RandRange(20000, 30000);
		List<string> args =
		[
			"--headless",
			"--log-file", "user://logs/local_server.log",
			"-solo", ProjectSettings.GlobalizePath(worldPath),
			"-port", port.ToString(),
			"-subworld",
			"--rendering-method", RenderingDeviceSwitcher.GetCurrentDriverName(),
			"-rmswignore"
		];
		if (Globals.IsInGDEditor)
		{
			args.InsertRange(0, ["--path", ProjectSettings.GlobalizePath("res://")]);
		}

		int serverPid = OS.CreateProcess(OS.GetExecutablePath(), [.. args]);
		if (serverPid <= 0)
		{
			PT.PrintErr($"Local server: failed to launch server!");
			return;
		}

		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ce.Entry(new ClientEntry.ClientEntryData
			{
				ConnectAddress = "127.0.0.1",
				ConnectPort = port,
				TestServerPid = serverPid,
			});
		}
	}

	public async void LaunchGame(int placeID)
	{
		try
		{
			LoadingScreen?.ShowScreen();
			(bool ready, int port, string? error) = await PolyMobileAuthAPI.WakePlace(placeID);
			LoadingScreen?.HideScreen();
			if (!ready)
			{
				string text = error switch
				{
					null or "" => "Cannot connect to that place.",
					"timeout" => "The server is taking too long to start. Try again.",
					"not_found" or "bad_id" => "That place doesn't exist on the server.",
					"spawn_failed" => "The server failed to start this place.",
					_ => error,
				};
				ShowErrorDialog(text);
				return;
			}
			Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
			if (app is ClientEntry ce)
			{
				ce.Entry(new ClientEntry.ClientEntryData
				{
					ConnectAddress = Globals.GameServerHost,
					ConnectPort = port,
				});
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr("LaunchGame failed for placeID=", placeID, ": ", ex);
			LoadingScreen?.HideScreen();
			ShowErrorDialog("Could not join: " + ex.Message);
		}
	}

	private void ShowErrorDialog(string message)
	{
		AcceptDialog dialog = new()
		{
			DialogText = message,
			Title = "Couldn't join",
			Exclusive = true,
		};
		dialog.Confirmed += () => dialog.QueueFree();
		dialog.Canceled += () => dialog.QueueFree();
		AddChild(dialog);
		dialog.PopupCentered();
	}

	private void LaunchClientWithToken(string token)
	{
		Node app = Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.Client);
		if (app is ClientEntry ce)
		{
			ce.Entry(new ClientEntry.ClientEntryData { Token = token });
		}
	}

	private const double ViewFadeSeconds = 0.16;

	public void SwitchTo(MobileViewEnum viewEnum, object? args = null)
	{
		if (viewEnum == CurrentView)
		{
			return;
		}

		MobileViewBase? outgoing = CurrentViewNode;

		// Check if cached
		if (!_viewCache.TryGetValue(viewEnum, out MobileViewBase? page))
		{
			PT.Print("Loading ", viewEnum);
			string pathToLoad = viewEnum switch
			{
				MobileViewEnum.Home => "res://scenes/mobile/views/home.tscn",
				MobileViewEnum.Worlds => "res://scenes/mobile/views/worlds.tscn",
				MobileViewEnum.PlaceInfo => "res://scenes/mobile/views/place_info.tscn",
				MobileViewEnum.Settings => "res://scenes/mobile/views/settings.tscn",
				_ => throw new ArgumentOutOfRangeException(nameof(viewEnum),
					 $"No scene defined for {viewEnum}")
			};

			PT.Print("Loading ", viewEnum);

			PackedScene packed = ResourceLoader.Load<PackedScene>(pathToLoad, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
			page = packed.Instantiate<MobileViewBase>();
			_viewCache[viewEnum] = page;
			_mainView.AddChild(page);
			page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}

		CurrentViewNode = page;
		CurrentView = viewEnum;
		page.ShowView(args);

		if (outgoing != null && outgoing != page)
		{
			MobileViewBase fadingOut = outgoing;
			Tween outTween = CreateTween();
			outTween.TweenProperty(fadingOut, "modulate:a", 0f, ViewFadeSeconds);
			outTween.TweenCallback(Callable.From(() =>
			{
				fadingOut.HideView();
				fadingOut.Visible = false;
				fadingOut.Modulate = new Color(1, 1, 1, 1);
			}));
		}

		page.Modulate = new Color(1, 1, 1, 0f);
		page.Visible = true;
		Tween inTween = CreateTween();
		inTween.TweenProperty(page, "modulate:a", 1f, ViewFadeSeconds);

		ViewPathSwitched?.Invoke(viewEnum);
	}
}

public enum MobileViewEnum
{
	None,
	Home,
	Worlds,
	Settings,
	PlaceInfo
}
