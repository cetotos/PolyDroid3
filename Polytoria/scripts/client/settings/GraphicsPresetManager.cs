// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.Settings;

public static class GraphicsPresetManager
{
	private static readonly HashSet<string> PresetManagedKeys = new()
	{
		SharedSettingKeys.Graphics.RenderingMethod,
		ClientSettingKeys.Graphics.RenderScale,
		ClientSettingKeys.Graphics.Msaa,
		ClientSettingKeys.Graphics.ShadowQuality,
		ClientSettingKeys.Graphics.ShadowDistance,
		ClientSettingKeys.PostProcessing.Glow,
		ClientSettingKeys.PostProcessing.Ssao,
		ClientSettingKeys.PostProcessing.Ssr,
		ClientSettingKeys.PostProcessing.Ssil,
		ClientSettingKeys.PostProcessing.Sdfgi,
	};

	public static bool IsPresetManagedKey(string key)
	{
		return PresetManagedKeys.Contains(key);
	}

	private sealed record PresetData(
		float RenderScale,
		MsaaOption Msaa,
		ShadowQuality ShadowQuality,
		float ShadowDistance,
		bool Glow,
		bool Ssao,
		bool Ssr,
		bool Ssil,
		bool Sdfgi
	);

	private static readonly Dictionary<GraphicsPreset, PresetData> Presets = new()
	{
		[GraphicsPreset.Low] = new(0.75f, MsaaOption.Disabled, ShadowQuality.Off, 100f, false, false, false, false, false),
		[GraphicsPreset.Medium] = new(1.0f, MsaaOption.X2, ShadowQuality.Medium, 1000f, true, true, false, false, false),
		[GraphicsPreset.High] = new(1.0f, MsaaOption.X4, ShadowQuality.High, 1250f, true, true, true, false, false),
		[GraphicsPreset.Ultra] = new(1.0f, MsaaOption.X8, ShadowQuality.Ultra, 1250f, true, true, true, true, false),
		[GraphicsPreset.Photo] = new(1.0f, MsaaOption.X8, ShadowQuality.Ultra, 1250f, true, true, true, true, true),
	};

	public static void ApplyPreset(GraphicsPreset preset)
	{
		if (!Presets.TryGetValue(preset, out var data))
		{
			PT.PrintErr($"GraphicsPresetManager: Unknown preset '{preset}', no changes applied.");
			return;
		}

		var settings = ClientSettingsService.Instance;
		settings.Set(ClientSettingKeys.Graphics.RenderScale, data.RenderScale);
		settings.Set(ClientSettingKeys.Graphics.Msaa, data.Msaa);
		settings.Set(ClientSettingKeys.Graphics.ShadowQuality, data.ShadowQuality);
		settings.Set(ClientSettingKeys.Graphics.ShadowDistance, data.ShadowDistance);
		settings.Set(ClientSettingKeys.PostProcessing.Glow, data.Glow);
		settings.Set(ClientSettingKeys.PostProcessing.Ssao, data.Ssao);
		settings.Set(ClientSettingKeys.PostProcessing.Ssr, data.Ssr);
		settings.Set(ClientSettingKeys.PostProcessing.Ssil, data.Ssil);
		settings.Set(ClientSettingKeys.PostProcessing.Sdfgi, data.Sdfgi);
	}
}
