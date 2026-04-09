// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using static Polytoria.Datamodel.Services.NetworkService;

namespace Polytoria.Datamodel;

[Abstract]
public partial class Physical : Dynamic
{
	private const float SyncInterval = 0.025f;
	private const float TouchedGapCheck = 20f;
	private bool _anchored = true;
	private bool _canCollide = true;
	private Vector3 _velocity = Vector3.Zero;
	private Vector3 _angularVelocity = Vector3.Zero;
	internal Area3D PhysicalArea = null!;

	private int _touchedListenerCount = 0;
	private bool _canTouch = false;

	private double _syncClock = 0;

	private readonly HashSet<Physical> _touchedBy = [];
	private static readonly Dictionary<Node, Physical> _proxyToPhysical = [];

	public List<CollisionShape3D> CollisionShapes = [];
	public List<CollisionShape3D> AreaCollisionShapes = [];
	public List<CollisionShape3D> CollisionRootShapes = [];

	[ScriptProperty] public PTSignal<Physical> Touched { get; private set; } = new();
	[ScriptProperty] public PTSignal<Physical> TouchEnded { get; private set; } = new();
	[ScriptProperty] public PTSignal MouseEnter { get; private set; } = new();
	[ScriptProperty] public PTSignal MouseExit { get; private set; } = new();
	[ScriptProperty] public PTSignal<Player> Clicked { get; private set; } = new();

	public event Action<CollisionShape3D>? CollisionShapeAdded;
	public event Action<CollisionShape3D>? CollisionShapeRemoved;

	[Editable, ScriptProperty]
	public virtual bool Anchored
	{
		get => _anchored;
		set
		{
			bool oldVal = _anchored;
			_anchored = value;

			if (oldVal != _anchored)
			{
				UpdateFreeze();
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public virtual bool CanCollide
	{
		get => _canCollide;
		set
		{
			_canCollide = value;

			UpdateCollision();

			OnPropertyChanged();
		}
	}

	internal void UpdateFreeze()
	{
		bool finalVal = _anchored;

		if (Root != null && Root.Network != null)
		{
			if (Root.Network.NetworkMode == NetworkModeEnum.Creator || !Root.IsLoaded)
			{
				finalVal = true;
			}

			// Freeze the object on non physics authority
			if (Root.Network.NetworkMode == NetworkModeEnum.Client && Root.Network.LocalPeerID != NetTransformAuthority && ExistInNetwork)
			{
				finalVal = true;
			}

			if (IsHidden)
			{
				finalVal = true;
			}
		}

		ApplyFreeze(finalVal);

		if (Root != null && Root.Network != null)
		{
			// Ignore player
			if (Root.Network.IsServer && this is not Player)
			{
				AutoUpdateNetTransform = finalVal;
			}
		}

		if (!OverridePhysicsProcess)
		{
			SetPhysicsProcess(!_anchored);
		}
	}

	protected virtual void ApplyFreeze(bool to) { }

	internal void UpdateCollision()
	{
		if (IsDeleted) return;
		if (OverrideCanCollide)
		{
			SetCollisionDisabled(!OverrideCanCollideTo);
			return;
		}

		// Set each collision
		if (!IsHidden)
		{
			// Stop collision override if player's not ready
			if (this is Player plr && !plr.IsReady) { return; }
			SetCollisionDisabled(!_canCollide);
		}
		else
		{
			SetCollisionDisabled(true);
		}

#if CREATOR
		RefreshCreatorBound();
#endif
	}

	internal void SetCollisionDisabled(bool disabled)
	{
		foreach (CollisionShape3D c in CollisionShapes.ToArray())
		{
			if (!Node.IsInstanceValid(c)) continue;
			c.Disabled = disabled;
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public virtual Vector3 Velocity
	{
		get
		{
			if (this is NPC npc)
			{
				return npc.CharacterVelocity.Flip();
			}
			else if (this is Entity e)
			{
				return e.RigidBody.LinearVelocity.Flip();
			}
			else if (this is PhysicalModel phm)
			{
				return phm.RigidBody.LinearVelocity.Flip();
			}

			return _velocity;
		}
		set
		{
			_velocity = value;

			var setto = _velocity.Flip();

			if (this is Player plr)
			{
				plr.LastVelocity = _velocity;
			}

			if (this is NPC npc)
			{
				npc.CharacterVelocity = setto;
			}
			else if (this is Entity e)
			{
				e.RigidBody.LinearVelocity = setto;
			}
			else if (this is PhysicalModel phm)
			{
				phm.RigidBody.LinearVelocity = setto;
			}

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public virtual Vector3 AngularVelocity
	{
		get
		{
			if (this is Entity e)
			{
				return e.RigidBody.AngularVelocity;
			}
			else if (this is PhysicalModel phm)
			{
				return phm.RigidBody.AngularVelocity;
			}

			return _angularVelocity;
		}
		set
		{
			_angularVelocity = value;

			if (this is Entity e)
			{
				e.RigidBody.AngularVelocity = _angularVelocity;
			}
			else if (this is PhysicalModel phm)
			{
				phm.RigidBody.AngularVelocity = _angularVelocity;
			}

			OnPropertyChanged();
		}
	}

	public PhysicalModel? PhysicalRoot { get; private set; }

	internal bool OverrideCanCollide = false;
	internal bool OverrideCanCollideTo = false;
	internal bool OverridePhysicsProcess = false;

	public override void HiddenChanged(bool to)
	{
		UpdateCollision();

		foreach (CollisionShape3D c in AreaCollisionShapes)
		{
			c.Disabled = to;
		}

		base.HiddenChanged(to);
	}

	public override void EnterTree()
	{
		Instance? current = Parent;
		while (current != null)
		{
			Type ct = current.GetType();
			if (Parent is PhysicalModel pr)
			{
				PhysicalRoot = pr;
				break;
			}
			if (ct.IsDefined(typeof(PhysicalRootStopAttribute), false))
			{
				break;
			}
			current = current.Parent;
		}
		base.EnterTree();

		foreach (CollisionShape3D item in CollisionShapes.ToArray())
		{
			PostCollisionShapeUpdate(item);
		}
	}

	public override void Init()
	{
		PhysicalArea = new()
		{
			Monitorable = true,
			Monitoring = _canTouch
		};
		PhysicalArea.SetCollisionMaskValue(2, true);
		PhysicalArea.AreaEntered += AreaEntered;
		PhysicalArea.AreaExited += AreaExited;

		// A little bit bigger
		PhysicalArea.Scale = new(1.01f, 1.01f, 1.01f);

		_proxyToPhysical.Add(PhysicalArea, this);
		GDNode3D.AddChild(PhysicalArea, false, Node.InternalMode.Front);

		Touched.Subscribed += OnTouchSubscribed;
		Touched.Unsubscribed += OnTouchUnsubscribed;

		TouchEnded.Subscribed += OnTouchSubscribed;
		TouchEnded.Unsubscribed += OnTouchUnsubscribed;

		base.Init();
		if (this is Entity e)
		{
			e.RigidBody.GravityScale = 2;
		}

		if (Root != null)
		{
			if (Root.IsLoaded)
			{
				OnRootReady();
			}
			else
			{
				Root.Loaded.Once(OnRootReady);
			}
		}

		// init area3d shapes
		foreach (CollisionShape3D item in CollisionShapes.ToArray())
		{
			CreateAreaShape(item);
		}
	}

	public override void PreDelete()
	{
		Root?.Loaded.Disconnect(OnRootReady);
		_proxyToPhysical.Remove(PhysicalArea);

		AreaCollisionShapes.Clear();
		CollisionRootShapes.Clear();
		CollisionShapes.Clear();
		_touchedBy.Clear();

		PhysicalArea.AreaEntered -= AreaEntered;
		PhysicalArea.AreaExited -= AreaExited;

		Touched.Subscribed -= OnTouchSubscribed;
		Touched.Unsubscribed -= OnTouchUnsubscribed;

		TouchEnded.Subscribed -= OnTouchSubscribed;
		TouchEnded.Unsubscribed -= OnTouchUnsubscribed;

		base.PreDelete();
	}

	public override void Ready()
	{
		UpdateCollision();
		UpdateFreeze();
		base.Ready();
	}

	private void OnTouchSubscribed()
	{
		_touchedListenerCount++;
		EnableCanTouch();
	}

	private void OnTouchUnsubscribed()
	{
		_touchedListenerCount--;
		if (_touchedListenerCount <= 0)
		{
			DisableCanTouch();
		}
	}

	private void OnRootReady()
	{
		UpdateFreeze();
	}

	internal void EnableCanTouch()
	{
		if (!_canTouch)
		{
			_canTouch = true;
			PT.CallOnMainThread(() =>
			{
				PhysicalArea.Monitoring = true;
			});
		}
	}

	internal void DisableCanTouch()
	{
		if (_canTouch)
		{
			_canTouch = false;
			PT.CallOnMainThread(() =>
			{
				PhysicalArea.Monitoring = false;
			});
		}
	}

	protected void UpdateVelocityInternal(Vector3 vel)
	{
		_velocity = vel;
	}

	public override void PhysicsProcess(double delta)
	{
		UpdateTransformTick(delta);
		if (Root == null || Root?.Network == null) { return; }

		_syncClock += delta;

		// Sync if has authority and not anchored, if so. sync in interval
		if (NetTransformAuthority == Root.Network.LocalPeerID && !Anchored && _syncClock > SyncInterval)
		{
			_syncClock = 0;
			UpdateNetTransform();
		}
		base.PhysicsProcess(delta);
	}

	[ScriptMethod]
	public void SetNetworkAuthority(Player? plr)
	{
		if (!Root.Network.IsServer) throw new InvalidOperationException("Set authority can only be called from server");
		Rpc(nameof(NetSetAuthority), plr?.PeerID ?? 1);
	}

	[NetRpc(AuthorityMode.Server, TransferMode = TransferMode.Reliable, CallLocal = true)]
	private void NetSetAuthority(int id)
	{
		NetTransformAuthority = id;
		SetNetworkAuthority(id, false);
		UpdateFreeze();
	}

	/// <summary>
	/// Add collision shape, this is used for mirroring collision shapes to other body types
	/// </summary>
	/// <param name="collisionShape"></param>
	protected void AddCollisionShape(CollisionShape3D collisionShape)
	{
		if (CollisionShapes.Contains(collisionShape)) return;
		CollisionShapes.Add(collisionShape);
		CollisionShapeAdded?.Invoke(collisionShape);

		CreateAreaShape(collisionShape);
	}

	/// <summary>
	/// This function must be called if collision shape has been updated
	/// </summary>
	/// <param name="collisionShape">Target collision shape</param>
	protected void PostCollisionShapeUpdate(CollisionShape3D collisionShape)
	{
		RemoveCollisionShape(collisionShape, false);
		AddCollisionShape(collisionShape);
	}

	/// <summary>
	/// Remove collision shape from the mirror
	/// </summary>
	/// <param name="collisionShape">Target collision shape</param>
	/// <param name="free">Free the shape now?</param>
	protected void RemoveCollisionShape(CollisionShape3D collisionShape, bool free = true)
	{
		if (!CollisionShapes.Contains(collisionShape)) return;
		CollisionShapes.Remove(collisionShape);

		if (collisionShape.HasMeta("_area_nodes"))
		{
			var createdNodes = collisionShape.GetMeta("_area_nodes").As<Godot.Collections.Array>();

			if (createdNodes != null)
			{
				foreach (var nodeVariant in createdNodes)
				{
					Node node = nodeVariant.As<Node>();
					if (node != null && Node.IsInstanceValid(node))
					{
						// Remove from AreaCollisionShapes
						if (node is CollisionShape3D shape)
						{
							AreaCollisionShapes.Remove(shape);
						}

						node.QueueFree();
					}
				}
			}

			collisionShape.RemoveMeta("_area_nodes");
		}

		if (collisionShape.GetParentOrNull<Node>() != GDNode)
		{
			collisionShape.Reparent(GDNode);
		}

		CollisionShapeRemoved?.Invoke(collisionShape);

		if (free)
		{
			collisionShape.QueueFree();
		}
	}

	protected void ClearCollisionShapes()
	{
		foreach (CollisionShape3D collision in CollisionShapes.ToArray())
		{
			RemoveCollisionShape(collision);
		}
	}


	private void CreateAreaShape(CollisionShape3D origin)
	{
		if (PhysicalArea == null || !Node.IsInstanceValid(origin)) return;

		Shape3D sharedShape = origin.Shape;
		Godot.Collections.Array<Node> createdNodes = [];

		CollisionShape3D CreateLinkedShape(Node parent)
		{
			// Create Node3D for scaling
			Node3D scaleNode = new()
			{
				Scale = new(1.01f, 1.01f, 1.01f)
			};

			CollisionShape3D newShape = new()
			{
				Shape = sharedShape,
				Disabled = IsHidden,
			};

			parent.AddChild(newShape);
			createdNodes.Add(newShape);

			RemoteTransform3D rt = new()
			{
				UseGlobalCoordinates = true
			};
			scaleNode.AddChild(rt);
			createdNodes.Add(scaleNode);

			if (origin.HasMeta("_remote_at"))
			{
				origin.GetMeta("_remote_at").As<Node>()?.AddChild(scaleNode);
			}
			else
			{
				GDNode.AddChild(scaleNode);
			}

			// Apply manual offset if found
			if (origin.HasMeta("_remote_offset"))
			{
				scaleNode.Position = origin.GetMeta("_remote_offset").As<Vector3>();
			}
			else
			{
				scaleNode.Position = Vector3.Zero;
			}

			rt.RemotePath = rt.GetPathTo(newShape);
			return newShape;
		}

		RemoteTransform3D CreateRemoteTransform(Node target)
		{
			Node3D scaleNode = new();

			RemoteTransform3D rt = new()
			{
				UseGlobalCoordinates = true
			};

			if (origin.HasMeta("_remote_at"))
			{
				origin.GetMeta("_remote_at").As<Node>()?.AddChild(scaleNode);
			}
			else
			{
				GDNode.AddChild(scaleNode);
			}

			scaleNode.AddChild(rt);

			// Apply manual offset if found
			if (origin.HasMeta("_remote_offset"))
			{
				scaleNode.Position = origin.GetMeta("_remote_offset").As<Vector3>();
			}
			else
			{
				scaleNode.Position = Vector3.Zero;
			}

			rt.RemotePath = rt.GetPathTo(target);
			createdNodes.Add(rt);
			return rt;
		}

		var areaShape = CreateLinkedShape(PhysicalArea);
		AreaCollisionShapes.Add(areaShape);

		// Handle Physical Root
		if (PhysicalRoot != null)
		{
			origin.Reparent(PhysicalRoot.GDNode);
			origin.GlobalTransform = GetGlobalTransform();
			var areaShape2 = CreateLinkedShape(PhysicalRoot.PhysicalArea);
			AreaCollisionShapes.Add(areaShape2);
		}

		CreateRemoteTransform(origin);

		// Store all created nodes in origin's metadata for cleanup
		origin.SetMeta("_area_nodes", createdNodes);
	}

	public static Physical? GetPhysicalFromCollider(Node collider)
	{
		if (_proxyToPhysical.TryGetValue(collider, out Physical? val)) return val;
		return null;
	}

	internal void ApplyForceFromPlayer(Vector3 force)
	{
		if (Anchored) return;
		if (this is not Entity) return;
		((RigidBody3D)GDNode).ApplyCentralImpulse(force);
	}

	private void AreaEntered(Area3D area)
	{
		Physical? p = GetPhysicalFromCollider(area);
		if (p != null)
		{
			InternalInvokeTouched(p);
		}
	}

	private void AreaExited(Area3D area)
	{
		Physical? p = GetPhysicalFromCollider(area);
		if (p != null)
		{
			InternalInvokeTouchEnded(p);
		}
	}

	internal Rid GetRid()
	{
		if (PhysicalArea != null)
		{
			return PhysicalArea.GetRid();
		}
		return ((CollisionObject3D)GDNode).GetRid();
	}

	internal void InvokeTouched(Physical hit)
	{
		//if (!IsInstanceValid(this) || !IsInsideTree()) return;
		InternalInvokeTouched(hit);
		Rpc(nameof(NetInvokeTouched), hit.NetworkedObjectID);
	}

	internal void InvokeTouchEnded(Physical hit)
	{
		InternalInvokeTouchEnded(hit);
		Rpc(nameof(NetInvokeTouchEnded), hit.NetworkedObjectID);
	}

	internal void InvokeClicked(Player by)
	{
		InternalInvokeClicked(by);
		RpcId(1, nameof(NetInvokeClicked), by.NetworkedObjectID);
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, AllowToServerOnly = false)]
	private void NetInvokeTouched(string touchedBy)
	{
		NetworkedObject? hit = Root.GetNetObjectFromID(touchedBy);

		// Only allow player hit invoke
		if (hit != null && hit is Player plr && !plr.IsDead)
		{
			// Ignore invalid touches (touches that are out of range)
			if (!IsTouchedValid(plr)) return;

			InternalInvokeTouched(plr);
		}
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable, AllowToServerOnly = false)]
	private void NetInvokeTouchEnded(string touchedBy)
	{
		NetworkedObject? hit = Root.GetNetObjectFromID(touchedBy);

		// Only allow player hit invoke
		if (hit != null && hit is Player plr)
		{
			InternalInvokeTouchEnded(plr);
		}
	}

	private bool IsTouchedValid(Player plr)
	{
		// Check if player position is in vaild range
		return GetSelfBound().Grow(TouchedGapCheck).HasPoint(plr.GetGlobalPosition());
	}

	[NetRpc(AuthorityMode.Any, TransferMode = TransferMode.Reliable)]
	private void NetInvokeClicked(string touchedBy)
	{
		NetworkedObject? hit = Root.GetNetObjectFromID(touchedBy);

		// Only allow player hit invoke
		if (hit != null && hit is Player plr)
		{
			InternalInvokeClicked(plr);
		}
	}

	private void InternalInvokeTouched(Physical physical)
	{
		if (_touchedBy.Contains(physical)) return;
		// Ignore dead NPCs, their position could be inaccurate
		if (physical is NPC npc && npc.IsDead) return;

		// Ignore player that's not ready
		if (physical is Player plr && !plr.IsReady) return;

		_touchedBy.Add(physical);

		Touched.Invoke(physical);
	}

	private void InternalInvokeTouchEnded(Physical physical)
	{
		_touchedBy.Remove(physical);

		// Ignore player that's not ready
		if (physical is Player plr && !plr.IsReady) return;

		TouchEnded.Invoke(physical);
	}

	private void InternalInvokeClicked(Player by)
	{
		Clicked.Invoke(by);
	}

	[ScriptMethod]
	public Physical[] GetTouching()
	{
		EnableCanTouch();
		List<Physical> phys = [];
		var area3ds = PhysicalArea.GetOverlappingAreas();
		foreach (Area3D item in area3ds)
		{
			Physical? phy = GetPhysicalFromCollider(item);
			if (phy != null)
			{
				phys.Add(phy);
			}
		}
		return [.. phys];
	}

	[ScriptMethod]
	public void MovePosition(Vector3 position)
	{
		Position += position;
	}

	[ScriptMethod]
	public void MoveRotation(Vector3 rotation)
	{
		Rotation += rotation;
	}
}
