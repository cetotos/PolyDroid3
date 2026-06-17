// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;

namespace Polytoria.Shared.Misc;

public partial class TouchLongPressToRightClick : Node
{
	private const float LongPressTimeSec = 0.45f;
	private const float MaxMovePx = 16f;

	private int _trackedIndex = -1;
	private Vector2 _startPos;
	private double _pressTime;
	private bool _emitted;
	private int _touchCount;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		SetProcessInput(true);
		SetProcess(true);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventScreenTouch t)
		{
			if (t.Pressed)
			{
				_touchCount++;
				if (_touchCount == 1)
				{
					_trackedIndex = t.Index;
					_startPos = t.Position;
					_pressTime = Time.GetUnixTimeFromSystem();
					_emitted = false;
				}
				else
				{
					if (_emitted)
					{
						EmitRightClick(_startPos, false);
						_emitted = false;
					}
					_trackedIndex = -1;
				}
			}
			else
			{
				_touchCount = Math.Max(0, _touchCount - 1);
				if (t.Index == _trackedIndex)
				{
					if (_emitted)
					{
						EmitRightClick(_startPos, false);
						_emitted = false;
					}
					_trackedIndex = -1;
				}
			}
		}
		else if (@event is InputEventScreenDrag drag && drag.Index == _trackedIndex)
		{
			if ((drag.Position - _startPos).Length() > MaxMovePx)
			{
				_trackedIndex = -1;
			}
		}
	}

	public override void _Process(double delta)
	{
		if (Globals.FreezeWorldInput)
		{
			_trackedIndex = -1;
			return;
		}
		if (_trackedIndex != -1 && !_emitted && _touchCount == 1)
		{
			double elapsed = Time.GetUnixTimeFromSystem() - _pressTime;
			if (elapsed >= LongPressTimeSec)
			{
				EmitRightClick(_startPos, true);
				_emitted = true;
			}
		}
	}

	private static void EmitRightClick(Vector2 pos, bool pressed)
	{
		var motion = new InputEventMouseMotion
		{
			Position = pos,
			GlobalPosition = pos,
		};
		Input.ParseInputEvent(motion);

		var ev = new InputEventMouseButton
		{
			Position = pos,
			GlobalPosition = pos,
			ButtonIndex = MouseButton.Right,
			Pressed = pressed,
			ButtonMask = pressed ? MouseButtonMask.Right : 0,
		};
		Input.ParseInputEvent(ev);
	}
}
