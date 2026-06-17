// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System;

namespace Polytoria.Shared;

public partial class XRControlBridge : Node3D
{
	private const float StickDeadZone = 0.15f;
	private const float SnapTurnDegrees = 45f;
	private const float SnapTurnThreshold = 0.6f;
	private const float ScrollDeadZone = 0.5f;
	private const float ScrollTicksPerSecond = 12f;

	private readonly XROrigin3D _origin;
	private XRController3D _left = null!;
	private XRController3D _right = null!;
	private XRPointer? _pointer;
	private float _scrollAccum;
	private bool _snapTurnArmed = true;
	private float _lastHealth = -1f;

	public static XRController3D? LeftController { get; private set; }
	public static XRController3D? RightController { get; private set; }
	public static XRController3D? DominantController => VRSettings.LeftHanded ? LeftController : RightController;

	private bool _jumpDown;
	private bool _sprintDown;
	private bool _toolPrevDown;
	private bool _toolNextDown;
	private bool _menuDown;

	public XRControlBridge(XROrigin3D origin)
	{
		_origin = origin;
	}

	public override void _Ready()
	{
		_left = new XRController3D { Tracker = "left_hand" };
		_right = new XRController3D { Tracker = "right_hand" };
		_origin.AddChild(_left);
		_origin.AddChild(_right);
		LeftController = _left;
		RightController = _right;
		_left.AddChild(new XRGrab(_left));
		_right.AddChild(new XRGrab(_right));
		SpawnPointer();
	}

	public override void _ExitTree()
	{
		if (LeftController == _left) LeftController = null;
		if (RightController == _right) RightController = null;
		base._ExitTree();
	}

	private void SpawnPointer()
	{
		if (VRPanel.Instance == null) return;
		XRController3D hand = VRSettings.LeftHanded ? _left : _right;
		_pointer = new XRPointer(hand, VRPanel.Instance);
		hand.AddChild(_pointer);
	}

	public override void _Process(double delta)
	{
		try
		{
			XRController3D dom = VRSettings.LeftHanded ? _left : _right;
			XRController3D off = VRSettings.LeftHanded ? _right : _left;

			Vector2 moveStick = off.GetVector2("primary");
			Vector2 turnStick = dom.GetVector2("primary");

			SetAxisActionPair("forward", "backward", moveStick.Y);
			SetAxisActionPair("rightward", "leftward", moveStick.X);

			if (Mathf.Abs(turnStick.X) > SnapTurnThreshold && _snapTurnArmed)
			{
				float yaw = -Mathf.Sign(turnStick.X) * Mathf.DegToRad(SnapTurnDegrees);
				_origin.RotateY(yaw);
				_snapTurnArmed = false;
				XRHaptics.Pulse(dom, 0.5f, 0.04f);
			}
			else if (Mathf.Abs(turnStick.X) < StickDeadZone)
			{
				_snapTurnArmed = true;
			}

			if (_pointer != null && Mathf.Abs(turnStick.Y) > ScrollDeadZone)
			{
				_scrollAccum += turnStick.Y * ScrollTicksPerSecond * (float)delta;
				while (_scrollAccum >= 1f)
				{
					_pointer.ScrollTick(true);
					_scrollAccum -= 1f;
				}
				while (_scrollAccum <= -1f)
				{
					_pointer.ScrollTick(false);
					_scrollAccum += 1f;
				}
			}
			else
			{
				_scrollAccum = 0f;
			}

			bool jumpNow = dom.IsButtonPressed("ax_button");
			if (jumpNow && !_jumpDown)
			{
				XRHaptics.PulseBoth(0.6f, 0.06f);
			}
			DispatchButton("jump", jumpNow, ref _jumpDown);
			DispatchButton("sprint", off.IsButtonPressed("ax_button"), ref _sprintDown);

			bool toolPrevNow = off.IsButtonPressed("by_button");
			bool toolNextNow = dom.IsButtonPressed("by_button");
			if ((toolPrevNow && !_toolPrevDown) || (toolNextNow && !_toolNextDown))
			{
				XRHaptics.Pulse(dom, 0.3f, 0.04f);
			}
			DispatchButton("equip_tool_cycle_left", toolPrevNow, ref _toolPrevDown);
			DispatchButton("equip_tool_cycle_right", toolNextNow, ref _toolNextDown);

			bool menuNow = _left.IsButtonPressed("menu_button");
			if (menuNow && !_menuDown)
			{
				if (VRPanel.Instance != null)
				{
					VRPanel.Instance.SetMaximized(!VRPanel.Instance.IsMaximized);
					VRBottomBar.Instance?.RefreshButtonText();
				}
				XRHaptics.Pulse(_left, 0.4f, 0.04f);
			}
			_menuDown = menuNow;

			DamageHaptics();
		}
		catch (Exception ex)
		{
			PT.PrintErr($"XR: control bridge: {ex.Message}");
		}
	}

	private void DamageHaptics()
	{
		Player? player = World.Current?.Players?.LocalPlayer;
		if (player == null || player.IsDeleted)
		{
			_lastHealth = -1f;
			return;
		}
		float health = player.Health;
		if (_lastHealth >= 0f && health < _lastHealth - 0.01f)
		{
			XRHaptics.PulseBoth(0.8f, 0.12f);
		}
		_lastHealth = health;
	}

	private static void SetAxisActionPair(string positiveAction, string negativeAction, float value)
	{
		if (value > StickDeadZone)
		{
			Input.ActionPress(positiveAction, value);
			Input.ActionRelease(negativeAction);
		}
		else if (value < -StickDeadZone)
		{
			Input.ActionPress(negativeAction, -value);
			Input.ActionRelease(positiveAction);
		}
		else
		{
			Input.ActionRelease(positiveAction);
			Input.ActionRelease(negativeAction);
		}
	}

	private static void DispatchButton(string action, bool pressed, ref bool wasDown)
	{
		if (pressed == wasDown) return;
		wasDown = pressed;
		var ev = new InputEventAction
		{
			Action = action,
			Pressed = pressed,
			Strength = pressed ? 1f : 0f,
		};
		Input.ParseInputEvent(ev);
		VRPanel.Instance?.Viewport?.PushInput(ev);
		VRBottomBar.Instance?.Viewport?.PushInput(ev);
	}
}
