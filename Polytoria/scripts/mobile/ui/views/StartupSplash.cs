// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Mobile;

namespace Polytoria.Mobile.UI;

public partial class StartupSplash : Control
{
	public override void _Ready()
	{
		StyleBoxFlat sb = new() { BgColor = MobileSettingsStore.GetBgColor() };
		AddThemeStyleboxOverride("panel", sb);
		Visible = true;
	}

	public void HideSplash()
	{
		GetNode<AnimationPlayer>("AnimPlay").Play("fadeout");
	}
}
