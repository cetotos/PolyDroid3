// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Creator.UI;
using Polytoria.Datamodel;
using Polytoria.Datamodel.Creator;
using Polytoria.Datamodel.Interfaces;

namespace Polytoria.Creator;

public partial class MultiSelectionBox : Control
{
	private const float BoxSizeThreshold = 5;
	private bool _dragging;
	private Vector2 _dragStart;
	private Vector2 _dragEnd;

	private int _anchorTouchIndex = -1;
	private int _endTouchIndex = -1;
	private Vector2 _anchorTouchPos;

	[Export]
	public WorldContainerOverlay Overlay = null!;

	[Export]
	private Panel _panel = null!;

	[Export]
	private Control _pivotControl = null!;

	[Export]
	private int _selectSensitivity = 50;

	private Tween? _tween;

	private void CalculateBox(Vector2 endPosition)
	{
		Vector2 topLeft = _dragStart - _pivotControl.GlobalPosition;
		Vector2 bottomRight = endPosition - _pivotControl.GlobalPosition;

		if (topLeft.X > bottomRight.X)
		{
			(bottomRight.X, topLeft.X) = (topLeft.X, bottomRight.X);
		}
		if (topLeft.Y > bottomRight.Y)
		{
			(bottomRight.Y, topLeft.Y) = (topLeft.Y, bottomRight.Y);
		}

		Rect2 box = new(topLeft, bottomRight - topLeft);
		if (box.Size.Length() < BoxSizeThreshold)
		{
			return;
		}
		Instance[] allObjects = Overlay.World.Environment.GetDescendants();

		Overlay.World.CreatorContext.Selections.DeselectAll();

		bool altPressed = Input.IsKeyPressed(Key.Alt);

		foreach (Instance item in allObjects)
		{
			if (item is Dynamic dyn)
			{
				var camera = Overlay.World.CreatorContext.Freelook.Camera3D;
				var globalPos = dyn.GetGlobalPosition();

				if (!camera.IsPositionInFrustum(globalPos))
					continue;

				if (box.HasPoint(camera.UnprojectPosition(globalPos)))
				{
					Instance? top = dyn;
					if (!altPressed)
					{
						top = Gizmos.GetModelRoot(dyn);
					}
					if (top == null) continue;
					if (top is Dynamic pd && pd.Locked) continue;

					if (altPressed && (top is IGroup)) continue;
					Overlay.World.CreatorContext.Selections.Select(top);
				}
			}
		}

		Overlay.Container.GrabFocus();
	}

	private void StartDrag(Vector2 startPos)
	{
		_tween?.Stop();
		_dragging = true;
		_dragStart = startPos;
		_dragEnd = startPos;
		_panel.Size = Vector2.Zero;
		_panel.Visible = true;
		_panel.Modulate = new Color(1, 1, 1, 1);
	}

	private void FinishDrag(Vector2 endPos)
	{
		_dragging = false;
		if ((_dragStart - endPos).Length() > _selectSensitivity)
		{
			_tween = GetTree().CreateTween();
			_tween.TweenProperty(_panel, "modulate", new Color(1, 1, 1, 0), 0.15f);
			_tween.TweenCallback(Callable.From(() =>
			{
				_panel.Visible = false;
				_panel.Size = Vector2.Zero;
			}));
			CalculateBox(endPos);
		}
		else
		{
			_panel.Visible = false;
			_panel.Size = Vector2.Zero;
		}
	}

	private void CancelDrag()
	{
		_dragging = false;
		_panel.Visible = false;
		_panel.Size = Vector2.Zero;
	}

	private void UpdateBoxVisual(Vector2 endPos)
	{
		Vector2 sizeProc = endPos - _dragStart;
		Vector2 pos = _dragStart;
		if (sizeProc.X < 0) pos += new Vector2(sizeProc.X, 0);
		if (sizeProc.Y < 0) pos += new Vector2(0, sizeProc.Y);
		_panel.GlobalPosition = pos;
		_panel.Size = new Vector2(Mathf.Abs(sizeProc.X), Mathf.Abs(sizeProc.Y));
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		Gizmos gizmos = Overlay.World.CreatorContext.Gizmos;
		CreatorSelections selections = Overlay.World.CreatorContext.Selections;

		if (Overlay.World.Input.IsTouchscreen)
		{
			if (@event is InputEventScreenTouch touch)
			{
				if (touch.Pressed)
				{
					if (_anchorTouchIndex == -1)
					{
						_anchorTouchIndex = touch.Index;
						_anchorTouchPos = touch.Position;
					}
					else if (_endTouchIndex == -1 && touch.Index != _anchorTouchIndex)
					{
						_endTouchIndex = touch.Index;
						if (!gizmos.HoveringGizmos && selections.SelectedInstances.Count == 0)
						{
							StartDrag(_anchorTouchPos);
							_dragEnd = touch.Position;
							UpdateBoxVisual(_dragEnd);
						}
					}
				}
				else
				{
					if (_dragging && (touch.Index == _anchorTouchIndex || touch.Index == _endTouchIndex))
					{
						FinishDrag(_dragEnd);
					}
					if (touch.Index == _anchorTouchIndex) _anchorTouchIndex = -1;
					if (touch.Index == _endTouchIndex) _endTouchIndex = -1;
				}
				return;
			}
			if (@event is InputEventScreenDrag drag && _dragging)
			{
				if (drag.Index == _endTouchIndex)
				{
					_dragEnd = drag.Position;
					UpdateBoxVisual(_dragEnd);
				}
				else if (drag.Index == _anchorTouchIndex)
				{
					_anchorTouchPos = drag.Position;
					_dragStart = _anchorTouchPos;
					UpdateBoxVisual(_dragEnd);
				}
				return;
			}
			return;
		}

		Vector2 mousePosition = GetViewport().GetMousePosition();

		if (_dragging && @event is InputEventMouseButton rightBtn && rightBtn.ButtonIndex == MouseButton.Right && rightBtn.Pressed)
		{
			CancelDrag();
			return;
		}

		if (@event is InputEventMouseButton mouseEvent && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			if (mouseEvent.Pressed)
			{
				if (_dragging == false && !gizmos.HoveringGizmos && selections.SelectedInstances.Count == 0)
				{
					StartDrag(mousePosition);
				}
			}
			else if (_dragging)
			{
				FinishDrag(mousePosition);
			}
		}

		if (@event is InputEventMouseMotion && _dragging)
		{
			UpdateBoxVisual(mousePosition);
		}
	}
}
