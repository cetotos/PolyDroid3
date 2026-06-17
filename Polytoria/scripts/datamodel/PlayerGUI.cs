// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Client.UI;

namespace Polytoria.Datamodel;

[Static("PlayerGUI")]
public partial class PlayerGUI : Instance
{
	private const string TouchControlsPath = "res://scenes/client/ui/touch_controls.tscn";
	internal InputFallbackBase InputFallback = null!;

	public override Node CreateGDNode()
	{
		return new CanvasLayer();
	}

	public override void InitGDNode()
	{
		base.InitGDNode();
		InputFallback = new InputFallbackBase() { FocusMode = Control.FocusModeEnum.Click, MouseFilter = Control.MouseFilterEnum.Pass };
		GDNode.AddChild(InputFallback, @internal: Node.InternalMode.Front);
		InputFallback.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
	}

	internal void GrabFocus()
	{
		InputFallback.GrabFocus();
	}

	internal void SetCursorShape(Control.CursorShape shape)
	{
		InputFallback.MouseDefaultCursorShape = shape;
	}

	public override void Init()
	{
		Root.Loaded.Once(OnGameReady);
		base.Init();
	}

	private void OnGameReady()
	{
		if (!Root.Input.IsTouchscreen || Polytoria.Shared.XRBootstrap.IsActive) return;
		PackedScene packed2 = GD.Load<PackedScene>(TouchControlsPath);
		Node touchUI = packed2.Instantiate();
		GDNode.AddChild(touchUI, true, @internal: Node.InternalMode.Back);
	}
}
