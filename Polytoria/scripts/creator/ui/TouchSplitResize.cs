// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Shared;
using System.Collections.Generic;

namespace Polytoria.Creator.UI;

public sealed partial class TouchSplitResize : Node
{
	private const float GrabZone = 24f;
	private const float CancelMove = 16f;
	private const ulong HoldMs = 300;

	private readonly List<SplitContainer> _splits = [];
	private SplitContainer? _candidate;
	private SplitContainer? _active;
	private ulong _pressTime;
	private Vector2 _pressLocal;
	private Vector2 _lastLocal;

	private static bool Enabled => Globals.IsMobileBuild || DisplayServer.IsTouchscreenAvailable();

	private void Rescan()
	{
		_splits.Clear();
		Collect(GetTree().Root);
	}

	private void Collect(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is SplitContainer sc) _splits.Add(sc);
			Collect(child);
		}
	}

	private static (Control?, Control?) Panes(SplitContainer sc)
	{
		Control? a = null;
		Control? b = null;
		foreach (Node c in sc.GetChildren())
		{
			if (c is Control ctrl && ctrl.Visible)
			{
				if (a == null) a = ctrl;
				else { b = ctrl; break; }
			}
		}
		return (a, b);
	}

	private SplitContainer? HitTest()
	{
		SplitContainer? best = null;
		float bestDist = GrabZone;

		foreach (SplitContainer sc in _splits)
		{
			if (!GodotObject.IsInstanceValid(sc) || !sc.IsVisibleInTree()) continue;
			(Control? a, Control? b) = Panes(sc);
			if (a == null || b == null) continue;

			Vector2 m = sc.GetLocalMousePosition();
			Vector2 size = sc.Size;
			if (m.X < 0 || m.Y < 0 || m.X > size.X || m.Y > size.Y) continue;

			bool vertical = sc is VSplitContainer;
			float line = vertical ? a.Position.Y + a.Size.Y : a.Position.X + a.Size.X;
			float dist = Mathf.Abs((vertical ? m.Y : m.X) - line);

			if (dist < bestDist)
			{
				bestDist = dist;
				best = sc;
			}
		}

		return best;
	}

	public override void _Input(InputEvent @event)
	{
		if (!Enabled) return;

		if (@event is InputEventMouseButton btn && btn.ButtonIndex == MouseButton.Left)
		{
			if (btn.Pressed)
			{
				if (_splits.Count == 0) Rescan();
				_candidate = HitTest();
				_active = null;
				if (_candidate != null)
				{
					_pressLocal = _candidate.GetLocalMousePosition();
					_pressTime = Time.GetTicksMsec();
				}
			}
			else
			{
				if (_active != null) GetViewport().SetInputAsHandled();
				_candidate = null;
				_active = null;
				Globals.DockResizing = false;
			}
		}
		else if (@event is InputEventMouseMotion)
		{
			if (_active != null)
			{
				Vector2 cur = _active.GetLocalMousePosition();
				Vector2 d = cur - _lastLocal;
				_lastLocal = cur;
				_active.SplitOffset += Mathf.RoundToInt(_active is VSplitContainer ? d.Y : d.X);
				GetViewport().SetInputAsHandled();
			}
			else if (_candidate != null && _candidate.GetLocalMousePosition().DistanceTo(_pressLocal) > CancelMove)
			{
				_candidate = null;
			}
		}
	}

	public override void _ExitTree()
	{
		Globals.DockResizing = false;
	}

	public override void _Process(double delta)
	{
		if (_active != null || _candidate == null) return;
		if (Time.GetTicksMsec() - _pressTime < HoldMs) return;

		Vector2 cur = _candidate.GetLocalMousePosition();
		if (cur.DistanceTo(_pressLocal) <= CancelMove)
		{
			_active = _candidate;
			_lastLocal = cur;
			Globals.DockResizing = true;
			if (Globals.IsMobileBuild) Input.VibrateHandheld(40);
		}
		else
		{
			_candidate = null;
		}
	}
}
