// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.Settings;

#if CREATOR
using Polytoria.Creator;
using Polytoria.Creator.Settings;
using Polytoria.Shared.Settings;
#endif
using Polytoria.Shared;
using ObsoleteAttribute = Polytoria.Attributes.ObsoleteAttribute;

namespace Polytoria.Datamodel;

[Static("Lighting")]
public sealed partial class Lighting : Instance
{
	private WorldEnvironment _worldEnv = null!;
	internal Godot.Environment environment = null!;
	private Godot.Sky _sky = null!;

	private SkyboxEnum _skybox;

	public bool CustomSkyApplied { get; private set; }
	private Sky? _currentSky;

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void Init()
	{
		_worldEnv = (WorldEnvironment)GDNode;
		environment = _worldEnv.Environment;
		_sky = environment.Sky;

#if CREATOR
		if (CreatorSettingsService.Instance != null)
		{
			CreatorSettingsService.Instance.Changed += OnCreatorSettingChanged;
			ApplyCreatorLightingEffects();
		}
		else
#endif
		{
			ApplyGraphicsSettings();
		}


		UpdateSkybox();

		base.Init();
	}

	public override void PreDelete()
	{
#if CREATOR
		if (CreatorSettingsService.Instance != null)
		{
			CreatorSettingsService.Instance.Changed -= OnCreatorSettingChanged;
		}
#endif
		base.PreDelete();
	}

#if CREATOR
	private void OnCreatorSettingChanged(SettingChangedEvent e)
	{
		if (e.Key == CreatorSettingKeys.Graphics.PhotoMode || e.Key == CreatorSettingKeys.Graphics.PostProcessing)
		{
			ApplyCreatorLightingEffects();
		}
	}
#endif

	private void ApplyCreatorLightingEffects()
	{
#if CREATOR
		if (CreatorSettingsService.Instance == null)
			return;

		bool photoMode = CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Graphics.PhotoMode);
		bool postProcessing = CreatorSettingsService.Instance.Get<bool>(CreatorSettingKeys.Graphics.PostProcessing);

		if (Globals.IsMobileBuild)
		{
			environment.GlowEnabled = false;
			environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
		}
		else
		{
			bool setTo = postProcessing;
			if (photoMode)
			{
				setTo = false;
			}
			environment.SsaoEnabled = setTo;
			environment.GlowEnabled = setTo;
		}

		environment.SsrEnabled = photoMode;
		environment.SdfgiEnabled = photoMode;
		environment.SsilEnabled = photoMode;
#endif
	}

	public void ApplyGraphicsSettings()
	{
		var settings = ClientSettingsService.Instance;
		bool mobile = Globals.IsMobileBuild;

		bool glow = settings.Get<bool>(ClientSettingKeys.PostProcessing.Glow);
		bool ssao = settings.Get<bool>(ClientSettingKeys.PostProcessing.Ssao);
		bool ssr = settings.Get<bool>(ClientSettingKeys.PostProcessing.Ssr);
		bool ssil = settings.Get<bool>(ClientSettingKeys.PostProcessing.Ssil);
		bool sdfgi = settings.Get<bool>(ClientSettingKeys.PostProcessing.Sdfgi);

		if (mobile)
		{
			glow = false;
			ssao = false;
			ssr = false;
			ssil = false;
			sdfgi = false;
		}

		environment.GlowEnabled = glow;
		environment.SsaoEnabled = ssao;
		environment.SsrEnabled = ssr;
		environment.SsilEnabled = ssil;
		environment.SdfgiEnabled = sdfgi;

		// advanced settings
		environment.SdfgiCascades = settings.Get<int>(ClientSettingKeys.PostProcessing.SdfgiCascades);
		environment.SdfgiMinCellSize = settings.Get<float>(ClientSettingKeys.PostProcessing.SdfgiCellSize);
		environment.SsilRadius = settings.Get<float>(ClientSettingKeys.PostProcessing.SsilRadius);
	}

	public void ApplySky(Sky sky)
	{
		if (sky.IsHidden) return;
		CustomSkyApplied = true;
		_sky.SkyMaterial = sky.SkyMaterial;
		_currentSky = sky;
	}

	public void RemoveSky(Sky sky)
	{
		if (_currentSky != sky) { return; }
		CustomSkyApplied = false;
		UpdateSkybox();
	}

	public void UpdateSkybox()
	{
		if (CustomSkyApplied)
		{
			return;
		}
		_sky.SkyMaterial = Globals.LoadSkybox(_skybox.ToString());
	}

	[Editable, ScriptProperty]
	public SkyboxEnum Skybox
	{
		get => _skybox;
		set
		{
			_skybox = value;
			UpdateSkybox();
		}
	}

	private AmbientSourceEnum _ambientSource;
	private Color _ambientColor;
	private bool _fogEnabled;
	private Color _fogColor;
	private float _fogStartDistance;
	private float _fogEndDistance;

	[Editable, ScriptProperty]
	public AmbientSourceEnum AmbientSource
	{
		get => _ambientSource;
		set
		{
			_ambientSource = value;
			environment.AmbientLightSource = value == AmbientSourceEnum.Skybox
				? Godot.Environment.AmbientSource.Bg
				: Godot.Environment.AmbientSource.Color;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color AmbientColor
	{
		get => _ambientColor;
		set
		{
			_ambientColor = value;
			environment.AmbientLightColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public bool FogEnabled
	{
		get => _fogEnabled;
		set
		{
			_fogEnabled = value;
			environment.FogEnabled = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color FogColor
	{
		get => _fogColor;
		set
		{
			_fogColor = value;
			environment.FogLightColor = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float FogStartDistance
	{
		get => _fogStartDistance;
		set
		{
			_fogStartDistance = value;
			environment.FogDepthBegin = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public float FogEndDistance
	{
		get => _fogEndDistance;
		set
		{
			_fogEndDistance = value;
			environment.FogDepthEnd = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Obsolete("Replaced with SunLight.Brightness")]
	public float SunBrightness
	{
		get => Sun.Brightness;
		set
		{
			Sun.Brightness = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Obsolete("Replaced with SunLight.Color")]
	public Color SunColor
	{
		get => Sun.Color;
		set
		{
			Sun.Color = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, Obsolete("Replaced with SunLight.Shadows")]
	public bool Shadows
	{
		get => Sun.Shadows;
		set
		{
			Sun.Shadows = value;
			OnPropertyChanged();
		}
	}


	public SunLight Sun
	{
		get
		{
			SunLight? sun = FindChild<SunLight>("SunLight");

			if (sun == null)
			{
				sun = Globals.LoadInstance<SunLight>(Root);
				sun.Parent = this;
			}

			return sun;
		}
	}

	public enum SkyboxEnum
	{
		Day1,
		Day2,
		Day3,
		Day4,
		Day5,
		Day6,
		Day7,
		Morning1,
		Morning2,
		Morning3,
		Morning4,
		Night1,
		Night2,
		Night3,
		Night4,
		Night5,
		Sunset1,
		Sunset2,
		Sunset3,
		Sunset4,
		Sunset5
	}

	public enum AmbientSourceEnum
	{
		Skybox,
		Color
	}
}
