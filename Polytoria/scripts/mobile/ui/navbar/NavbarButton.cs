// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public partial class NavbarButton : Button
{
	private const float ActiveAlpha = 1f;
	private const float InactiveAlpha = 0.4f;
	private const double FadeSeconds = 0.16;

	[Export]
	public MobileViewEnum SwitchTo;

	private Tween? _activeTween;

	public override void _Ready()
	{
		MobileUI.Singleton.ViewPathSwitched += OnViewPathSwitched;
		base._Ready();
	}

	public override void _ExitTree()
	{
		if (MobileUI.Singleton != null) MobileUI.Singleton.ViewPathSwitched -= OnViewPathSwitched;
		base._ExitTree();
	}

	private void OnViewPathSwitched(MobileViewEnum to)
	{
		float target = to == SwitchTo ? ActiveAlpha : InactiveAlpha;
		_activeTween?.Kill();
		_activeTween = CreateTween();
		_activeTween.TweenProperty(this, "modulate:a", target, FadeSeconds);
	}

	public override void _Pressed()
	{
		MobileUI.Singleton.SwitchTo(SwitchTo);
		base._Pressed();
	}
}
