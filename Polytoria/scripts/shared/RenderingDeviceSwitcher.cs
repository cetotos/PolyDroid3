// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared.Settings;
using System;
using System.Collections.Generic;

namespace Polytoria.Shared;

public static class RenderingDeviceSwitcher
{
	public static RenderingDeviceEnum FromRenderingMethodOption(RenderingMethodOption option)
	{
		return option switch
		{
			RenderingMethodOption.Standard => RenderingDeviceEnum.Forward,
			RenderingMethodOption.Performance => RenderingDeviceEnum.Mobile,
			RenderingMethodOption.Compatibility => RenderingDeviceEnum.GLCompatibility,
			RenderingMethodOption.Auto => throw new ArgumentException("Auto does not map to rendering device"),
			_ => RenderingDeviceEnum.Forward
		};
	}

	public static void Switch(RenderingMethodOption option)
	{
		Apply(option, GraphicsApiOption.Auto);
	}

	public static void Apply(RenderingMethodOption method, GraphicsApiOption api)
	{
		// Mobile are locked to one renderer only, don't change
		if (Globals.IsMobileBuild) return;
		if (Globals.IsInGDEditor) return;

		string? desiredMethod = method == RenderingMethodOption.Auto
			? null
			: GetRenderingName(FromRenderingMethodOption(method));
		string? desiredDriver = GetDriverName(method, api);

		bool methodMismatch = desiredMethod != null && RenderingServer.GetCurrentRenderingMethod() != desiredMethod;
		bool driverMismatch = desiredDriver != null && !string.Equals(RenderingServer.GetCurrentRenderingDriverName(), desiredDriver, StringComparison.OrdinalIgnoreCase);

		if (!methodMismatch && !driverMismatch) return;

		string[] args = OS.GetCmdlineArgs();

		if (args.Contains("-rmswignore"))
		{
			// Already switched, but godot may have refused it. let's just go with that anyways
			return;
		}

		// relaunch ourselves with the right renderer then bail. the throw isn't a real error
		OS.CreateProcess(OS.GetExecutablePath(), GetRestartArgs(args, desiredMethod, desiredDriver));

		Globals.Singleton.Quit(force: true);
		throw new SwitchingRenderingDeviceException();
	}

	private static string? GetDriverName(RenderingMethodOption method, GraphicsApiOption api)
	{
		if (method == RenderingMethodOption.Compatibility) return null;
		return api switch
		{
			GraphicsApiOption.Vulkan => "vulkan",
			GraphicsApiOption.Direct3D12 => "d3d12",
			_ => null
		};
	}

	private static string[] GetRestartArgs(string[] args, string? renderingName, string? driverName)
	{
		List<string> filtered = [];

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];

			if (arg == "--rendering-method" || arg == "--rendering-driver")
			{
				if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
					i++;

				continue;
			}

			if (arg == "-rmswignore")
				continue;

			filtered.Add(arg);
		}

		if (renderingName != null)
		{
			filtered.Add("--rendering-method");
			filtered.Add(renderingName);
		}
		if (driverName != null)
		{
			filtered.Add("--rendering-driver");
			filtered.Add(driverName);
		}
		filtered.Add("-rmswignore");

		return [.. filtered];
	}

	public static string GetCurrentDriverName()
	{
		return RenderingServer.GetCurrentRenderingMethod();
	}

	public static string GetRenderingName(RenderingDeviceEnum e)
	{
		return e switch
		{
			RenderingDeviceEnum.Forward => "forward_plus",
			RenderingDeviceEnum.Mobile => "mobile",
			RenderingDeviceEnum.GLCompatibility => "gl_compatibility",
			_ => throw new IndexOutOfRangeException()
		};
	}

	public class SwitchingRenderingDeviceException : Exception { }

	public enum RenderingDeviceEnum
	{
		Forward,
		Mobile,
		GLCompatibility
	}
}
