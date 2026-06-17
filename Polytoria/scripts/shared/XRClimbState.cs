// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System.Collections.Generic;

namespace Polytoria.Shared;

public static class XRClimbState
{
	private static readonly List<XRGrab> Hands = [];

	public static bool Active => Hands.Count > 0;

	public static void Latch(XRGrab hand)
	{
		Hands.Remove(hand);
		Hands.Add(hand);
	}

	public static void Unlatch(XRGrab hand)
	{
		Hands.Remove(hand);
	}

	public static bool TryGetPull(out Vector3 anchorWorld, out Vector3 handWorld)
	{
		for (int i = Hands.Count - 1; i >= 0; i--)
		{
			if (Hands[i].TryGetClimb(out anchorWorld, out handWorld)) return true;
		}
		anchorWorld = handWorld = default;
		return false;
	}
}
