// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;

namespace Polytoria.Shared;

public partial class XRHeadIK : SkeletonModifier3D
{
	private const float Smoothing = 18f;

	private readonly Skeleton3D _skeleton;
	private int _headIdx = -1;
	private bool _ready;
	private Basis _restBasis = Basis.Identity;
	private Quaternion _current = Quaternion.Identity;
	private bool _hasCurrent;

	public Quaternion? TargetRotationLocal;

	public XRHeadIK(Skeleton3D skel)
	{
		_skeleton = skel;
	}

	public override void _Ready()
	{
		_headIdx = _skeleton.FindBone("Head_2");
		if (_headIdx < 0)
		{
			QueueFree();
			return;
		}
		_restBasis = _skeleton.GetBoneGlobalRest(_headIdx).Basis.Orthonormalized();
		_ready = true;
	}

	public override void _ProcessModificationWithDelta(double delta)
	{
		if (!_ready || !TargetRotationLocal.HasValue) return;

		Quaternion target = TargetRotationLocal.Value;
		if (!_hasCurrent)
		{
			_current = target;
			_hasCurrent = true;
		}
		else
		{
			float t = 1f - Mathf.Exp(-Smoothing * (float)delta);
			_current = _current.Slerp(target, t);
		}

		Transform3D current = _skeleton.GetBoneGlobalPose(_headIdx);
		Basis basis = new Basis(_current) * _restBasis;
		_skeleton.SetBoneGlobalPose(_headIdx, new Transform3D(basis, current.Origin));
	}
}
