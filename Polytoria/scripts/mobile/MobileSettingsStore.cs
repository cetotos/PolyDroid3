// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Mobile;

public enum MobileThemeColor
{
	Dark = 0,
	Blue = 1,
}

public static class MobileSettingsStore
{
	private const string PathConst = "user://mobile_settings.cfg";
	private const string Section = "ui";
	private const string ThemeColorKey = "theme_color";
	private const string UiScaleKey = "ui_scale";

	public static event Action? ThemeColorChanged;

	public static Color GetBgColor()
	{
		return ThemeColor switch
		{
			MobileThemeColor.Blue => new Color(0.12941177f, 0.24313726f, 0.38039216f, 1f),
			_ => new Color(0.02745098f, 0.047058824f, 0.07450981f, 1f),
		};
	}

	public static float UiScale
	{
		get
		{
			ConfigFile cfg = new();
			if (cfg.Load(PathConst) != Error.Ok) return 1f;
			float v = cfg.GetValue(Section, UiScaleKey, 1f).AsSingle();
			return Mathf.Clamp(v, 0.5f, 2f);
		}
		set
		{
			ConfigFile cfg = new();
			cfg.Load(PathConst);
			cfg.SetValue(Section, UiScaleKey, value);
			cfg.Save(PathConst);
		}
	}

	public static MobileThemeColor ThemeColor
	{
		get
		{
			ConfigFile cfg = new();
			if (cfg.Load(PathConst) != Error.Ok) return MobileThemeColor.Dark;
			int v = cfg.GetValue(Section, ThemeColorKey, (int)MobileThemeColor.Dark).AsInt32();
			return Enum.IsDefined(typeof(MobileThemeColor), v) ? (MobileThemeColor)v : MobileThemeColor.Dark;
		}
		set
		{
			ConfigFile cfg = new();
			cfg.Load(PathConst);
			cfg.SetValue(Section, ThemeColorKey, (int)value);
			cfg.Save(PathConst);
			ThemeColorChanged?.Invoke();
		}
	}
}
