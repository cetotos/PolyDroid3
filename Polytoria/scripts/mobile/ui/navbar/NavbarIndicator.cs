// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Mobile.UI;

public partial class NavbarIndicator : Panel
{
	private const double TweenSeconds = 0.32;

	[Export] public NodePath? HBoxPath;

	private Control _hbox = null!;
	private Tween? _activeTween;
	private bool _snapped;

	public override void _Ready()
	{
		TopLevel = true;
		MouseFilter = MouseFilterEnum.Ignore;
		_hbox = GetNode<Control>(HBoxPath);
		MobileUI.Singleton.ViewPathSwitched += OnSwitched;
	}

	public override void _ExitTree()
	{
		if (MobileUI.Singleton != null) MobileUI.Singleton.ViewPathSwitched -= OnSwitched;
		base._ExitTree();
	}

	private async void OnSwitched(MobileViewEnum to)
	{
		NavbarButton? btn = FindButton(to);
		if (btn == null) return;

		if (btn.Size == Vector2.Zero)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!IsInstanceValid(this) || !IsInstanceValid(btn)) return;
		}

		if (!_snapped)
		{
			_snapped = true;
			GlobalPosition = btn.GlobalPosition;
			Size = btn.Size;
			return;
		}

		_activeTween?.Kill();
		_activeTween = CreateTween().SetParallel(true)
			.SetTrans(Tween.TransitionType.Quart)
			.SetEase(Tween.EaseType.Out);
		_activeTween.TweenProperty(this, "global_position", btn.GlobalPosition, TweenSeconds);
		_activeTween.TweenProperty(this, "size", btn.Size, TweenSeconds);
	}

	private NavbarButton? FindButton(MobileViewEnum view)
	{
		foreach (Node child in _hbox.GetChildren())
		{
			if (child is NavbarButton b && b.SwitchTo == view) return b;
		}
		return null;
	}
}
