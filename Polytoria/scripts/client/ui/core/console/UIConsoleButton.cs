// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Client.Settings;
using Polytoria.Shared;
using Polytoria.Shared.Settings;

namespace Polytoria.Client.UI;

public partial class UIConsoleButton : Button
{
	[Export] public DevConsoleWindow DevWindow { get; set; } = null!;

	public override void _Ready()
	{
		DevWindow ??= CoreUIRoot.Singleton.DevWindow;
		ClientSettingsService.Instance.Changed += OnSettingChanged;
		UpdateVisible();
		Toggled += OnToggled;
		DevWindow.VisibilityChanged += OnConsoleVisibilityChanged;
	}

	public override void _ExitTree()
	{
		if (ClientSettingsService.Instance != null)
			ClientSettingsService.Instance.Changed -= OnSettingChanged;
		base._ExitTree();
	}

	internal void OnToggled(bool toggleOn)
	{
		DevWindow.SetOpen(toggleOn);
	}

	private void OnConsoleVisibilityChanged()
	{
		SetPressedNoSignal(DevWindow.Visible);
	}

	private void OnSettingChanged(SettingChangedEvent change)
	{
		if (change.Key == ClientSettingKeys.Overlay.ShowConsoleButton)
			UpdateVisible();
	}

	private void UpdateVisible()
	{
		if (XRBootstrap.IsActive)
		{
			Visible = true;
			return;
		}

		bool touchscreen = Globals.IsMobileBuild || DisplayServer.IsTouchscreenAvailable();
		Visible = touchscreen && ClientSettingsService.Instance.Get<bool>(ClientSettingKeys.Overlay.ShowConsoleButton);
	}
}
