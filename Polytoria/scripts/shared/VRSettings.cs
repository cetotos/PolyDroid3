// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Client.Settings;
using Polytoria.Shared.Settings;

namespace Polytoria.Shared;

public static class VRSettings
{
	private static ClientSettingsService? _service;
	private static bool _leftHanded;
	private static float _hapticStrength = 1f;
	private static bool _grabbing = true;
	private static float _snapTurnDegrees = 45f;
	private static bool _smoothTurning;
	private static float _smoothTurnSpeed = 90f;
	private static float _uiScale = 1f;

	public static bool LeftHanded { get { Sync(); return _leftHanded; } }
	public static float HapticStrength { get { Sync(); return _hapticStrength; } }
	public static bool Grabbing { get { Sync(); return _grabbing; } }
	public static float SnapTurnDegrees { get { Sync(); return _snapTurnDegrees; } }
	public static bool SmoothTurning { get { Sync(); return _smoothTurning; } }
	public static float SmoothTurnSpeed { get { Sync(); return _smoothTurnSpeed; } }
	public static float UiScale { get { Sync(); return _uiScale; } }

	private static void Sync()
	{
		ClientSettingsService service = ClientSettingsService.Instance;
		if (service == null || service == _service) return;
		_service = service;
		service.Changed += OnChanged;
		Refresh();
	}

	private static void OnChanged(SettingChangedEvent _) => Refresh();

	private static void Refresh()
	{
		if (_service == null) return;
		_leftHanded = _service.Get<bool>(ClientSettingKeys.VR.LeftHanded);
		_hapticStrength = _service.Get<float>(ClientSettingKeys.VR.HapticStrength) / 100f;
		_grabbing = _service.Get<bool>(ClientSettingKeys.VR.Grabbing);
		_snapTurnDegrees = _service.Get<float>(ClientSettingKeys.VR.SnapTurnAngle);
		_smoothTurning = _service.Get<bool>(ClientSettingKeys.VR.SmoothTurning);
		_smoothTurnSpeed = _service.Get<float>(ClientSettingKeys.VR.SmoothTurnSpeed);
		_uiScale = System.Math.Clamp(_service.Get<float>(ClientSettingKeys.Display.UiScale), 0.5f, 2f);
	}
}
