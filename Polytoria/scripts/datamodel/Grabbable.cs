// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using static Polytoria.Datamodel.Environment;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Grabbable : Instance
{
	private const float VRFollowForce = 40f;
	private const float VRRotateForce = 20f;
	private const float ReleaseHandbackDelay = 0.6f;

	private bool _dragging = false;
	private int _releaseSeq;
	private Physical? _parent = null!;

	private float _force;
	private float _maxRange;
	private float _maxGrabbableRange;
	private bool _useDragForce;
	private Player? _dragger;
	private GrabbablePermissionModeEnum _permissionMode = GrabbablePermissionModeEnum.Everyone;

	[Editable, ScriptProperty, DefaultValue(10)]
	public float Force
	{
		get => _force;
		set
		{
			_force = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(8)]
	public float MaxRange
	{
		get => _maxRange;
		set
		{
			_maxRange = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(12)]
	public float MaxGrabbableRange
	{
		get => _maxGrabbableRange;
		set
		{
			_maxGrabbableRange = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool UseDragForce
	{
		get => _useDragForce;
		set
		{
			_useDragForce = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public GrabbablePermissionModeEnum PermissionMode
	{
		get => _permissionMode;
		set
		{
			_permissionMode = value;
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public Player? Dragger => _dragger;
	[ScriptProperty] public PTFunction? PermissionPredicate { get; set; }
	[ScriptProperty] public PTSignal<Player> Grabbed { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> Released { get; private set; } = new();

	internal Node3D? VRHand { get; private set; }
	internal Node3D? VRHand2 { get; private set; }

	private Transform3D _vrHandOffset = Transform3D.Identity;
	private Transform3D _vrTwoHandOffset = Transform3D.Identity;

	internal bool CanVRGrab(Node3D hand, Player by)
	{
		if (_parent == null) return false;
		if (PermissionMode == GrabbablePermissionModeEnum.None) return false;
		if (VRHand == hand || VRHand2 == hand) return false;
		if (VRHand != null) return VRHand2 == null && _dragger == by;
		return _dragger == null;
	}

	internal bool TryVRGrab(Node3D hand, Player by)
	{
		if (!CanVRGrab(hand, by)) return false;
		if (VRHand == null)
		{
			VRHand = hand;
			CaptureVROffsets();
			_parent!.InvokeClicked(by);
		}
		else
		{
			VRHand2 = hand;
			CaptureVROffsets();
		}
		return true;
	}

	internal void VRRelease(Node3D hand, Vector3 throwVelocity)
	{
		if (hand == VRHand2)
		{
			VRHand2 = null;
			CaptureVROffsets();
			return;
		}
		if (hand != VRHand) return;
		VRHand = VRHand2;
		VRHand2 = null;
		if (VRHand != null)
		{
			CaptureVROffsets();
			return;
		}
		if (!_dragging) return;
		if (UseDragForce && Parent?.GDNode is RigidBody3D rigid3D)
		{
			rigid3D.LinearVelocity = throwVelocity;
		}
		ReleaseDrag(throwVelocity);
	}

	private void CaptureVROffsets()
	{
		if (_parent?.GDNode is not Node3D node) return;
		if (VRHand != null && Node.IsInstanceValid(VRHand))
		{
			_vrHandOffset = VRHand.GlobalTransform.AffineInverse() * node.GlobalTransform;
		}
		if (VRHand2 != null && Node.IsInstanceValid(VRHand2) && TwoHandFrame() is Transform3D frame)
		{
			_vrTwoHandOffset = frame.AffineInverse() * node.GlobalTransform;
		}
	}

	private Transform3D? TwoHandFrame()
	{
		Vector3 a = VRHand!.GlobalPosition;
		Vector3 b = VRHand2!.GlobalPosition;
		Vector3 dir = b - a;
		if (dir.LengthSquared() < 1e-6f) return null;
		dir = dir.Normalized();
		Vector3 side = dir.Cross(VRHand.GlobalTransform.Basis.Y);
		if (side.LengthSquared() < 1e-6f) side = dir.Cross(Vector3.Up);
		if (side.LengthSquared() < 1e-6f) side = dir.Cross(Vector3.Right);
		side = side.Normalized();
		return new Transform3D(new Basis(dir, side.Cross(dir), side), a);
	}

	private Transform3D VRTargetTransform()
	{
		if (VRHand2 != null && Node.IsInstanceValid(VRHand2) && TwoHandFrame() is Transform3D frame)
		{
			return frame * _vrTwoHandOffset;
		}
		return VRHand!.GlobalTransform * _vrHandOffset;
	}

	public override void EnterTree()
	{
		if (Parent is Physical phy)
		{
			_parent = phy;
			phy.Clicked.Connect(OnClicked);
			phy.MouseEnter.Connect(OnMouseEnter);
			phy.MouseExit.Connect(OnMouseExit);
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		_parent?.Clicked.Disconnect(OnClicked);
		_parent?.MouseEnter.Disconnect(OnMouseEnter);
		_parent?.MouseExit.Disconnect(OnMouseExit);
		_parent = null;
		base.ExitTree();
	}

	public override void Init()
	{
		base.Init();
		Root.Input.GodotInputEvent += OnInput;
		SetPhysicsProcess(true);
	}

	public override void PreDelete()
	{
		Root.Input.GodotInputEvent -= OnInput;
		base.PreDelete();
	}

	private void OnMouseEnter()
	{
		if (!_dragging)
		{
			Root.PlayerGUI.SetCursorShape(Control.CursorShape.Drag);
		}
	}

	private void OnMouseExit()
	{
		if (!_dragging)
		{
			Root.PlayerGUI.SetCursorShape(Control.CursorShape.Arrow);
		}
	}


	public void OnInput(InputEvent @event)
	{
		if (@event.IsActionReleased("activate"))
		{
			if (_dragging && VRHand == null)
			{
				ReleaseDrag();
			}
		}
	}

	private async void OnClicked(Player by)
	{
		if (_dragger != null) return;
		if (_parent != null)
		{
			// Check grabbable range
			if ((by.Position - _parent.Position).Length() > MaxGrabbableRange) return;
		}
		if (Root.Network.IsServer)
		{
			// If is server
			if (PermissionMode == GrabbablePermissionModeEnum.Everyone)
			{
				GiveDragTo(by);
			}
			else if (PermissionMode == GrabbablePermissionModeEnum.Scripted)
			{
				if (PermissionPredicate != null)
				{
					object?[] res = await PermissionPredicate.Call(by);
					if (res.Length != 1) return;
					if (res[0] is bool b && b)
					{
						GiveDragTo(by);
					}
				}
			}
		}
		else if (by == Root.Players.LocalPlayer)
		{
			// If is self
			if (PermissionMode == GrabbablePermissionModeEnum.Everyone)
			{
				InternalGiveGrab();
			}
		}
	}

	private void GiveDragTo(Player plr)
	{
		if (_parent == null) return;
		_dragger = plr;
		_parent.SetNetworkAuthority(plr);
		Grabbed.Invoke(plr);
		RpcId(plr.PeerID, nameof(NetGrabDrag));
	}

	private async void ReleaseDrag(Vector3? velocity = null)
	{
		Vector3 v = velocity ?? (Parent?.GDNode is RigidBody3D rigid3D ? rigid3D.LinearVelocity : Vector3.Zero);
		InternalReleaseDrag();
		Root.PlayerGUI.SetCursorShape(Control.CursorShape.Arrow);

		int seq = ++_releaseSeq;
		await Globals.Singleton.ToSignal(Globals.Singleton.GetTree().CreateTimer(ReleaseHandbackDelay), SceneTreeTimer.SignalName.Timeout);
		if (IsDeleted || _dragging || seq != _releaseSeq) return;

		if (Parent?.GDNode is RigidBody3D rb)
		{
			v = rb.LinearVelocity;
		}
		RpcId(1, nameof(NetDispatchReleaseDrag), v.X, v.Y, v.Z);
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetGrabDrag()
	{
		InternalGiveGrab();
	}

	internal void InternalGiveGrab()
	{
		_dragger = Root.Players.LocalPlayer;
		_dragging = true;
		Grabbed.Invoke(_dragger);
		Root.PlayerGUI.SetCursorShape(Control.CursorShape.CanDrop);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetDispatchReleaseDrag(float vx, float vy, float vz)
	{
		Player? p = Root.Players.GetPlayerFromPeerID(RemoteSenderId);

		if (p == _dragger)
		{
			InternalReleaseDrag();

			// Return authority to server
			_parent?.SetNetworkAuthority(null);

			if (UseDragForce && _parent?.GDNode is RigidBody3D rigid3D)
			{
				rigid3D.Sleeping = false;
				rigid3D.LinearVelocity = new Vector3(vx, vy, vz);
			}

			Rpc(nameof(NetReleaseDrag));
		}
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable)]
	private void NetReleaseDrag()
	{
		InternalReleaseDrag();
	}

	private void InternalReleaseDrag()
	{
		_dragging = false;
		_dragger = null;
		Released.Invoke();
	}

	public override void PhysicsProcess(double delta)
	{
		if (Parent == null) return;
		if (_dragger == null) return;

		// Set to null when deleted
		if (_dragger.IsDeleted) { _dragger = null; return; }

		// Process drag physics if enabled
		if (UseDragForce)
		{
			if (Parent.GDNode is RigidBody3D rigid3D)
			{
				if (_dragging && VRHand != null)
				{
					if (VRHand2 != null && !Node.IsInstanceValid(VRHand2)) VRHand2 = null;
					if (Node.IsInstanceValid(VRHand))
					{
						Transform3D target = VRTargetTransform();
						rigid3D.LinearVelocity = (target.Origin - rigid3D.GlobalPosition) * Mathf.Max(Force, VRFollowForce);
						rigid3D.AngularVelocity = RotationToVelocity(rigid3D.GlobalTransform.Basis, target.Basis);
					}
				}
				else if (_dragging)
				{
					Viewport viewport = Globals.Singleton.GetViewport();
					Camera3D camera = viewport.GetCamera3D();
					Camera? cam = Root.Environment.CurrentCamera;
					if (cam == null) return;
					Vector2 mousePos = Root.Input.MousePosition;
					Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
					Vector3 rayDir = camera.ProjectRayNormal(mousePos);

					Vector3? targetPos = null;

					if (cam.IsFirstPerson)
					{
						targetPos = rayOrigin + rayDir * MaxRange;
					}
					else
					{
						RayResult? hit = Root.Environment.Raycast(rayOrigin, rayDir, ignoreList: [Parent]);
						if (hit != null)
						{
							targetPos = hit.Value.Position;
						}
					}

					if (targetPos == null) return;

					Vector3 anchorPos = _dragger.Position;
					Vector3 direction = targetPos.Value - anchorPos;
					float distance = direction.Length();

					if (distance > MaxRange)
					{
						targetPos = anchorPos + direction.Normalized() * MaxRange;
					}

					Vector3 moveDirection = targetPos.Value - rigid3D.GlobalPosition;
					rigid3D.LinearVelocity = moveDirection * Force;
				}
			}
		}
		base.PhysicsProcess(delta);
	}

	private static Vector3 RotationToVelocity(Basis current, Basis target)
	{
		Quaternion dq = (target.Orthonormalized() * current.Orthonormalized().Inverse()).GetRotationQuaternion();
		if (dq.W < 0f) dq = new Quaternion(-dq.X, -dq.Y, -dq.Z, -dq.W);
		Vector3 axis = new(dq.X, dq.Y, dq.Z);
		float len = axis.Length();
		if (len < 1e-4f) return Vector3.Zero;
		return axis / len * (2f * Mathf.Acos(Mathf.Clamp(dq.W, -1f, 1f))) * VRRotateForce;
	}

	[ScriptEnum]
	public enum GrabbablePermissionModeEnum
	{
		None,
		Everyone,
		Scripted
	}
}
