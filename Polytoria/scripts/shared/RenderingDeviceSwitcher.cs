using Godot;
using System;
using System.Collections.Generic;

namespace Polytoria.Shared;

internal static class RenderingDeviceSwitcher
{
	public static void Switch(RenderingDeviceEnum to)
	{
		string renderingName = GetRenderingName(to);
		string currentMethod = RenderingServer.GetCurrentRenderingMethod();
		if (currentMethod == renderingName)
		{
			// already using this rendering, nothing to do
			return;
		}

		List<string> args = [.. System.Environment.GetCommandLineArgs()];

		if (args.Contains("--rendering-method"))
		{
			// Already tried switching rendering method, but godot may have denied it, let's just go with that anyways
			return;
		}

		args.AddRange("--rendering-method", renderingName);

		string exePath = OS.GetExecutablePath();
		OS.CreateProcess(exePath, [.. args]);
		Globals.Singleton.Quit(force: true);
		throw new Exception("Switching rendering device");
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

	public enum RenderingDeviceEnum
	{
		Forward,
		Mobile,
		GLCompatibility
	}
}
