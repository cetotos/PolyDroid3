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
		[GraphicsPreset.Low] = new(
			RenderScale: 0.75f,
			Msaa: MsaaOption.Disabled,
			ShadowQuality: ShadowQuality.Off,
			ShadowDistance: 100f,
			Glow: false,
			Ssao: false,
			Ssr: false,
			Ssil: false,
			Sdfgi: false
		),
		[GraphicsPreset.Medium] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X2,
			ShadowQuality: ShadowQuality.Medium,
			ShadowDistance: 1000f,
			Glow: true,
			Ssao: true,
			Ssr: false,
			Ssil: false,
			Sdfgi: false
		),
		[GraphicsPreset.High] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X4,
			ShadowQuality: ShadowQuality.High,
			ShadowDistance: 1250f,
			Glow: true,
			Ssao: true,
			Ssr: true,
			Ssil: false,
			Sdfgi: false
		),
		[GraphicsPreset.Ultra] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X8,
			ShadowQuality: ShadowQuality.Ultra,
			ShadowDistance: 1250f,
			Glow: true,
			Ssao: true,
			Ssr: true,
			Ssil: true,
			Sdfgi: false
		),
		[GraphicsPreset.Photo] = new(
			RenderScale: 1.0f,
			Msaa: MsaaOption.X8,
			ShadowQuality: ShadowQuality.Ultra,
			ShadowDistance: 1250f,
			Glow: true,
			Ssao: true,
			Ssr: true,
			Ssil: true,
			Sdfgi: true
		),
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
