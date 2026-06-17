// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Threading.Tasks;

namespace Polytoria.Shared;

public partial class AppCloseDimmer : Node
{
	private static Node Singleton = null!;

	public AppCloseDimmer()
	{
		Singleton = this;
	}

	public static async Task Show()
	{
		if (Singleton == null) return;
		Node? parent = Singleton.GetParent();
		if (parent == null) return;

		CanvasLayer layer = new()
		{
			Layer = 10000
		};
		ColorRect c = new()
		{
			Color = new(0, 0, 0, 0.5f),
			ZIndex = 1000,
			TopLevel = true
		};
		c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(c);
		parent.CallDeferred(Node.MethodName.AddChild, layer);

		if (RenderingServer.Singleton.GetRenderingDevice() != null)
		{
			await Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		}
	}
}
