// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class XRArmIK : SkeletonModifier3D
{
	private const float WristGripOffsetMeters = 0.5f;
	private const float PalmRollDegrees = 90f;
	private const float ForearmTwistFollow = 0.4f;
	private const float PoleTwistGain = 0.7f;
	private const float PoleSwingLimitRad = 1.75f;
	private const float PoleSmoothing = 12f;
	private static readonly Vector3 PoleRight = new Vector3(-0.4f, -0.6f, -1f).Normalized();
	private static readonly Vector3 PoleLeft = new Vector3(0.4f, -0.6f, -1f).Normalized();

	private readonly Skeleton3D _skeleton;
	private readonly XRController3D? _leftController;
	private readonly XRController3D? _rightController;

	public Transform3D? OverrideLeftTargetLocal;
	public Transform3D? OverrideRightTargetLocal;
	public Transform3D? LastLeftTargetLocal { get; private set; }
	public Transform3D? LastRightTargetLocal { get; private set; }

	private int _luaIdx, _llaIdx, _lhdIdx;
	private int _ruaIdx, _rlaIdx, _rhdIdx;

	private float _lL1, _lL2, _rL1, _rL2;

	private Vector3 _lUpperRestDir, _lLowerRestDir;
	private Vector3 _rUpperRestDir, _rLowerRestDir;
	private Basis _luaRestBasis, _llaRestBasis;
	private Basis _ruaRestBasis, _rlaRestBasis;
	private Basis _lHandNeutral, _rHandNeutral;
	private float _lPoleAngle, _rPoleAngle;

	private bool _ready;

	public XRArmIK(Skeleton3D skel, XRController3D? left, XRController3D? right)
	{
		_skeleton = skel;
		_leftController = left;
		_rightController = right;
	}

	public override void _Ready()
	{
		_luaIdx = _skeleton.FindBone("UpperArm.L");
		_llaIdx = _skeleton.FindBone("LowerArm.L");
		_lhdIdx = _skeleton.FindBone("Hand.L");
		_ruaIdx = _skeleton.FindBone("UpperArm.R");
		_rlaIdx = _skeleton.FindBone("LowerArm.R");
		_rhdIdx = _skeleton.FindBone("Hand.R");

		if (_luaIdx < 0 || _llaIdx < 0 || _lhdIdx < 0 || _ruaIdx < 0 || _rlaIdx < 0 || _rhdIdx < 0)
		{
			PT.PrintErr($"XRArmIK: missing bone indices. queueing free");
			QueueFree();
			return;
		}

		Transform3D luaRest = _skeleton.GetBoneGlobalRest(_luaIdx);
		Transform3D llaRest = _skeleton.GetBoneGlobalRest(_llaIdx);
		Transform3D lhdRest = _skeleton.GetBoneGlobalRest(_lhdIdx);
		Transform3D ruaRest = _skeleton.GetBoneGlobalRest(_ruaIdx);
		Transform3D rlaRest = _skeleton.GetBoneGlobalRest(_rlaIdx);
		Transform3D rhdRest = _skeleton.GetBoneGlobalRest(_rhdIdx);

		Vector3 lShoulderToElbow = llaRest.Origin - luaRest.Origin;
		Vector3 lElbowToHand = lhdRest.Origin - llaRest.Origin;
		Vector3 rShoulderToElbow = rlaRest.Origin - ruaRest.Origin;
		Vector3 rElbowToHand = rhdRest.Origin - rlaRest.Origin;

		_lL1 = lShoulderToElbow.Length();
		_lL2 = lElbowToHand.Length();
		_rL1 = rShoulderToElbow.Length();
		_rL2 = rElbowToHand.Length();

		_lUpperRestDir = lShoulderToElbow / _lL1;
		_lLowerRestDir = lElbowToHand / _lL2;
		_rUpperRestDir = rShoulderToElbow / _rL1;
		_rLowerRestDir = rElbowToHand / _rL2;

		_luaRestBasis = luaRest.Basis;
		_llaRestBasis = llaRest.Basis;
		_ruaRestBasis = ruaRest.Basis;
		_rlaRestBasis = rlaRest.Basis;

		_lHandNeutral = NeutralHandBasis(lhdRest.Basis, _lLowerRestDir, -PalmRollDegrees);
		_rHandNeutral = NeutralHandBasis(rhdRest.Basis, _rLowerRestDir, PalmRollDegrees);

		_ready = true;
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		if (!_ready) return;
		ApplyArm(_luaIdx, _llaIdx, _lhdIdx, _lL1, _lL2, _lUpperRestDir, _lLowerRestDir, _luaRestBasis, _llaRestBasis, _leftController, isRight: false, delta);
		ApplyArm(_ruaIdx, _rlaIdx, _rhdIdx, _rL1, _rL2, _rUpperRestDir, _rLowerRestDir, _ruaRestBasis, _rlaRestBasis, _rightController, isRight: true, delta);
	}

	private Transform3D? ResolveTargetLocal(XRController3D? ctrl, bool isRight)
	{
		if (ctrl != null)
		{
			Transform3D skelGlobalInv = _skeleton.GlobalTransform.AffineInverse();
			Transform3D local = skelGlobalInv * ctrl.GlobalTransform;
			if (isRight) LastRightTargetLocal = local; else LastLeftTargetLocal = local;
			return local;
		}
		return isRight ? OverrideRightTargetLocal : OverrideLeftTargetLocal;
	}

	private void ApplyArm(int upperIdx, int lowerIdx, int handIdx, float l1, float l2, Vector3 upperRestDir, Vector3 lowerRestDir, Basis upperRestBasis, Basis lowerRestBasis, XRController3D? ctrl, bool isRight, double delta)
	{
		Transform3D? targetLocalOpt = ResolveTargetLocal(ctrl, isRight);
		if (targetLocalOpt == null) return;
		Vector3 shoulder = _skeleton.GetBoneGlobalPose(upperIdx).Origin;
		Transform3D targetLocal = targetLocalOpt.Value;
		Vector3 target = targetLocal.Origin + targetLocal.Basis.Z * WristGripOffsetMeters;

		Vector3 toTarget = target - shoulder;
		float dist = toTarget.Length();
		float maxReach = (l1 + l2) * 0.999f;
		if (dist > maxReach)
		{
			toTarget = toTarget.Normalized() * maxReach;
			target = shoulder + toTarget;
			dist = maxReach;
		}
		if (dist < 0.001f) return;

		Vector3 toTargetN = toTarget / dist;
		float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
		float angleA = Mathf.Acos(cosA);

		Vector3 pole = isRight ? PoleRight : PoleLeft;
		Vector3 binormal = pole - toTargetN * pole.Dot(toTargetN);
		if (binormal.LengthSquared() < 1e-6f)
		{
			binormal = Vector3.Down - toTargetN * toTargetN.Dot(Vector3.Down);
			if (binormal.LengthSquared() < 1e-6f) binormal = toTargetN.Cross(Vector3.Right);
		}
		binormal = binormal.Normalized();

		Basis neutral = isRight ? _rHandNeutral : _lHandNeutral;
		Basis handBasis = (targetLocal.Basis.Orthonormalized() * XRBootstrap.BodyYawCorrection * neutral).Orthonormalized();
		Quaternion qHand = handBasis.GetRotationQuaternion();

		Quaternion qForearmZeroRoll = SolveForearm(shoulder, target, toTargetN, angleA, l1, binormal, upperRestDir, lowerRestDir, lowerRestBasis, out _, out _, out _);
		float rollAngle = TwistAngle(qHand * qForearmZeroRoll.Inverse(), toTargetN);
		float poleTarget = Mathf.Clamp(rollAngle * PoleTwistGain, -PoleSwingLimitRad, PoleSwingLimitRad);

		ref float poleAngle = ref (isRight ? ref _rPoleAngle : ref _lPoleAngle);
		poleAngle = Mathf.Lerp(poleAngle, poleTarget, 1f - Mathf.Exp(-PoleSmoothing * (float)delta));
		Vector3 swungBinormal = binormal.Rotated(toTargetN, poleAngle);

		Quaternion qForearm = SolveForearm(shoulder, target, toTargetN, angleA, l1, swungBinormal, upperRestDir, lowerRestDir, lowerRestBasis, out Vector3 elbow, out Vector3 newLowerDir, out Quaternion qUpper);

		Basis upperBasis = new Basis(qUpper) * upperRestBasis;
		_skeleton.SetBoneGlobalPose(upperIdx, new Transform3D(upperBasis, shoulder));

		Quaternion wristDelta = qHand * qForearm.Inverse();
		Quaternion forearmTwist = Quaternion.Identity.Slerp(TwistAbout(wristDelta, newLowerDir), ForearmTwistFollow);
		Basis twistedLowerBasis = new Basis(forearmTwist) * new Basis(SolveLowerQuat(qUpper, lowerRestDir, newLowerDir)) * lowerRestBasis;
		_skeleton.SetBoneGlobalPose(lowerIdx, new Transform3D(twistedLowerBasis, elbow));

		_skeleton.SetBoneGlobalPose(handIdx, new Transform3D(handBasis, target));
	}

	private Quaternion SolveForearm(Vector3 shoulder, Vector3 target, Vector3 toTargetN, float angleA, float l1, Vector3 binormal, Vector3 upperRestDir, Vector3 lowerRestDir, Basis lowerRestBasis, out Vector3 elbow, out Vector3 newLowerDir, out Quaternion qUpper)
	{
		elbow = shoulder + l1 * (Mathf.Cos(angleA) * toTargetN + Mathf.Sin(angleA) * binormal);
		Vector3 newUpperDir = (elbow - shoulder).Normalized();
		qUpper = ShortestArc(upperRestDir, newUpperDir);
		newLowerDir = (target - elbow).Normalized();
		Quaternion qLower = SolveLowerQuat(qUpper, lowerRestDir, newLowerDir);
		return (new Basis(qLower) * lowerRestBasis).Orthonormalized().GetRotationQuaternion();
	}

	private static Quaternion SolveLowerQuat(Quaternion qUpper, Vector3 lowerRestDir, Vector3 newLowerDir)
	{
		Vector3 lowerDirAfterUpper = qUpper * lowerRestDir;
		return ShortestArc(lowerDirAfterUpper, newLowerDir) * qUpper;
	}

	private static float TwistAngle(Quaternion q, Vector3 axis)
	{
		Quaternion twist = TwistAbout(q, axis);
		float angle = 2f * Mathf.Acos(Mathf.Clamp(twist.W, -1f, 1f));
		Vector3 v = new(twist.X, twist.Y, twist.Z);
		return v.Dot(axis) < 0f ? -angle : angle;
	}

	private static Basis NeutralHandBasis(Basis rest, Vector3 restArmDir, float rollDegrees)
	{
		Quaternion toForward = ShortestArc(restArmDir.Normalized(), Vector3.Back);
		return new Basis(Vector3.Back, Mathf.DegToRad(rollDegrees)) * new Basis(toForward) * rest.Orthonormalized();
	}

	private static Quaternion TwistAbout(Quaternion q, Vector3 axis)
	{
		if (q.W < 0f) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
		Vector3 r = new Vector3(q.X, q.Y, q.Z);
		Vector3 proj = axis * r.Dot(axis);
		Quaternion twist = new Quaternion(proj.X, proj.Y, proj.Z, q.W);
		float len = Mathf.Sqrt(twist.X * twist.X + twist.Y * twist.Y + twist.Z * twist.Z + twist.W * twist.W);
		if (len < 1e-6f) return Quaternion.Identity;
		return new Quaternion(twist.X / len, twist.Y / len, twist.Z / len, twist.W / len);
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
		float angle = Mathf.Acos(d);
		return new Quaternion(cross / crossLen, angle);
	}
}
