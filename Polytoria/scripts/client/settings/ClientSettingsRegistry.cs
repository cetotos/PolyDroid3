// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Shared.Settings;
using System.Collections.Generic;

namespace Polytoria.Client.Settings;

public static class ClientSettingsRegistry
{
	public static readonly IReadOnlyList<SettingSectionDef> Sections =
	[
		new() {Key = "general", Label = "General", IconPath = "res://assets/textures/ui-icons/settings.svg", SortOrder = 0},
		new() {Key = "display", Label = "Display", IconPath = "res://assets/textures/ui-icons/camera.svg", SortOrder = 1},
		new() {Key = "graphics", Label = "Graphics", IconPath = "res://assets/textures/ui-icons/mountain.svg", SortOrder = 2},
		new() {Key = "post_processing", Label = "Post Processing", IconPath = "res://assets/textures/ui-icons/rocket.svg", SortOrder = 3},
		new() {Key = "ray_tracing", Label = "Ray Tracing", IconPath = "res://assets/textures/ui-icons/raytrace.svg", SortOrder = 4},
		new() {Key = "overlay", Label = "Overlay", IconPath = "res://assets/textures/ui-icons/copy.svg", SortOrder = 5},
		new() {Key = "chat", Label = "Chat", IconPath = "res://assets/textures/ui-icons/messages.svg", SortOrder = 6},
		new() {Key = "vr", Label = "VR", IconPath = "res://assets/textures/ui-icons/vr.svg", SortOrder = 7},
		new() {Key = "advanced", Label = "Advanced", IconPath = "res://assets/textures/ui-icons/code.svg", SortOrder = 8}
	];

	public static readonly IReadOnlyDictionary<string, SettingDef> Definitions = Build();

	private static Dictionary<string, SettingDef> Build()
	{
		var defs = new Dictionary<string, SettingDef>();

		SharedSettingsRegistry.AddSharedTo(defs);

		defs.Add(ClientSettingKeys.Chat.ChatColors,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Chat.ChatColors,
				SectionKey = "chat",
				Label = "Chat Colors",
				Description = "Show colored usernames in chat.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(ClientSettingKeys.Chat.ChatFont,
			new SettingDef<string>
			{
				Key = ClientSettingKeys.Chat.ChatFont,
				SectionKey = "chat",
				Label = "Chat Font",
				Description = "Font used for chat messages.",
				ValueKind = SettingValueKind.String,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = "",
				Options =
				[
					new() { Value = "", Label = "Default" },
					new() { Value = "res://assets/fonts/built-in/SourceSans3-VariableFont_wght.ttf", Label = "Source Sans" },
					new() { Value = "res://assets/fonts/built-in/RobotoMono-VariableFont_wght.ttf", Label = "Roboto Mono" },
					new() { Value = "res://assets/fonts/built-in/Rubik-VariableFont_wght.ttf", Label = "Rubik" },
					new() { Value = "res://assets/fonts/built-in/Poppins/Poppins-Regular.ttf", Label = "Poppins" },
					new() { Value = "res://assets/fonts/built-in/ComicNeue/ComicNeue-Regular.ttf", Label = "Comic Neue" },
					new() { Value = "res://assets/fonts/built-in/PressStart2P-Regular.ttf", Label = "Press Start 2P" },
					new() { Value = "res://assets/fonts/built-in/Comic Sans MS.ttf", Label = "Comic Sans MS" },
					new() { Value = "res://assets/fonts/built-in/Fredoka-VariableFont_wdth,wght.ttf", Label = "Fredoka" },
				]
			});

		defs.Add(ClientSettingKeys.Chat.ChatFontSize,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Chat.ChatFontSize,
				SectionKey = "chat",
				Label = "Chat Font Size",
				Description = "Font size for chat messages. 0 uses the theme default.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 0f,
				MinValue = 0f,
				MaxValue = 28f,
				Step = 1f
			});

		defs.Add(ClientSettingKeys.General.CtrlLock,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.General.CtrlLock,
				SectionKey = "general",
				Label = "Ctrl Lock",
				Description = "Allow Ctrl Lock while in third person.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(ClientSettingKeys.General.MasterVolume,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.General.MasterVolume,
				SectionKey = "general",
				Label = "Volume",
				Description = "Master game volume.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 80f,
				MinValue = 0f,
				MaxValue = 100f,
				Step = 1f
			});

		defs.Add(ClientSettingKeys.General.CameraSensitivity,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.General.CameraSensitivity,
				SectionKey = "general",
				Label = "Camera Sensitivity",
				Description = "Camera movement sensitivity.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 0.6f,
				MinValue = 0.1f,
				MaxValue = 1.2f,
				Step = 0.1f
			});

		defs.Add(ClientSettingKeys.Display.UiScale,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Display.UiScale,
				SectionKey = "display",
				Label = "UI Scale",
				Description = "Scale of the user interface.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = 1f,
				Options =
				[
					new() { Value = 0.5f, Label = "0.5x" },
					new() { Value = 0.75f, Label = "0.75x" },
					new() { Value = 1f, Label = "1x" },
					new() { Value = 1.25f, Label = "1.25x" },
					new() { Value = 1.5f, Label = "1.5x" },
					new() { Value = 1.75f, Label = "1.75x" },
					new() { Value = 2f, Label = "2x" },
				]
			});

		defs.Add(ClientSettingKeys.Overlay.PerformanceOverlayMode,
			new SettingDef<OverlayMode>
			{
				Key = ClientSettingKeys.Overlay.PerformanceOverlayMode,
				SectionKey = "overlay",
				Label = "Performance Overlay",
				Description = "Show performance information on the screen.",
				ValueKind = SettingValueKind.Enum,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = OverlayMode.None,
				Options =
				[
					new() { Value = OverlayMode.None, Label = "None" },
					new() { Value = OverlayMode.Minimal, Label = "Minimal" },
					new() { Value = OverlayMode.Full, Label = "Full" },
				]
			});

		defs.Add(ClientSettingKeys.Overlay.ConnectionIndicators,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Overlay.ConnectionIndicators,
				SectionKey = "overlay",
				Label = "Show Connection Indicators",
				Description = "Show connection status warnings.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true
			});

		defs.Add(ClientSettingKeys.Overlay.ButtonScale,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.Overlay.ButtonScale,
				SectionKey = "overlay",
				Label = "Button Scale",
				Description = "Scale of the sprint and jump buttons.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = 1f,
				DisabledText = _ =>
				{
					if (Polytoria.Shared.XRBootstrap.IsActive)
						return "Unavailable in VR.";
					bool touchscreen = Polytoria.Shared.Globals.IsMobileBuild || Godot.DisplayServer.IsTouchscreenAvailable();
					return touchscreen ? null : "Only available on touchscreen devices.";
				},
				Options =
				[
					new() { Value = 0.5f, Label = "0.5x" },
					new() { Value = 0.75f, Label = "0.75x" },
					new() { Value = 1f, Label = "1x" },
					new() { Value = 1.25f, Label = "1.25x" },
					new() { Value = 1.5f, Label = "1.5x" },
					new() { Value = 1.75f, Label = "1.75x" },
					new() { Value = 2f, Label = "2x" },
				]
			});

		defs.Add(ClientSettingKeys.Advanced.ShowAdvancedSettings,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Advanced.ShowAdvancedSettings,
				SectionKey = "advanced",
				Label = "Show Advanced Settings",
				Description = "Shows hidden advanced settings.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
			});

		defs.Add(ClientSettingKeys.Overlay.ShowConsoleButton,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.Overlay.ShowConsoleButton,
				SectionKey = "overlay",
				Label = "Show Console Button",
				Description = "Show the developer console button on screen.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false,
			});

		defs.Add(ClientSettingKeys.VR.OpenXR,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.VR.OpenXR,
				SectionKey = "vr",
				Label = "Enable OpenXR",
				Description = "Initialize OpenXR for VR. Requires restart to take effect.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
				RequiresRestart = true
			});

		static string? RequiresVR(ISettingsContext _) =>
			Polytoria.Shared.XRBootstrap.IsActive ? null : "Only available in VR.";

		defs.Add(ClientSettingKeys.VR.LeftHanded,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.VR.LeftHanded,
				SectionKey = "vr",
				Label = "Left Handed",
				Description = "Toggle left hand as main hand.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false,
				DisabledText = RequiresVR
			});

		defs.Add(ClientSettingKeys.VR.HapticStrength,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.VR.HapticStrength,
				SectionKey = "vr",
				Label = "Haptic Strength",
				Description = "Controller vibration strength.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 100f,
				MinValue = 0f,
				MaxValue = 100f,
				Step = 5f,
				DisabledText = RequiresVR
			});

		defs.Add(ClientSettingKeys.VR.Grabbing,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.VR.Grabbing,
				SectionKey = "vr",
				Label = "Grab Objects",
				Description = "Grab and throw objects with the grip buttons.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = true,
				DisabledText = RequiresVR
			});

		defs.Add(ClientSettingKeys.VR.SnapTurnAngle,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.VR.SnapTurnAngle,
				SectionKey = "vr",
				Label = "Snap Turn Angle",
				Description = "Degrees to turn with every flick.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Dropdown,
				DefaultValue = 45f,
				Options =
				[
					new() { Value = 30f, Label = "30°" },
					new() { Value = 45f, Label = "45°" },
					new() { Value = 60f, Label = "60°" },
					new() { Value = 90f, Label = "90°" },
				],
				DisabledText = RequiresVR
			});

		defs.Add(ClientSettingKeys.VR.SmoothTurning,
			new SettingDef<bool>
			{
				Key = ClientSettingKeys.VR.SmoothTurning,
				SectionKey = "vr",
				Label = "Smooth Turning",
				Description = "Toggle continuously turning instead of snap turning.",
				ValueKind = SettingValueKind.Bool,
				ControlKind = SettingControlKind.Toggle,
				DefaultValue = false,
				DisabledText = RequiresVR
			});

		defs.Add(ClientSettingKeys.VR.SmoothTurnSpeed,
			new SettingDef<float>
			{
				Key = ClientSettingKeys.VR.SmoothTurnSpeed,
				SectionKey = "vr",
				Label = "Smooth Turning Speed",
				Description = "Turn speed in degrees per second.",
				ValueKind = SettingValueKind.Float,
				ControlKind = SettingControlKind.Slider,
				DefaultValue = 90f,
				MinValue = 30f,
				MaxValue = 270f,
				Step = 15f,
				DisabledText = RequiresVR
			});

		SettingDef.ValidateAll(defs.Values);
		return defs;
	}
}
