// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Polytoria.Shared;

public static class ProcessUtil
{
	public static int Spawn(string exePath, IEnumerable<string> args)
	{
		ProcessStartInfo startInfo = new()
		{
			FileName = exePath,
			UseShellExecute = false,
			CreateNoWindow = true,
		};

		foreach (string arg in args)
		{
			startInfo.ArgumentList.Add(arg);
		}

		try
		{
			Process process = Process.Start(startInfo)!;
			return process.Id;
		}
		catch (Exception ex)
		{
			GD.PushError($"Failed to spawn process '{exePath}'! {ex.Message}");
			return -1;
		}
	}
}
