// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public static class XRHaptics
{
	public static void Pulse(XRController3D? controller, float amplitude, float durationSec)
	{
		float strength = VRSettings.HapticStrength;
		if (controller == null || strength <= 0f) return;
		controller.TriggerHapticPulse("haptic", 0f, Mathf.Clamp(amplitude * strength, 0f, 1f), durationSec, 0f);
	}

	public static void PulseBoth(float amplitude, float durationSec)
	{
		Pulse(XRControlBridge.LeftController, amplitude, durationSec);
		Pulse(XRControlBridge.RightController, amplitude, durationSec);
	}
}
