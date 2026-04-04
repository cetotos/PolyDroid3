// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
#if CREATOR
using Polytoria.Creator.UI;
using Polytoria.Datamodel.Services;
using Polytoria.Creator.Spatial;
using Polytoria.Datamodel.Interfaces;
#endif
using Polytoria.Utils;
using System;
using System.Collections.Generic;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class Dynamic : Instance
{
	internal Node3D GDNode3D = null!;

	private const float MinScale = 0.001f;
	private const float LerpSpeed = 20;
	public event Action? TransformChanged;
	public event Action? ReliableTransformChanged;
	protected List<Node3D> excludedBoundNodes = [];
	private bool _hasSyncedOnce = false;
	private bool _locked;
	private bool _isFirstUpdate = true;
	private bool _isDirty = false;

#if CREATOR
	private Area3D _boundArea3D = null!;
	private CollisionShape3D _boundCollider = null!;
	private BoxShape3D _boundShape = null!;
	internal bool HasBound => _boundArea3D != null;
	internal Aabb CreatorBounds;
	private readonly static Dictionary<Node, Dynamic> _creatorProxyToDyn = [];
#endif

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Position
	{
		get
		{
			return GetGlobalPosition().Flip();
		}
		set
		{
			SetGlobalPosition(value.Flip());
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Rotation
	{
		get
		{
			Basis globalBasis = GetGlobalTransform().Basis;
			Quaternion q = globalBasis.GetRotationQuaternion();

			return MathUtils.Vector3RadToDeg(q.GetEuler()).FlipEuler();
		}
		set
		{
			GDNode3D.GlobalRotationDegrees = value.FlipEuler();
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, CloneIgnore, SaveIgnore]
	public Vector3 Size
	{
		get => GetGlobalTransform().Basis.Scale;
		set
		{
			Vector3 scale = new(
				Mathf.Max(value.X, MinScale),
				Mathf.Max(value.Y, MinScale),
				Mathf.Max(value.Z, MinScale)
			);

			Vector3 parentScale = GetParentScale();

			if (this is Part part)
			{
				if (Parent is Dynamic)
				{
					part.PartSize = scale;
				}
				else
				{
					part.PartSize = scale / parentScale; // Part size is local
				}
				part.RefreshUV1();
			}
			else
			{
				if (Parent is Dynamic)
				{
					GDNode3D.Scale = scale / parentScale;
				}
				else
				{
					GDNode3D.Scale = scale;
				}
			}
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, CloneIgnore, NoSync]
	public Vector3 LocalPosition
	{
		get
		{
			return GetLocalPosition().Flip();
		}
		set
		{
			SetLocalPosition(value.Flip());
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, CloneIgnore, NoSync]
	public Vector3 LocalRotation
	{
		get
		{
			return GDNode3D.RotationDegrees.FlipEuler();
		}
		set
		{
			GDNode3D.RotationDegrees = value.FlipEuler();
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, CloneIgnore, NoSync]
	public Vector3 LocalSize
	{
		get
		{
			if (this is Part part)
			{
				return part.PartSize;
			}
			return GDNode3D.Scale;
		}
		set
		{
			Vector3 scale = new(
				Mathf.Max(value.X, MinScale),
				Mathf.Max(value.Y, MinScale),
				Mathf.Max(value.Z, MinScale)
			);

			Vector3 parentScale = GetParentScale();

			if (this is Part part)
			{
				part.PartSize = scale * parentScale;
				part.RefreshUV1();
			}
			else
			{
				GDNode3D.Scale = scale;
			}
			if (AutoUpdateNetTransform)
			{
				UpdateNetTransformReliable();
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty, CloneIgnore, NoSync]
	public Quaternion Quaternion
	{
		get => GetGlobalTransform().Basis.GetRotationQuaternion().Flip();
		set
		{
			Quaternion q = value.Flip();
			GDNode3D.GlobalBasis = new(q);
			OnPropertyChanged();
		}
	}

	[ScriptProperty, CloneIgnore, NoSync]
	public Quaternion LocalQuaternion
	{
		get => GetLocalTransform().Basis.GetRotationQuaternion().Flip();
		set
		{
			Quaternion q = value.Flip();
			GDNode3D.Basis = new(q);
			OnPropertyChanged();
		}
	}

	[Editable(IsHidden = true), ScriptProperty, DefaultValue(false)]
	public bool Locked
	{
		get => _locked;
		set
		{
			_locked = value;

#if CREATOR
			Explorer.RefreshLocked(this);
#endif
			OnPropertyChanged();
		}
	}

	[SyncVar(AllowAuthorWrite = false, ServerOnly = true)]
	public int NetTransformAuthority { get; set; } = 1;

	[ScriptProperty] public Vector3 Forward => GetGlobalTransform().Basis.Z.Normalized().Flip();
	[ScriptProperty] public Vector3 Right => -GetGlobalTransform().Basis.X.Normalized().Flip();
	[ScriptProperty] public Vector3 Up => GetGlobalTransform().Basis.Y.Normalized().Flip();

	public override Node CreateGDNode()
	{
		return new Node3D();
	}

	public override void InitGDNode()
	{
		GDNode3D = (Node3D)GDNode;
		base.InitGDNode();
	}

	public override void PreDelete()
	{
		excludedBoundNodes.Clear();
#if CREATOR
		if (_boundArea3D != null)
		{
			_creatorProxyToDyn.Remove(_boundArea3D);
			if (Node.IsInstanceValid(_boundArea3D))
			{
				_boundArea3D.QueueFree();
				_boundArea3D.Dispose();
			}
		}
#endif
		base.PreDelete();
	}

	public override void Init()
	{
#if CREATOR
		CreateCreatorBounds();
#endif
		base.Init();
	}

	public override void Ready()
	{
		if (Root != null && Root.Network != null && Root.Network.IsServer)
		{
			UpdateNetTransformReliable();
		}
		base.Ready();
	}

	private Transform3D? _lastSentTransform;
	private Transform3D _netTransform;
	private Transform3D _currentTransform;
	private bool _lerpUnreliable = false;

	/// <summary>
	/// Set if netwwork transform will be update automatically once setter called
	/// set this to false if you update them manually every frame via UpdateNetTransform()
	/// </summary>
	public bool AutoUpdateNetTransform { get; internal set; } = true;

	/// <summary>
	/// Set to true if transform will be overrided, essentially ignoring network transform 
	/// </summary>
	public bool OverrideNetworkTransform { get; internal set; } = false;

	public void UpdateTransformTick(double delta)
	{
		if (!_lerpUnreliable) { return; }

		Transform3D old = _currentTransform;

		UpdateTransform(delta);

		if (_currentTransform != old)
		{
			InvokeTransformChanged();
		}
	}

	private void UpdateTransform(double delta)
	{
		if (!_isDirty) return;
		float positionDistance = _currentTransform.Origin.DistanceTo(_netTransform.Origin);

		// Check if this is the first update or if distance is too large
		if (_isFirstUpdate || positionDistance > 8f)
		{
			// Snap directly to target
			_currentTransform = _netTransform;
			_isFirstUpdate = false;
			_isDirty = false;
			SetLocalTransform(_currentTransform);
		}
		else
		{
			// Lerp position and rotation
			Vector3 newPosition = _currentTransform.Origin.Lerp(_netTransform.Origin, (float)(delta * LerpSpeed));
			Quaternion currentRotation = _currentTransform.Basis.GetRotationQuaternion();
			Quaternion targetRotation = _netTransform.Basis.GetRotationQuaternion();
			Quaternion newRotation = currentRotation.Slerp(targetRotation, (float)(delta * LerpSpeed));

			Vector3 newScale = _netTransform.Basis.Scale;

			_currentTransform = new Transform3D(new Basis(newRotation).Scaled(newScale), newPosition);

			// Check if close enough to snap to final position
			if (positionDistance < 0.01f && currentRotation.AngleTo(targetRotation) < 0.1f)
			{
				_isDirty = false;
				_currentTransform = _netTransform;
			}
			SetLocalTransform(_currentTransform);
		}
	}

	[ScriptMethod]
	public void LookAt(object target)
	{
		LookAt(target, Vector3.Up);
	}

	[ScriptMethod]
	public void LookAt(object target, Vector3 up)
	{
		Vector3 pos;
		if (target is Vector3 targetPos)
		{
			pos = targetPos;
		}
		else if (target is Dynamic dyn)
		{
			pos = dyn.Position;
		}
		else
		{
			throw new InvalidOperationException("LookAt Target is invalid");
		}

		GDNode3D.LookAt(pos.Flip(), up);

		// switch coordinates system 
		GDNode3D.RotateY(Mathf.Pi);
		GDNode3D.RotationDegrees *= new Vector3(-1, 1, 1);

		UpdateNetTransformReliable();
	}

	[ScriptMethod]
	public void Translate(Vector3 translation)
	{
		SetGlobalTransform(GetGlobalTransform().Translated(translation));
		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	[ScriptMethod]
	public void RotateAround(Vector3 point, Vector3 axis, float angle)
	{
		Transform3D transform = GetGlobalTransform();

		transform.Origin -= point;

		Basis rotation = new(axis.Normalized(), Mathf.DegToRad(angle));

		transform.Basis = rotation * transform.Basis;
		transform.Origin = rotation.Xform(transform.Origin);

		transform.Origin += point;

		SetGlobalTransform(transform);

		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	[ScriptMethod]
	public void Rotate(Vector3 eulerAngles)
	{
		Vector3 radians = eulerAngles * Mathf.DegToRad(1.0f);

		GDNode3D.RotateObjectLocal(Vector3.Right, radians.X);
		GDNode3D.RotateObjectLocal(Vector3.Up, radians.Y);
		GDNode3D.RotateObjectLocal(Vector3.Back, radians.Z);

		if (AutoUpdateNetTransform)
		{
			UpdateNetTransformReliable();
		}
	}

	// NOTE: Update operations needs transform to be force updated as godot does not update them instantly
	protected void UpdateNetTransform()
	{
		if (Root == null || Root.Network == null) return;

		GDNode3D.ForceUpdateTransform();
		Transform3D current = GetLocalTransform();

		_lastSentTransform = current;

		InvokeTransformChanged();
		SendNetTransformUnreliable();
	}

	protected void UpdateNetTransformReliable()
	{
		if (Root == null || Root.Network == null) return;

		GDNode3D.ForceUpdateTransform();
		Transform3D current = GetLocalTransform();

		// Only send if changed
		if (_lastSentTransform == null || !_lastSentTransform.Value.IsEqualApprox(current))
		{
			_lastSentTransform = current;

			InvokeTransformChanged();
			if (!Root.IsLoaded) return;
			SendNetTransformReliable();
		}
	}

	protected void SendNetTransformUnreliable(bool lerp = true)
	{
		if (Root == null || Root?.Network == null) { return; }

		UpdateCurrentTransformCache();

		GDNode3D.ForceUpdateTransform();
		if (!Root.Network.IsServer)
		{
			// Send transform to server
			Root.Network.TransformSync.SendTransformToServer(this, lerp);
		}
		else
		{
			// Server broadcasts to all clients
			Root.Network.TransformSync.BroadcastTransformFromServer(this, lerp);
		}
	}

	protected void SendNetTransformReliable(bool lerp = false)
	{
		if (Root == null || Root?.Network == null) return;
		_lerpUnreliable = false;
		UpdateCurrentTransformCache();
		ReliableTransformChanged?.Invoke();
		Root.Network.TransformSync.SendUpdateTransform(this, true, 0, lerp);
	}

	/// <summary>
	/// Must be called after manual transform update
	/// </summary>
	internal void UpdateCurrentTransformCache()
	{
		Transform3D newt = GetLocalTransform();
		if (newt != _currentTransform)
		{
			if (_hasSyncedOnce)
			{
				if (this is Part part)
				{
					part.RefreshUV1();
				}

				InvokeTransformChanged();
			}
			else
			{
				_hasSyncedOnce = true;
			}
		}
		_currentTransform = newt;
	}

	/// <summary>
	/// Function for processing transform, can be used for sanity checks
	/// </summary>
	/// <param name="fromPeer"></param>
	/// <param name="newTransform"></param>
	/// <returns></returns>
	internal virtual Transform3D TransformNetworkPass(int fromPeer, Transform3D newTransform)
	{
		return newTransform;
	}

	internal virtual bool TransformNetworkCheck(Transform3D newTransform)
	{
		return true;
	}

	internal void UpdateTransformFromNet(Transform3D transform, bool isReliable, bool lerpTransform)
	{
		if (OverrideNetworkTransform) return;
		_netTransform = transform;
		_isDirty = true;

		// temporary set to disable lerping on non player
		// object seems to glitch weirdly when lerping
		// TODO: come back and fix this
		if (Root.Network.IsServer || !lerpTransform)
		{
			_lerpUnreliable = false;
			_currentTransform = transform;
			SetLocalTransform(transform);
		}
		else if (lerpTransform && !Root.Network.IsServer)
		{
			_lerpUnreliable = true;
		}

		if (isReliable)
		{
			ReliableTransformChanged?.Invoke();
		}

		if (this is Part part)
		{
			part.RefreshUV1();
		}

		InvokeTransformChanged();
	}

#if CREATOR
	private void CreateCreatorBounds()
	{
		if (Root == null) return;
		if (Root.Network.NetworkMode != NetworkService.NetworkModeEnum.Creator) return;

		_boundArea3D = new()
		{
			Monitorable = true,
			Monitoring = false,
		};
		SetCreatorBoundActive(true);
		_creatorProxyToDyn[_boundArea3D] = this;

		_boundShape = new();

		_boundCollider = new()
		{
			Shape = _boundShape
		};

		_boundArea3D.AddChild(_boundCollider);
		Root.Environment.GDNode.AddChild(_boundArea3D);

		UpdateCreatorBounds();
	}

	internal void UpdateCreatorBounds()
	{
		if (_boundShape == null) return;
		if (Root == null) return;
		if (Root.Network.NetworkMode != NetworkService.NetworkModeEnum.Creator) return;

		Aabb bound = CalculateBounds();

		CreatorBounds = bound;

		_boundShape.Size = bound.Size;
		_boundCollider.Position = bound.GetCenter();
	}

	public static Dynamic? GetDynFromCreatorBounds(Node collider)
	{
		if (_creatorProxyToDyn.TryGetValue(collider, out Dynamic? dyn)) return dyn;
		return null;
	}

	internal Rid GetBoundRid()
	{
		return _boundArea3D.GetRid();
	}

	internal void RefreshCreatorBound()
	{
		UpdateCreatorBounds();
		SetCreatorBoundActive(!IsHidden);
	}

	private void SetCreatorBoundActive(bool to)
	{
		if (_boundArea3D == null) return;
		// Ignore model/physical model and camera
		if (to && this is not IGroup and not Camera)
		{
			if (this is Physical p && p.CanCollide)
			{
				_boundArea3D.CollisionLayer = (1 << 3);
			}
			else
			{
				_boundArea3D.CollisionLayer = (1 << 2);
			}
		}
		else
		{
			_boundArea3D.CollisionLayer = 0;
		}
	}

	internal void PropagateUpdateCreatorBounds()
	{
		foreach (Instance item in GetChildren())
		{
			if (item is Dynamic dyn)
			{
				dyn.PropagateUpdateCreatorBounds();
				dyn.UpdateCreatorBounds();
			}
		}
		UpdateCreatorBounds();
	}
#endif

	internal void InvokeTransformChanged()
	{
#if CREATOR
		if (Root.CreatorContext != null && Root.CreatorContext.Gizmos != null)
		{
			if (!Root.CreatorContext.Gizmos.HoveringGizmos && !Root.CreatorContext.Gizmos.IsDraggingDynamic)
			{
				// Update creator bounds if not changed by gizmos
				UpdateCreatorBounds();
			}
		}
#endif

		OnPropertyChanged(nameof(Position));
		OnPropertyChanged(nameof(Rotation));
		OnPropertyChanged(nameof(Size));
		OnPropertyChanged(nameof(LocalPosition));
		OnPropertyChanged(nameof(LocalRotation));
		OnPropertyChanged(nameof(LocalSize));

		TransformChanged?.Invoke();
		foreach (Instance item in GetDescendants())
		{
			if (item is Dynamic dyn)
			{
				dyn.InvokeTransformChanged();
			}
		}

		// Destroy entity/physicalModel under part destroy height
		if (Root != null && Root.Environment != null && this is Entity or PhysicalModel)
		{
			if (Position.Y <= Root.Environment.PartDestroyHeight)
			{
				Delete();
			}
		}
	}

	public override void HiddenChanged(bool to)
	{
		// Player cannot be hidden
		if (this is Player) return;

		GDNode3D.Visible = !to;

#if CREATOR
		if (_boundArea3D != null)
		{
			_boundArea3D.Monitorable = !to;
			RefreshCreatorBound();
		}
#endif

		base.HiddenChanged(to);
	}

	internal Vector3 GetGlobalPosition()
	{
		return GetGlobalTransform().Origin;
	}

	internal void SetGlobalPosition(Vector3 to)
	{
		var t = GetGlobalTransform();
		SetGlobalTransform(new Transform3D(t.Basis, to));
	}

	internal Vector3 GetLocalPosition()
	{
		return GetLocalTransform().Origin;
	}

	internal void SetLocalPosition(Vector3 to)
	{
		var t = GetLocalTransform();
		SetLocalTransform(new Transform3D(t.Basis, to));
	}

	internal Transform3D GetGlobalTransform()
	{
		var t = GDNode3D.GlobalTransform;
		if (this is Part part)
		{
			var rotation = t.Basis.Orthonormalized();
			var scaledBasis = new Basis(
				rotation.Column0 * part.PartSize.X,
				rotation.Column1 * part.PartSize.Y,
				rotation.Column2 * part.PartSize.Z
			);
			return new Transform3D(scaledBasis, t.Origin);
		}
		return t;
	}

	internal Transform3D GetLocalTransform()
	{
		var t = GDNode3D.Transform;
		if (this is Part part)
		{
			var scale = part.PartSize * GetParentScale();
			var rotation = t.Basis.Orthonormalized();
			var scaledBasis = new Basis(
				rotation.Column0 * scale.X,
				rotation.Column1 * scale.Y,
				rotation.Column2 * scale.Z
			);
			return new Transform3D(scaledBasis, t.Origin);
		}
		return t;
	}

	internal void SetGlobalTransform(Transform3D to)
	{
		if (this is Part part)
		{
			part.PartSize = (Parent is Dynamic) ? to.Basis.Scale : to.Basis.Scale / GetParentScale();
			GDNode3D.GlobalTransform = new Transform3D(to.Basis.Orthonormalized(), to.Origin);
		}
		else
		{
			GDNode3D.GlobalTransform = to;
		}
		UpdateCurrentTransformCache();
	}

	internal void SetLocalTransform(Transform3D to)
	{
		if (this is Part part)
		{
			part.PartSize = (Parent is Dynamic) ? to.Basis.Scale : to.Basis.Scale / GetParentScale();
			GDNode3D.Transform = new Transform3D(to.Basis.Orthonormalized(), to.Origin);
		}
		else
		{
			GDNode3D.Transform = to;
		}
		UpdateCurrentTransformCache();
	}

	private Vector3 GetParentScale()
	{
		if (Parent is Dynamic p && p.GDNode3D.IsInsideTree())
			return p.GetGlobalTransform().Basis.Scale;
		return Vector3.One;
	}

	internal void ForceUpdateTransform()
	{
		GDNode3D.ForceUpdateTransform();
	}

	[ScriptMethod]
	public Aabb GetBounds()
	{
		return CalculateBounds();
	}

	internal void SetVisualMaskLayer(int layer, bool to)
	{
		foreach (Node item in GetDescendantsInternal(GDNode))
		{
			if (item is VisualInstance3D v)
			{
				v.SetLayerMaskValue(layer, to);
			}
		}
	}

	internal Aabb CalculateBounds()
	{
		Aabb? bounds = null;

		Instance[] all = [this, .. GetDescendants()];

		foreach (Instance item in all)
		{
			if (item is Part part)
			{
				Transform3D t = part.GetGlobalTransform();

				Vector3 localSize = part.Size;
				Vector3 he = localSize / 2f;

				Vector3 basisScale = t.Basis.Scale;

				// get pure rotation matrix
				Basis rot = t.Basis;
				rot.X /= basisScale.X;
				rot.Y /= basisScale.Y;
				rot.Z /= basisScale.Z;

				// some dark magic
				Vector3 worldExtents = new(
					Mathf.Abs(rot.X.X) * he.X + Mathf.Abs(rot.Y.X) * he.Y + Mathf.Abs(rot.Z.X) * he.Z,
					Mathf.Abs(rot.X.Y) * he.X + Mathf.Abs(rot.Y.Y) * he.Y + Mathf.Abs(rot.Z.Y) * he.Z,
					Mathf.Abs(rot.X.Z) * he.X + Mathf.Abs(rot.Y.Z) * he.Y + Mathf.Abs(rot.Z.Z) * he.Z
				);

				Vector3 center = t.Origin;

				Aabb pBounds = new(center - worldExtents, worldExtents * 2);


				if (bounds == null)
				{
					bounds = pBounds;
				}
				else
				{
					bounds = bounds.Value.Merge(pBounds);
				}
			}
			else if (item is Light l)
			{
				Transform3D t = l.GetGlobalTransform();

				Aabb pBounds = new(t.Origin - Vector3.One, Vector3.One * 2);

				if (bounds == null)
				{
					bounds = pBounds;
				}
				else
				{
					bounds = bounds.Value.Merge(pBounds);
				}
			}
			else if (item is Dynamic dyn)
			{
				Node[] scanNodes = [GDNode3D, .. GetNonInstanceDescendants(dyn.GDNode3D)];

				foreach (Node n in scanNodes)
				{
#if CREATOR
					if (n is ISpatial) continue;
#endif
					if (n is VisualInstance3D v3d)
					{
						if (!v3d.IsVisibleInTree())
						{
							continue;
						}

						bool shouldExclude = false;
						foreach (Node3D excludedNode in excludedBoundNodes)
						{
							if (v3d == excludedNode || v3d.IsDescendantOf(excludedNode))
							{
								shouldExclude = true;
								break;
							}
						}

						if (shouldExclude)
						{
							continue;
						}

						if (!v3d.IsInsideTree())
						{
							continue;
						}

						Aabb vBounds = v3d.GlobalTransform * v3d.GetAabb();
						if (vBounds.Size == Vector3.Zero)
						{
							continue;
						}

						if (bounds == null)
						{
							bounds = vBounds;
						}
						else
						{
							bounds = bounds.Value.Merge(vBounds);
						}
					}
				}
			}
		}

		return bounds ?? new(GetGlobalPosition(), Vector3.One * 0.5f);
	}

	public virtual Aabb GetSelfBound() { return default; }
}
