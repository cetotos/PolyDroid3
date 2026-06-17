// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Client;

public static class UsernameStore
{
	private const string PathConst = "user://username.cfg";
	private const string Section = "user";
	private const string UsernameKey = "username";
	private const string UserIdKey = "user_id";

	public static string Username
	{
		get
		{
			ConfigFile cfg = new();
			if (cfg.Load(PathConst) != Error.Ok) return "";
			return cfg.GetValue(Section, UsernameKey, "").AsString();
		}
		set
		{
			ConfigFile cfg = new();
			cfg.Load(PathConst);
			cfg.SetValue(Section, UsernameKey, value);
			cfg.Save(PathConst);
		}
	}

	public static int UserId
	{
		get
		{
			ConfigFile cfg = new();
			if (cfg.Load(PathConst) != Error.Ok) return 0;
			return cfg.GetValue(Section, UserIdKey, 0).AsInt32();
		}
		set
		{
			ConfigFile cfg = new();
			cfg.Load(PathConst);
			cfg.SetValue(Section, UserIdKey, value);
			cfg.Save(PathConst);
		}
	}
}
