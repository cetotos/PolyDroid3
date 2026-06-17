// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client;
#if DEBUG
using System;
using Polytoria.DatamodelTest;
#endif
using Polytoria.DocsGen;
using System.Collections.Generic;
using static Polytoria.Shared.Globals;

namespace Polytoria.Shared;

public partial class AppEntry : Node
{
	public async override void _Ready()
	{
		Dictionary<string, string> cmdargs = ReadCmdArgs();
		bool isApiRefGen = cmdargs.ContainsKey("genapi");
		bool isCreator = cmdargs.ContainsKey("creator");
		bool isLtChild = cmdargs.ContainsKey("ltchild");
		bool isSolo = cmdargs.ContainsKey("solo");

		bool wantsCreator = isCreator || OS.HasFeature("creator");
		try { ApplyStartupRenderingMethod(wantsCreator); }
		catch (Polytoria.Shared.RenderingDeviceSwitcher.SwitchingRenderingDeviceException) { return; }

		if (cmdargs.TryGetValue("wait", out string? waitTime))
		{
			await Singleton.WaitAsync(float.Parse(waitTime));
		}

		if (!cmdargs.ContainsKey("noxr"))
		{
			XRBootstrap.TryEnable(GetViewport());
		}

		if (isApiRefGen && IsInGDEditor)
		{
			PT.Print("Generating references...");
			APIReferenceGenerator.GenerateRefFile();
			PT.Print("Completed! Exiting...");
			Globals.Singleton.Quit();
			return;
		}

#if DEBUG
		// Datamodel test block
		bool isDMTest = cmdargs.ContainsKey("dmtest");
		if (isDMTest)
		{
			DatamodelTestEntry dt = new();
			AddChild(dt);
			try
			{
				dt.Entry();
			}
			catch (Exception ex)
			{
				PT.PrintErr(ex);
				Singleton.Quit(force: true, code: 1);
			}
			return;
		}
#endif

		AppEntryEnum entry = AppEntryEnum.Client;
		if (OS.HasFeature("client"))
		{
			entry = AppEntryEnum.Client;
		}
		if (OS.HasFeature("creator") || isCreator)
		{
			entry = AppEntryEnum.Creator;
		}
		if (OS.HasFeature("mobile-ui"))
		{
			bool hasDeeplinkLaunch = cmdargs.ContainsKey("token")
				|| cmdargs.ContainsKey("code")
				|| cmdargs.ContainsKey("state");
			if (OS.HasFeature("legacy-mobile-ui") || hasDeeplinkLaunch || !XRBootstrap.IsActive)
			{
				entry = AppEntryEnum.MobileUI;
			}
			else
			{
				entry = AppEntryEnum.MainMenu;
			}
		}
		if (OS.HasFeature("renderer"))
		{
			entry = AppEntryEnum.Renderer;
		}

		bool isDesktopClient = entry == AppEntryEnum.Client
			&& !OS.HasFeature("mobile-ui")
			&& !OS.HasFeature("mobile")
			&& !OS.HasFeature("server")
			&& !Globals.IsServerBuild
			&& !isCreator;
		bool hasGameLaunchArgs = cmdargs.ContainsKey("address")
			|| isSolo
			|| isLtChild
			|| cmdargs.ContainsKey("token")
			|| cmdargs.ContainsKey("code")
			|| cmdargs.ContainsKey("state")
			|| cmdargs.ContainsKey("server")
			|| cmdargs.ContainsKey("child");
		if (isDesktopClient && !hasGameLaunchArgs)
		{
			entry = XRBootstrap.IsActive ? AppEntryEnum.MainMenu : AppEntryEnum.MobileUI;
		}

		if (isSolo)
		{
			entry = AppEntryEnum.Client;
		}

		if (isLtChild)
		{
			entry = AppEntryEnum.Client;
		}

		if (cmdargs.ContainsKey("address"))
		{
			entry = AppEntryEnum.Client;
		}

		Callable.From(() =>
		{
			Node app = Globals.Singleton.SwitchEntry(entry);
			if (app is ClientEntry ce)
			{
				ce.Entry();
			}
			QueueFree();
		}).CallDeferred();
	}

	private const string ClientSettingsPath = "user://settings_client.json";
	private const string CreatorSettingsPath = "user://creator/creator_settings.json";

	private static void ApplyStartupRenderingMethod(bool wantsCreator)
	{
		if (IsMobileBuild || IsServerBuild || IsInGDEditor) return;
		string path = wantsCreator ? CreatorSettingsPath : ClientSettingsPath;
		if (!Godot.FileAccess.FileExists(path)) return;

		string json = Godot.FileAccess.GetFileAsString(path);
		if (string.IsNullOrEmpty(json)) return;

		Polytoria.Shared.Settings.RenderingMethodOption method = Polytoria.Shared.Settings.RenderingMethodOption.Auto;
		Polytoria.Shared.Settings.GraphicsApiOption api = Polytoria.Shared.Settings.GraphicsApiOption.Auto;
		using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
		{
			if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
			if (doc.RootElement.TryGetProperty(Polytoria.Shared.Settings.SharedSettingKeys.Graphics.RenderingMethod, out System.Text.Json.JsonElement methodEl)
				&& methodEl.ValueKind == System.Text.Json.JsonValueKind.String
				&& System.Enum.TryParse(methodEl.GetString(), true, out Polytoria.Shared.Settings.RenderingMethodOption parsedMethod))
				method = parsedMethod;
			if (doc.RootElement.TryGetProperty(Polytoria.Shared.Settings.SharedSettingKeys.Graphics.GraphicsApi, out System.Text.Json.JsonElement apiEl)
				&& apiEl.ValueKind == System.Text.Json.JsonValueKind.String
				&& System.Enum.TryParse(apiEl.GetString(), true, out Polytoria.Shared.Settings.GraphicsApiOption parsedApi))
				api = parsedApi;
		}

		Polytoria.Shared.RenderingDeviceSwitcher.Apply(method, api);
	}
}
