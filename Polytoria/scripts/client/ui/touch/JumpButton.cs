// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings;
using Polytoria.Datamodel;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.UI.Touch;

public partial class JumpButton : Control
{
	private static readonly Color NormalModulate = new(1f, 1f, 1f);
	private static readonly Color PressedModulate = new(2f, 2f, 2f);
	private const float TweenDuration = 0.08f;

	private TouchScreenButton _button = null!;
	private Tween? _tween;
	private Vector2 _baseScale = Vector2.One;
	private Vector2 _basePosition = Vector2.Zero;

	public override void _Ready()
	{
		Visible = World.Current!.Input.IsTouchscreen && !Polytoria.Shared.XRBootstrap.IsActive;
		_button = GetNode<TouchScreenButton>("TouchScreenButton");
		_baseScale = _button.Scale;
		_basePosition = _button.Position;
		_button.Pressed += () => AnimateModulate(PressedModulate);
		_button.Released += () => AnimateModulate(NormalModulate);
		ClientSettingsService.Instance.Changed += OnSettingChanged;

		ApplyScale();
	}

	public override void _ExitTree()
	{
		ClientSettingsService.Instance.Changed -= OnSettingChanged;
		base._ExitTree();
	}

	private void OnSettingChanged(SettingChangedEvent change)
	{
		if (change.Key == ClientSettingKeys.Overlay.ButtonScale)
			ApplyScale();
	}

	private void ApplyScale()
	{
		float multiplier = ClientSettingsService.Instance.Get<float>(ClientSettingKeys.Overlay.ButtonScale);
		_button.Scale = _baseScale * multiplier;
		Vector2 textureSize = _button.TextureNormal?.GetSize() ?? Vector2.Zero;
		_button.Position = _basePosition - textureSize * _baseScale * (multiplier - 1f);
	}

	private void AnimateModulate(Color target)
	{
		_tween?.Kill();
		_tween = CreateTween();
		_tween.TweenProperty(_button, "self_modulate", target, TweenDuration);
	}
}
