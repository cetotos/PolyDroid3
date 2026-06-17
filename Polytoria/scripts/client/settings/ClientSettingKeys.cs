// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Polytoria.Client.Settings;

public static class ClientSettingKeys
{
	public static class General
	{
		public const string CtrlLock = "general.ctrl_lock";
		public const string MasterVolume = "general.master_volume";
		public const string CameraSensitivity = "general.camera_sensitivity";
	}

	public static class Chat
	{
		public const string ChatColors = "chat.chat_colors";
		public const string ChatFont = "chat.chat_font";
		public const string ChatFontSize = "chat.chat_font_size";
	}

	public static class Display
	{
		public const string UiScale = "display.ui_scale";
	}

	public static class Overlay
	{
		public const string PerformanceOverlayMode = "overlay.performance_mode";
		public const string ConnectionIndicators = "overlay.connection_indicators";
		public const string ButtonScale = "overlay.button_scale";
		public const string ShowConsoleButton = "overlay.show_console_button";
	}

	public static class VR
	{
		public const string OpenXR = "vr.openxr";
		public const string LeftHanded = "vr.left_handed";
		public const string HapticStrength = "vr.haptic_strength";
		public const string Grabbing = "vr.grabbing";
		public const string SnapTurnAngle = "vr.snap_turn_angle";
		public const string SmoothTurning = "vr.smooth_turning";
		public const string SmoothTurnSpeed = "vr.smooth_turn_speed";
	}

	public static class Advanced
	{
		public const string ShowAdvancedSettings = "advanced.show_advanced_settings";
	}
}
