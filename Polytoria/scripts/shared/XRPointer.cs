// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class XRPointer : Node3D
{
	private const float MaxReachMeters = 5f;
	private const float LaserRadius = 0.001f;

	private readonly XRController3D _controller;
	private MeshInstance3D _laser = null!;
	private MeshInstance3D _dot = null!;
	private CylinderMesh _laserMesh = null!;
	private bool _triggerWasDown;
	private Vector2 _lastPixel;
	private IVRPointerTarget? _hoverTarget;
	private IVRPointerTarget? _captured;

	public XRPointer(XRController3D controller, VRPanel _)
	{
		_controller = controller;
	}

	public override void _Ready()
	{
		_laserMesh = new CylinderMesh
		{
			TopRadius = LaserRadius * 0.3f,
			BottomRadius = LaserRadius,
			Height = MaxReachMeters,
		};
		_laser = new MeshInstance3D
		{
			Mesh = _laserMesh,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.5f, 0.85f, 1.0f, 0.85f),
				EmissionEnabled = true,
				Emission = new Color(0.4f, 0.8f, 1.0f),
				EmissionEnergyMultiplier = 2.5f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			},
			Visible = false,
		};
		_laser.RotateX(-Mathf.Pi / 2f);
		AddChild(_laser);

		_dot = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.008f, Height = 0.016f },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 1f, 1f, 1f),
				EmissionEnabled = true,
				Emission = new Color(0.7f, 0.95f, 1.0f),
				EmissionEnergyMultiplier = 3f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			},
			TopLevel = true,
			Visible = false,
		};
		AddChild(_dot);
	}

	public override void _Process(double delta)
	{
		Transform3D origin = GlobalTransform;
		Vector3 rayOrigin = origin.Origin;
		Vector3 rayDir = -origin.Basis.Z.Normalized();
		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		float maxRange = MaxReachMeters * ws;

		bool trigDown = _controller.IsButtonPressed("trigger_click") || _controller.GetFloat("trigger") > 0.5f || _controller.IsButtonPressed("select_button");

		if (_captured != null)
		{
			if (Project(_captured, rayOrigin, rayDir, float.MaxValue, requireInBounds: false, out float ct, out Vector3 chit, out Vector2 cpixel))
			{
				ShowBeam(chit, ct);
				if (cpixel != _lastPixel)
				{
					_lastPixel = cpixel;
					PushMotion(_captured, cpixel);
				}
			}
			if (!trigDown)
			{
				PushButton(_captured, _lastPixel, false);
				_captured = null;
				_triggerWasDown = false;
			}
			return;
		}

		IVRPointerTarget? best = null;
		float bestT = float.MaxValue;
		Vector3 bestHit = Vector3.Zero;
		Vector2 bestPixel = Vector2.Zero;

		foreach (IVRPointerTarget tgt in VRPointerRegistry.Targets)
		{
			if (!tgt.AcceptsPointer) continue;
			if (!Project(tgt, rayOrigin, rayDir, maxRange, requireInBounds: true, out float t, out Vector3 hit, out Vector2 pixel)) continue;
			if (t >= bestT) continue;
			best = tgt;
			bestT = t;
			bestHit = hit;
			bestPixel = pixel;
		}

		if (best == null)
		{
			_hoverTarget = null;
			_laser.Visible = false;
			_dot.Visible = false;
			_triggerWasDown = trigDown;
			return;
		}

		ShowBeam(bestHit, bestT);
		best.OnPointerHit();

		if (best != _hoverTarget || bestPixel != _lastPixel)
		{
			_hoverTarget = best;
			_lastPixel = bestPixel;
			PushMotion(best, bestPixel);
		}

		if (trigDown && !_triggerWasDown)
		{
			_triggerWasDown = true;
			_captured = best;
			PushButton(best, bestPixel, true);
			XRHaptics.Pulse(_controller, 0.4f, 0.04f);
		}
		else if (!trigDown)
		{
			_triggerWasDown = false;
		}
	}

	private static bool Project(IVRPointerTarget tgt, Vector3 rayOrigin, Vector3 rayDir, float maxRange, bool requireInBounds, out float t, out Vector3 hit, out Vector2 pixel)
	{
		t = 0f;
		hit = Vector3.Zero;
		pixel = Vector2.Zero;
		Transform3D px = tgt.PanelGlobalTransform;
		Vector3 pn = px.Basis.Z.Normalized();
		float denom = rayDir.Dot(pn);
		if (Mathf.Abs(denom) < 5e-4f) return false;
		t = (px.Origin - rayOrigin).Dot(pn) / denom;
		if (t <= 0f || t > maxRange) return false;

		hit = rayOrigin + rayDir * t;
		Vector3 local = px.AffineInverse() * hit;
		Vector2 size = tgt.PanelSizeMeters;
		if (requireInBounds && (Mathf.Abs(local.X) > size.X / 2f || Mathf.Abs(local.Y) > size.Y / 2f)) return false;

		float u = (local.X + size.X / 2f) / size.X;
		float v = 1f - (local.Y + size.Y / 2f) / size.Y;
		Vector2I psz = tgt.ViewportPixelSize;
		pixel = new(Mathf.Clamp(u, 0f, 1f) * psz.X, Mathf.Clamp(v, 0f, 1f) * psz.Y);
		return true;
	}

	private void ShowBeam(Vector3 hit, float dist)
	{
		_dot.Visible = true;
		_dot.GlobalPosition = hit;
		_laser.Visible = true;
		_laserMesh.Height = dist;
		_laser.Position = new Vector3(0f, 0f, -dist / 2f);
	}

	private static void PushMotion(IVRPointerTarget tgt, Vector2 pixel)
	{
		tgt.TargetViewport.PushInput(new InputEventMouseMotion
		{
			Position = pixel,
			GlobalPosition = pixel,
		});
	}

	private static void PushButton(IVRPointerTarget tgt, Vector2 pixel, bool pressed)
	{
		tgt.TargetViewport.PushInput(new InputEventMouseButton
		{
			Position = pixel,
			GlobalPosition = pixel,
			ButtonIndex = MouseButton.Left,
			Pressed = pressed,
		});
	}

	public bool ScrollTick(bool up)
	{
		IVRPointerTarget? tgt = _captured ?? _hoverTarget;
		if (tgt == null)
		{
			return false;
		}
		MouseButton button = up ? MouseButton.WheelUp : MouseButton.WheelDown;
		tgt.TargetViewport.PushInput(new InputEventMouseButton
		{
			Position = _lastPixel,
			GlobalPosition = _lastPixel,
			ButtonIndex = button,
			Factor = 1f,
			Pressed = true,
		});
		tgt.TargetViewport.PushInput(new InputEventMouseButton
		{
			Position = _lastPixel,
			GlobalPosition = _lastPixel,
			ButtonIndex = button,
			Factor = 1f,
			Pressed = false,
		});
		return true;
	}
}
