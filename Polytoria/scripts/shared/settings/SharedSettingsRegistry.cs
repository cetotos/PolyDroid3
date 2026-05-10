// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.


using System.Collections.Generic;

namespace Polytoria.Shared.Settings;

public static class SharedSettingsRegistry
{
	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	public static void AddSharedTo(Dictionary<string, SettingDef> target)
	{
		foreach (var pair in Definitions)
			target[pair.Key] = pair.Value;
	}

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>
		{
			{
				SharedSettingKeys.Display.Fullscreen,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.Display.Fullscreen,
					SectionKey = "display",
					Label = "Fullscreen",
					Description = "Use fullscreen window mode.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = false
				}
			},
			{
				SharedSettingKeys.Display.VSync,
				new SettingDef<bool>
				{
					Key = SharedSettingKeys.Display.VSync,
					SectionKey = "display",
					Label = "V-Sync",
					Description = "Synchronize frames to display refresh.",
					ValueKind = SettingValueKind.Bool,
					ControlKind = SettingControlKind.Toggle,
					DefaultValue = true
				}
			},
			{
				SharedSettingKeys.Graphics.RenderingMethod,
				new SettingDef<RenderingMethodOption>
				{
					Key = SharedSettingKeys.Graphics.RenderingMethod,
					SectionKey = "graphics",
					Label = "Rendering Method",
					Description = "Rendering method to use. Use compatibility on older hardware.",
					ValueKind = SettingValueKind.Enum,
					ControlKind = SettingControlKind.Dropdown,
					DefaultValue = RenderingMethodOption.Standard,
					RequiresRestart = true,
					Options =
					[
						new() { Value = RenderingMethodOption.Standard, Label = "Standard" },
						new() { Value = RenderingMethodOption.Performance, Label = "Performance" },
						new() { Value = RenderingMethodOption.Compatibility, Label = "Compatibility" },
					]
				}
			},
		};

		SettingDef.ValidateAll(defs.Values);
		return defs;
	}
}
