// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Networking;
using Polytoria.Utils;
using System;

namespace Polytoria.Datamodel;

[Abstract]
public abstract partial class Entity : Physical
{
	private const float VelocitySyncInterval = 0.4f;
	private const float MinMass = 0.01f;

	internal RigidBody3D RigidBody = null!;
	internal PhysicsMaterial PhysicsMat = null!;

	private Vector3 _lastVelocity;
	private Vector3 _lastAngularVelocity;
	private double _velocityClock = 0;

	private bool _useGravity = true;
	private bool _isSpawn = false;
	private float _mass;
	private float _friction;
	private float _drag;
	private float _angularDrag;
	private float _bounciness;

	private Color _color = new(1, 1, 1);
	private bool _castShadows = true;

	[Editable, ScriptProperty]
	public virtual Color Color
	{
		get => _color;
		set
		{
			_color = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public virtual bool CastShadows
	{
		get => _castShadows;
		set
		{
			_castShadows = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(false)]
	public bool IsSpawn
	{
		get => _isSpawn;
		set
		{
			_isSpawn = value;

			if (_isSpawn)
			{
				Root.Environment.RegisterSpawnPoint(this);
			}
			else
			{
				Root.Environment.UnregisterSpawnPoint(this);
			}
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public bool UseGravity
	{
		get => _useGravity;
		set
		{
			_useGravity = value;

			RigidBody.GravityScale = value ? 2 : 0;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public float Mass
	{
		get => _mass;
		set
		{
			_mass = value;

			RigidBody.Mass = Math.Max(_mass, MinMass);

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.6f)]
	public float Friction
	{
		get => _friction;
		set
		{
			_friction = value;
			PhysicsMat.Friction = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float Drag
	{
		get => _drag;
		set
		{
			_drag = value;
			RigidBody.LinearDamp = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float AngularDrag
	{
		get => _angularDrag;
		set
		{
			_angularDrag = value;
			RigidBody.AngularDamp = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public float Bounciness
	{
		get => _bounciness;
		set
		{
			_bounciness = value;

			PhysicsMat.Bounce = value;

			OnPropertyChanged();
		}
	}

	public override Node CreateGDNode()
	{
		return new RigidBody3D();
	}

	public override void InitGDNode()
	{
		base.InitGDNode();
		PhysicsMat = new();
		RigidBody = (RigidBody3D)GDNode;
		RigidBody.PhysicsMaterialOverride = PhysicsMat;
	}

	public override void Init()
	{
		UpdateCamLayer();
		base.Init();
	}

	public override void PreDelete()
	{
		// Unregister spawnpoint on delete
		Root.Environment.UnregisterSpawnPoint(this);
		base.PreDelete();
	}

	public override void PhysicsProcess(double delta)
	{
		if (NetTransformAuthority == Root.Network.LocalPeerID)
		{
			// Velocity sync clock
			_velocityClock += delta;
			if (_velocityClock < VelocitySyncInterval) return;
			_velocityClock = 0;

			if (!Velocity.IsEqualApprox(_lastVelocity) || !AngularVelocity.IsEqualApprox(_lastAngularVelocity))
			{
				_lastVelocity = Velocity;
				_lastAngularVelocity = AngularVelocity;
				Rpc(nameof(NetUpdateVelocity), Velocity, AngularVelocity);
			}
		}
		base.PhysicsProcess(delta);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetUpdateVelocity(Vector3 velo, Vector3 angVelo)
	{
		Velocity = velo;
		AngularVelocity = angVelo;
	}

	internal void UpdateCamLayer()
	{
		if (Color.A > 0.5)
		{
			// Set layer for solid
			RigidBody.CollisionLayer = 1 << 0 | 1 << 5;
		}
		else
		{
			// Set layer for transparent
			RigidBody.CollisionLayer = 1;
		}
	}

	[ScriptMethod]
	public void AddForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 setto = force.Flip();
		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyCentralForce(setto);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantCentralForce(setto);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyCentralImpulse(setto);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			Velocity = force;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	[ScriptMethod]
	public void AddTorque(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyTorque(force);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantTorque(force);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyTorqueImpulse(force);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			AngularVelocity = force;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	[ScriptMethod]
	public void AddForceAtPosition(Vector3 force, Vector3 position, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 setto = force.Flip();
		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyForce(setto, position);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantForce(setto, position);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyImpulse(setto, position);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			Velocity = force;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	[ScriptMethod]
	public void AddRelativeForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 worldForce = RigidBody.GlobalTransform.Basis * force.Flip();
		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyCentralForce(worldForce);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantCentralForce(worldForce);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyCentralImpulse(worldForce);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			RigidBody.LinearVelocity += worldForce;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	[ScriptMethod]
	public void AddRelativeTorque(Vector3 torque, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 worldTorque = RigidBody.GlobalTransform.Basis * torque;

		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyTorque(worldTorque);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantTorque(worldTorque);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyTorqueImpulse(worldTorque);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			RigidBody.AngularVelocity += worldTorque;
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	protected override void ApplyFreeze(bool to)
	{
		RigidBody.Freeze = to;
		base.ApplyFreeze(to);
	}

	public enum ForceModeEnum
	{
		Force,
		Acceleration,
		Impulse,
		VelocityChange
	}
}
