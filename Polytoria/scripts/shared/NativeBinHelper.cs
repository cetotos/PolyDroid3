// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.
using Godot;
using Polytoria.Utils;
using System;
using System.Runtime.InteropServices;

namespace Polytoria.Shared;

public static partial class NativeBinHelper
{
	public const string LuaLSEditorExecutablePath = "res://native/luau-lsp/";
	[LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
	private static partial int chmod(string pathname, int mode);

	public static void Init()
	{
#if CREATOR && GODOT_LINUXBSD
		InitLinuxCreator();
#elif CREATOR && GODOT_MACOS
		InitMacOSCreator();
#endif
	}

	private static void InitLinuxCreator()
	{
#if CREATOR
		int ret = chmod(ResolveLuauLspBinPath(), 0x755);
		if (ret != 0)
		{
			throw new System.Exception("Linux permission set failure: Code " + ret);
		}
#endif
	}

	private static void InitMacOSCreator()
	{
#if CREATOR
		int ret = chmod(ResolveLuauLspBinPath(), 0x755);
		if (ret != 0)
		{
			throw new System.Exception("macOS permission set failure: Code " + ret);
		}
#endif
	}

	internal static string ResolveLuauLspBinPath()
	{
		string basePath;
		string? exeName = null;

		if (Globals.IsInGDEditor)
		{
			basePath = LuaLSEditorExecutablePath;
		}
		else
		{
			basePath = OS.GetExecutablePath().GetBaseDir();
		}

		if (OS.HasFeature("windows"))
		{
			exeName = "luau-lsp.exe";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("windows");
		}
		else if (OS.HasFeature("macos"))
		{
			exeName = "luau-lsp";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("macos");
			else
				// luau-lsp live alongside Polytoria.app, go up 3 levels to reach the folder containing the .app
				// NOTE: HACKY!!!!!!!!!!!!!!!!!!!!
				basePath = basePath.GetBaseDir().GetBaseDir().GetBaseDir();
		}
		else if (OS.HasFeature("linux"))
		{
			exeName = "luau-lsp";
			if (Globals.IsInGDEditor)
				basePath = basePath.PathJoin("linux");
		}

		if (exeName == null) throw new Exception("Unsupported platform for luau-lsp");

		string exePath = basePath.PathJoin(exeName);
		string exePathGlobal = ProjectSettings.GlobalizePath(exePath).SanitizePath();
		return exePathGlobal;
	}
}
