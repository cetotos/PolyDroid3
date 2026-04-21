// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Interfaces;
using Polytoria.Utils;
using System;

namespace Polytoria.Datamodel;

[Instantiable]
public partial class PhysicalModel : Physical, IGroup
{
	internal RigidBody3D RigidBody = null!;
	internal PhysicsMaterial PhysicsMat = null!;

	private bool _useGravity = true;
	private float _mass;
	private float _friction;
	private float _drag;
	private float _angularDrag;
	private float _bounciness;

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 Velocity
	{
		get
		{
			return RigidBody.LinearVelocity.Flip();
		}
		set
		{
			RigidBody.LinearVelocity = value.Flip();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar(Unreliable = true, AllowAuthorWrite = true)]
	public override Vector3 AngularVelocity
	{
		get
		{
			return RigidBody.AngularVelocity.FlipEuler();
		}
		set
		{
			RigidBody.AngularVelocity = value.FlipEuler();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(true)]
	public override bool UseGravity
	{
		get => _useGravity;
		set
		{
			if (_useGravity == value)
			{
				return;
			}

			_useGravity = value;

			RigidBody.GravityScale = value ? 2 : 0;

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(1f)]
	public override float Mass
	{
		get => _mass;
		set
		{
			if (_mass == value)
			{
				return;
			}

			_mass = value;

			RigidBody.Mass = Math.Max(_mass, Physical.MinMass);

			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0.6f)]
	public override float Friction
	{
		get => _friction;
		set
		{
			if (_friction == value)
			{
				return;
			}

			_friction = value;
			PhysicsMat.Friction = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public override float Drag
	{
		get => _drag;
		set
		{
			if (_drag == value)
			{
				return;
			}

			_drag = value;
			RigidBody.LinearDamp = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public override float AngularDrag
	{
		get => _angularDrag;
		set
		{
			if (_angularDrag == value)
			{
				return;
			}

			_angularDrag = value;
			RigidBody.AngularDamp = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, DefaultValue(0)]
	public override float Bounciness
	{
		get => _bounciness;
		set
		{
			if (_bounciness == value)
			{
				return;
			}

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
		RigidBody.GravityScale = 2;
	}

	public override void Init()
	{
		base.Init();
		Anchored = true;
		CanCollide = true;
	}

	internal override void ApplyAddForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyCentralForce(force);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantCentralForce(force);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyCentralImpulse(force);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			Velocity = force.Flip();
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	internal override void ApplyAddTorque(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
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

	internal override void ApplyAddForceAtPosition(Vector3 force, Vector3 position, ForceModeEnum mode = ForceModeEnum.Force)
	{
		if (mode == ForceModeEnum.Force)
		{
			RigidBody.ApplyForce(force, position);
		}
		else if (mode == ForceModeEnum.Acceleration)
		{
			RigidBody.AddConstantForce(force, position);
		}
		else if (mode == ForceModeEnum.Impulse)
		{
			RigidBody.ApplyImpulse(force, position);
		}
		else if (mode == ForceModeEnum.VelocityChange)
		{
			Velocity = force.Flip();
		}
		else
		{
			throw new NotImplementedException(mode + " not implemented");
		}
	}

	internal override void ApplyAddRelativeForce(Vector3 force, ForceModeEnum mode = ForceModeEnum.Force)
	{
		Vector3 worldForce = RigidBody.GlobalTransform.Basis * force;
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

	internal override void ApplyAddRelativeTorque(Vector3 torque, ForceModeEnum mode = ForceModeEnum.Force)
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
}
