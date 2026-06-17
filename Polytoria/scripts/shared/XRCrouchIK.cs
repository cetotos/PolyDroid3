// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class XRCrouchIK : SkeletonModifier3D
{
	private const float MaxCrouchFraction = 0.65f;
	private const float CrouchSmoothing = 14f;
	private const float MovingBlendSpeed = 8f;
	private const float HorizontalLeanLimit = 2f;
	private const float RaiseLimit = 0.5f;
	private static readonly Vector3 KneePole = new(0f, 0f, 1f);

	private sealed class Leg
	{
		public int UpperIdx;
		public int LowerIdx;
		public float L1;
		public float L2;
		public Vector3 Ankle;
		public Vector3 ThighRestDir;
		public Vector3 ShinRestDir;
		public Basis UpperRestBasis;
		public Basis LowerRestBasis;
	}

	private readonly Skeleton3D _skeleton;
	private Leg _left = null!;
	private Leg _right = null!;
	private int _torsoIdx;
	private int _headIdx = -1;
	private bool _ready;
	private Vector3 _offset;
	private float _bendCrouch;
	private float _movingBlend;

	public float CrouchWorld;
	public bool Moving;
	public Vector3? HeadTargetLocal;
	public Vector3? OverrideTorsoOffset;
	public Vector3 LastTorsoOffset { get; private set; }

	public XRCrouchIK(Skeleton3D skel)
	{
		_skeleton = skel;
	}

	public override void _Ready()
	{
		_torsoIdx = _skeleton.FindBone("LowerTorso");
		_headIdx = _skeleton.FindBone("Head_2");
		int lu = _skeleton.FindBone("UpperLeg.L");
		int ll = _skeleton.FindBone("LowerLeg.L");
		int ru = _skeleton.FindBone("UpperLeg.R");
		int rl = _skeleton.FindBone("LowerLeg.R");
		if (_torsoIdx < 0 || lu < 0 || ll < 0 || ru < 0 || rl < 0)
		{
			QueueFree();
			return;
		}

		_left = SetupLeg(lu, ll);
		_right = SetupLeg(ru, rl);
		_ready = true;
	}

	private Leg SetupLeg(int upperIdx, int lowerIdx)
	{
		Transform3D hipRest = _skeleton.GetBoneGlobalRest(upperIdx);
		Transform3D kneeRest = _skeleton.GetBoneGlobalRest(lowerIdx);
		Vector3 hip = hipRest.Origin;
		Vector3 knee = kneeRest.Origin;
		Vector3 thigh = knee - hip;
		float l1 = thigh.Length();
		Vector3 thighDir = thigh / l1;
		float l2 = thighDir.Y < -0.1f ? knee.Y / -thighDir.Y : l1;

		return new Leg
		{
			UpperIdx = upperIdx,
			LowerIdx = lowerIdx,
			L1 = l1,
			L2 = l2,
			Ankle = knee + thighDir * l2,
			ThighRestDir = thighDir,
			ShinRestDir = thighDir,
			UpperRestBasis = hipRest.Basis,
			LowerRestBasis = kneeRest.Basis,
		};
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		if (!_ready) return;

		float scaleY = _skeleton.GlobalTransform.Basis.Y.Length();
		if (scaleY <= 0f) scaleY = 1f;
		float maxCrouch = (_left.L1 + _left.L2) * MaxCrouchFraction;

		Vector3 target;
		if (OverrideTorsoOffset.HasValue)
		{
			target = OverrideTorsoOffset.Value;
		}
		else if (HeadTargetLocal.HasValue && _headIdx >= 0)
		{
			target = HeadTargetLocal.Value - _skeleton.GetBoneGlobalPose(_headIdx).Origin;
		}
		else
		{
			target = new Vector3(0f, -Mathf.Clamp(CrouchWorld / scaleY, 0f, maxCrouch), 0f);
		}

		Vector3 horizontal = target with { Y = 0f };
		if (horizontal.Length() > HorizontalLeanLimit)
		{
			horizontal = horizontal.Normalized() * HorizontalLeanLimit;
		}
		target = new Vector3(horizontal.X, Mathf.Clamp(target.Y, -maxCrouch, RaiseLimit), horizontal.Z);

		_offset = _offset.Lerp(target, 1f - Mathf.Exp(-CrouchSmoothing * (float)delta));
		_movingBlend = Mathf.Lerp(_movingBlend, Moving ? 1f : 0f, 1f - Mathf.Exp(-MovingBlendSpeed * (float)delta));
		LastTorsoOffset = _offset;
		_bendCrouch = Mathf.Max(0f, -_offset.Y);
		if (_offset.Length() < 0.005f) return;

		Transform3D torso = _skeleton.GetBoneGlobalPose(_torsoIdx);
		torso.Origin += _offset;
		_skeleton.SetBoneGlobalPose(_torsoIdx, torso);

		ApplyLeg(_left);
		ApplyLeg(_right);
	}

	private void ApplyLeg(Leg leg)
	{
		Transform3D upAnim = _skeleton.GetBoneGlobalPose(leg.UpperIdx);
		Transform3D loAnim = _skeleton.GetBoneGlobalPose(leg.LowerIdx);

		float theta = Mathf.Acos(Mathf.Clamp(1f - _bendCrouch / (leg.L1 + leg.L2), 0.1f, 1f));
		Basis bendFwd = new(Vector3.Right, -theta);
		Basis bendBack = new(Vector3.Right, theta);
		Transform3D gaitUpper = new(bendFwd * upAnim.Basis, upAnim.Origin);
		Transform3D gaitLower = new(bendBack * loAnim.Basis, upAnim.Origin + bendFwd * (loAnim.Origin - upAnim.Origin));

		Transform3D upper = gaitUpper;
		Transform3D lower = gaitLower;
		if (_movingBlend < 0.999f && SolvePlanted(leg, upAnim.Origin, out Transform3D plantedUpper, out Transform3D plantedLower))
		{
			upper = plantedUpper.InterpolateWith(gaitUpper, _movingBlend);
			lower = plantedLower.InterpolateWith(gaitLower, _movingBlend);
		}

		_skeleton.SetBoneGlobalPose(leg.UpperIdx, upper);
		_skeleton.SetBoneGlobalPose(leg.LowerIdx, lower);
	}

	private bool SolvePlanted(Leg leg, Vector3 hip, out Transform3D upper, out Transform3D lower)
	{
		upper = default;
		lower = default;

		Vector3 toTarget = leg.Ankle - hip;
		float dist = toTarget.Length();
		float maxReach = (leg.L1 + leg.L2) * 0.999f;
		if (dist > maxReach)
		{
			toTarget = toTarget / dist * maxReach;
			dist = maxReach;
		}
		if (dist < 0.001f) return false;

		Vector3 t = toTarget / dist;
		float cosA = Mathf.Clamp((leg.L1 * leg.L1 + dist * dist - leg.L2 * leg.L2) / (2f * leg.L1 * dist), -1f, 1f);
		float angleA = Mathf.Acos(cosA);

		Vector3 binormal = KneePole - t * KneePole.Dot(t);
		if (binormal.LengthSquared() < 1e-6f)
		{
			binormal = Vector3.Up - t * t.Dot(Vector3.Up);
			if (binormal.LengthSquared() < 1e-6f) return false;
		}
		binormal = binormal.Normalized();

		Vector3 knee = hip + leg.L1 * (Mathf.Cos(angleA) * t + Mathf.Sin(angleA) * binormal);

		Vector3 thighDir = (knee - hip).Normalized();
		Quaternion qUpper = ShortestArc(leg.ThighRestDir, thighDir);
		upper = new Transform3D(new Basis(qUpper) * leg.UpperRestBasis, hip);

		Vector3 shinDir = (hip + toTarget - knee).Normalized();
		Quaternion qLower = ShortestArc(qUpper * leg.ShinRestDir, shinDir) * qUpper;
		lower = new Transform3D(new Basis(qLower) * leg.LowerRestBasis, knee);
		return true;
	}

	private static Quaternion ShortestArc(Vector3 from, Vector3 to)
	{
		float d = Mathf.Clamp(from.Dot(to), -1f, 1f);
		if (d > 0.99999f) return Quaternion.Identity;
		if (d < -0.99999f)
		{
			Vector3 axis = Vector3.Right.Cross(from);
			if (axis.LengthSquared() < 1e-6f) axis = Vector3.Up.Cross(from);
			return new Quaternion(axis.Normalized(), Mathf.Pi);
		}
		Vector3 cross = from.Cross(to);
		float crossLen = cross.Length();
		if (crossLen < 1e-6f) return Quaternion.Identity;
		return new Quaternion(cross / crossLen, Mathf.Acos(d));
	}
}
