using Godot;
using Polytoria.Datamodel;
using Polytoria.Utils;

namespace Polytoria.Providers.PlayerMovement;

public class DefaultMovement : IPlayerMovement
{
	private const float VRClimbMaxSpeedMeters = 7f;

	public Player Target { get; set; } = null!;

	public World Root { get; set; } = null!;

	public InputSnapshot SampleInput(double delta)
	{
		Camera? cam = Root.Environment.CurrentCamera;
		Vector3 moveDirection = Vector3.Zero;
		Vector3 camRotation = Vector3.Zero;
		float forwardInput = 0f;
		bool jump = false;
		bool sprint = false;
		bool camLocked = false;

		if (cam != null && (Root.Input.IsGameFocused || Polytoria.Shared.XRBootstrap.IsActive) && Target.CanMove && !Target.IsDead)
		{
			Vector3 facingRot = cam.Camera3D.GlobalRotation;
			camRotation = facingRot;

			float forwardStrength = Input.GetActionStrength("forward");
			float backwardStrength = Input.GetActionStrength("backward");
			forwardInput = forwardStrength - backwardStrength;

			moveDirection.X = Input.GetActionStrength("rightward") - Input.GetActionStrength("leftward");
			moveDirection.Z = backwardStrength - forwardStrength;
			moveDirection = moveDirection.Rotated(Vector3.Up, facingRot.Y).LimitLength(1);

			bool initialSprintOverride = Target.SprintOverride;
			jump = Input.IsActionPressed("jump");
			sprint = Input.IsActionPressed("sprint") || initialSprintOverride;

			if (Target.SprintHoldAgain)
			{
				sprint = Target.SprintOverride = false;
				if (Input.IsActionJustReleased("sprint") || initialSprintOverride)
				{
					Target.SprintHoldAgain = false;
				}
			}

			camLocked = cam.IsFirstPerson || cam.CtrlLocked;
		}

		return new()
		{
			Delta = delta,
			MoveDirection = moveDirection,
			Jump = jump,
			Sprint = sprint,
			ForwardInput = forwardInput,
			CameraRotation = camRotation,
			CamLocked = camLocked
		};
	}

	public void ProcessInput(InputSnapshot snapshot)
	{
		bool isOnFloor = Target.CharBody3D.IsOnFloor();
		CharacterModel.CharacterModelStateEnum finalState = CharacterModel.CharacterModelStateEnum.Idle;

		double delta = snapshot.Delta;

		Vector3 externalVelocity = Target.ExternalVelocity;
		bool hasExternalVelocity = externalVelocity.X != 0 || externalVelocity.Z != 0;

		if (Target.CanMove && !Target.IsDead)
		{
			float gdWalkSpeed = Target.WalkSpeed;
			bool sprinting = snapshot.Sprint;

			Vector3 moveDirection = snapshot.MoveDirection;
			float forwardInput = snapshot.ForwardInput;

			bool vrClimb = false;
			if (Polytoria.Shared.XRBootstrap.IsActive
				&& Polytoria.Shared.XRClimbState.TryGetPull(out Vector3 climbAnchor, out Vector3 climbHand))
			{
				Target.EndClimb();
				Target.JustFinishedClimbing = false;
				float ws = (float)XRServer.WorldScale;
				if (ws <= 0f) ws = 1f;
				Vector3 pull = ((climbAnchor - climbHand) / (float)delta).LimitLength(VRClimbMaxSpeedMeters * ws);
				pull.X += moveDirection.X * gdWalkSpeed;
				pull.Z += moveDirection.Z * gdWalkSpeed;
				Target.CharacterVelocity = pull;
				finalState = CharacterModel.CharacterModelStateEnum.Climbing;
				Target.Character?.SetAnimSpeed(Target.CharacterVelocity.Y / 8);
				vrClimb = true;
			}

			// Handle jump
			if (snapshot.Jump && !vrClimb)
			{
				Target.Jump();
			}

			// Sprint/Stamina
			if (sprinting && moveDirection != Vector3.Zero)
			{
				if (Target.Stamina > 0 || !Target.UseStamina)
				{
					gdWalkSpeed = Target.SprintSpeed;
				}
				else
				{
					sprinting = false;
					Target.SprintHoldAgain = true;
				}

				Target.RemoveStaminaTick(delta);
			}
			else
			{
				Target.AddStaminaTick(delta);
			}

			if (Target.IsClimbing)
			{
				// Reset all vectors, lock to Y only
				Target.CharacterVelocity.X = 0;
				Target.CharacterVelocity.Z = 0;

				float climbSpeed = forwardInput * gdWalkSpeed * Target.ClimbingTruss!.ClimbSpeed;

				// Add y velocity
				Target.CharacterVelocity.Y = climbSpeed;

				finalState = CharacterModel.CharacterModelStateEnum.Climbing;
				Target.Character?.SetAnimSpeed(climbSpeed / 8);
			}
			else if (Target.JustFinishedClimbing)
			{
				Target.JustFinishedClimbing = false;
				Target.CharacterVelocity.Y = 0;
			}

			if (snapshot.CamLocked)
			{
				if (Polytoria.Shared.XRBootstrap.IsActive)
				{
					const float HeadYawDeadZoneDeg = 45f;
					float targetY = 180f + Mathf.RadToDeg(snapshot.CameraRotation.Y);
					float yawDelta = Mathf.Wrap(targetY - Target.Rotation.Y, -180f, 180f);
					if (Mathf.Abs(yawDelta) > HeadYawDeadZoneDeg)
					{
						float excess = yawDelta - Mathf.Sign(yawDelta) * HeadYawDeadZoneDeg;
						Target.Rotation = Target.Rotation with { Y = Target.Rotation.Y + excess };
					}
				}
				else
				{
					Target.Rotation = Target.Rotation with { Y = 180 + Mathf.RadToDeg(snapshot.CameraRotation.Y) };
				}
			}

			Vector3 pushVelocity = hasExternalVelocity
				? externalVelocity with { Y = 0 }
				: Vector3.Zero;

			if (moveDirection != Vector3.Zero && !Target.IsClimbing && !vrClimb)
			{
				Target.IsMoving = true;

				Target.CharacterVelocity.X = (moveDirection.X * gdWalkSpeed) + pushVelocity.X;
				Target.CharacterVelocity.Z = (moveDirection.Z * gdWalkSpeed) + pushVelocity.Z;

				if (!snapshot.CamLocked)
				{
					// Apply rotation by move direction
					Target.Rotation = Target.Rotation with
					{
						Y = Mathf.RadToDeg(Mathf.LerpAngle(Mathf.DegToRad(Target.Rotation.Y), Mathf.Atan2(Target.CharacterVelocity.X, Target.CharacterVelocity.Z), MathUtils.ExpDecay((float)delta, NPC.BodyRotateLerp)))
					};
				}


				float animMoveAmount = Mathf.Max(Mathf.Clamp(moveDirection.Length(), 0f, 1f), 0.15f);
				if (sprinting && Target.SprintSpeed != Target.WalkSpeed)
				{
					finalState = CharacterModel.CharacterModelStateEnum.Running;
					Target.Character?.SetAnimSpeed(gdWalkSpeed / 20 * animMoveAmount);
				}
				else
				{
					finalState = CharacterModel.CharacterModelStateEnum.Walking;
					Target.Character?.SetAnimSpeed(gdWalkSpeed / 8 * animMoveAmount);
				}
			}
			else if (!Target.IsClimbing && !vrClimb)
			{
				Target.IsMoving = false;

				if (hasExternalVelocity)
				{
					Target.CharacterVelocity.X = pushVelocity.X;
					Target.CharacterVelocity.Z = pushVelocity.Z;
				}
				else
				{
					// Stop horizontal movement when no input
					Target.CharacterVelocity.X = Mathf.MoveToward(Target.CharacterVelocity.X, 0, gdWalkSpeed);
					Target.CharacterVelocity.Z = Mathf.MoveToward(Target.CharacterVelocity.Z, 0, gdWalkSpeed);
				}
				Target.Character?.SetAnimSpeed(1);
			}

			if (!isOnFloor && !Target.IsClimbing && !vrClimb)
			{
				Target.Character?.SetAnimSpeed(1);
				finalState = CharacterModel.CharacterModelStateEnum.Jumping;
			}

			// Remove debounce if touched the ground
			if (Target.ClimbDebounce && isOnFloor)
			{
				Target.ClimbDebounce = false;
			}

			if (Target.IsClimbing && isOnFloor)
			{
				Target.EndClimb();
			}
		}
		else
		{
			Target.CharacterVelocity = new Vector3(0, Target.CharacterVelocity.Y, 0);
		}

		Target.Character?.SetState(finalState);

		if (hasExternalVelocity)
		{
			float decay = Target.WalkSpeed * 60f * (float)delta;
			Target.ExternalVelocity = new Vector3(
				Mathf.MoveToward(externalVelocity.X, 0, decay),
				externalVelocity.Y,
				Mathf.MoveToward(externalVelocity.Z, 0, decay)
			);
		}

		Target.ApplyInternalVelocity(Target.CharacterVelocity);
		Target.CharBody3D.Velocity = Target.CharacterVelocity;
		Target.CharBody3D.MoveAndSlide();

		if (isOnFloor && Target.IsMoving && !Target.IsClimbing && !Target.IsSitting)
		{
			Target.TryStepUp();
		}
	}
}
