// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using System.Collections.Generic;

namespace Polytoria.Shared;

public partial class XRGrab : Node
{
	private const float GripPressThreshold = 0.6f;
	private const float GripReleaseThreshold = 0.35f;
	private const float HandRadius = 0.08f;
	private const float GrabSurfaceMargin = 0.03f;
	private const float ClimbReachMeters = 0.45f;
	private const int VelocitySamples = 8;

	private static readonly Dictionary<Grabbable, int> HighlightCounts = [];
	private static readonly StandardMaterial3D HighlightMat = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		BlendMode = BaseMaterial3D.BlendModeEnum.Add,
		AlbedoColor = new Color(0.45f, 0.7f, 1f, 0.25f),
	};

	private readonly XRController3D _controller;
	private Grabbable? _held;
	private Grabbable? _highlighted;
	private Truss? _climbTruss;
	private Vector3 _climbAnchorLocal;
	private bool _gripDown;
	private AnimatableBody3D? _handBody;
	private PhysicsBody3D? _exceptedPlayerBody;
	private RigidBody3D? _exceptedHeldBody;
	private readonly Vector3[] _positions = new Vector3[VelocitySamples];
	private readonly double[] _times = new double[VelocitySamples];
	private int _sampleHead;
	private int _sampleCount;

	public XRGrab(XRController3D controller)
	{
		_controller = controller;
	}

	public override void _Ready()
	{
		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		_handBody = new AnimatableBody3D
		{
			SyncToPhysics = true,
			CollisionLayer = 1,
			CollisionMask = 0,
		};
		_handBody.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = HandRadius * ws } });
		_controller.AddChild(_handBody);
	}

	public override void _Process(double delta)
	{
		if (XRControlBridge.LeftController != _controller && XRControlBridge.RightController != _controller) return;

		RecordSample();
		UpdateBodyExceptions();

		float grip = _controller.GetFloat("grip");
		if (!_gripDown && grip > GripPressThreshold)
		{
			_gripDown = true;
			TryGrab();
		}
		else if (_gripDown && grip < GripReleaseThreshold)
		{
			_gripDown = false;
			if (_climbTruss != null)
			{
				Unlatch();
			}
			else
			{
				Release();
			}
		}

		if (_climbTruss != null && !TryGetClimb(out _, out _))
		{
			Unlatch();
		}

		if (_held != null && _held.IsDeleted)
		{
			_held = null;
		}

		UpdateHighlight();
	}

	private void UpdateBodyExceptions()
	{
		if (_handBody == null) return;

		PhysicsBody3D? playerBody = World.Current?.Players?.LocalPlayer?.CharBody3D;
		if (playerBody != _exceptedPlayerBody)
		{
			if (_exceptedPlayerBody != null && IsInstanceValid(_exceptedPlayerBody)) _handBody.RemoveCollisionExceptionWith(_exceptedPlayerBody);
			_exceptedPlayerBody = playerBody;
			if (playerBody != null) _handBody.AddCollisionExceptionWith(playerBody);
		}

		RigidBody3D? heldBody = _held?.Parent is Physical heldPhy ? heldPhy.GDNode as RigidBody3D : null;
		if (heldBody != _exceptedHeldBody)
		{
			if (_exceptedHeldBody != null && IsInstanceValid(_exceptedHeldBody)) _handBody.RemoveCollisionExceptionWith(_exceptedHeldBody);
			_exceptedHeldBody = heldBody;
			if (heldBody != null) _handBody.AddCollisionExceptionWith(heldBody);
		}
	}

	public override void _ExitTree()
	{
		if (_highlighted != null)
		{
			RemoveHighlight(_highlighted);
			_highlighted = null;
		}
		_climbTruss = null;
		XRClimbState.Unlatch(this);
		base._ExitTree();
	}

	private Grabbable? FindGrabbable()
	{
		World? root = World.Current;
		Player? localPlayer = root?.Players?.LocalPlayer;
		if (root?.Environment == null || localPlayer == null) return null;

		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		Vector3 handPos = _controller.GlobalPosition;

		Grabbable? best = null;
		float bestDist = float.MaxValue;
		foreach (Instance hit in root.Environment.OverlapSphere(handPos, (HandRadius + GrabSurfaceMargin) * 2f * ws))
		{
			if (hit is not Physical phy || phy.GDNode is not RigidBody3D body) continue;
			Grabbable[] grabs = phy.GetChildrenOfClass<Grabbable>();
			if (grabs.Length == 0) continue;
			Grabbable g = grabs[0];
			if (!g.CanVRGrab(_controller, localPlayer)) continue;
			float dist = handPos.DistanceSquaredTo(body.GlobalPosition);
			if (dist >= bestDist) continue;
			best = g;
			bestDist = dist;
		}

		return best;
	}

	private void TryGrab()
	{
		Player? localPlayer = World.Current?.Players?.LocalPlayer;
		if (localPlayer == null) return;

		Grabbable? best = VRSettings.Grabbing ? FindGrabbable() : null;
		if (best != null && best.TryVRGrab(_controller, localPlayer))
		{
			_held = best;
			XRHaptics.Pulse(_controller, 0.5f, 0.05f);
			return;
		}

		TryLatchTruss();
	}

	private void TryLatchTruss()
	{
		World? root = World.Current;
		if (root?.Environment == null) return;

		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		Vector3 handPos = _controller.GlobalPosition;

		Truss? best = null;
		float bestDist = float.MaxValue;
		foreach (Instance hit in root.Environment.OverlapSphere(handPos, ClimbReachMeters * ws))
		{
			if (hit is not Truss truss || truss.GDNode is not Node3D node) continue;
			float dist = handPos.DistanceSquaredTo(node.GlobalPosition);
			if (dist >= bestDist) continue;
			best = truss;
			bestDist = dist;
		}
		if (best == null || best.GDNode is not Node3D trussNode) return;

		_climbTruss = best;
		_climbAnchorLocal = trussNode.GlobalTransform.AffineInverse() * handPos;
		XRClimbState.Latch(this);
		XRHaptics.Pulse(_controller, 0.6f, 0.05f);
	}

	public bool TryGetClimb(out Vector3 anchorWorld, out Vector3 handWorld)
	{
		anchorWorld = handWorld = default;
		if (_climbTruss == null || _climbTruss.IsDeleted) return false;
		if (_climbTruss.GDNode is not Node3D node || !IsInstanceValid(node)) return false;
		if (!IsInstanceValid(_controller)) return false;
		anchorWorld = node.GlobalTransform * _climbAnchorLocal;
		handWorld = _controller.GlobalPosition;
		return true;
	}

	private void Unlatch()
	{
		_climbTruss = null;
		XRClimbState.Unlatch(this);
		XRHaptics.Pulse(_controller, 0.3f, 0.04f);
	}

	private void UpdateHighlight()
	{
		Grabbable? candidate = null;
		if (_held == null && VRSettings.Grabbing)
		{
			candidate = FindGrabbable();
		}
		if (candidate == _highlighted) return;
		if (_highlighted != null)
		{
			RemoveHighlight(_highlighted);
		}
		_highlighted = candidate;
		if (candidate != null)
		{
			AddHighlight(candidate);
		}
	}

	private static void AddHighlight(Grabbable g)
	{
		HighlightCounts.TryGetValue(g, out int count);
		HighlightCounts[g] = count + 1;
		if (count == 0)
		{
			SetOverlay(g, HighlightMat);
		}
	}

	private static void RemoveHighlight(Grabbable g)
	{
		if (!HighlightCounts.TryGetValue(g, out int count)) return;
		if (count <= 1)
		{
			HighlightCounts.Remove(g);
			SetOverlay(g, null);
		}
		else
		{
			HighlightCounts[g] = count - 1;
		}
	}

	private static void SetOverlay(Grabbable g, Material? mat)
	{
		if (g.IsDeleted || g.Parent is not Physical phy || phy.GDNode == null) return;
		foreach (Node child in phy.GDNode.GetChildren())
		{
			if (child is MeshInstance3D mesh)
			{
				mesh.MaterialOverlay = mat;
			}
		}
	}

	private void Release()
	{
		Grabbable? held = _held;
		_held = null;
		if (held == null || held.IsDeleted) return;
		held.VRRelease(_controller, ComputeVelocity());
		XRHaptics.Pulse(_controller, 0.3f, 0.04f);
	}

	private void RecordSample()
	{
		_positions[_sampleHead] = _controller.GlobalPosition;
		_times[_sampleHead] = Time.GetTicksMsec() / 1000.0;
		_sampleHead = (_sampleHead + 1) % VelocitySamples;
		if (_sampleCount < VelocitySamples) _sampleCount++;
	}

	private Vector3 ComputeVelocity()
	{
		if (_sampleCount < 2) return Vector3.Zero;
		int newest = (_sampleHead - 1 + VelocitySamples) % VelocitySamples;
		int oldest = _sampleCount < VelocitySamples ? 0 : _sampleHead;
		double dt = _times[newest] - _times[oldest];
		if (dt <= 0.0001) return Vector3.Zero;
		return (_positions[newest] - _positions[oldest]) / (float)dt;
	}
}
