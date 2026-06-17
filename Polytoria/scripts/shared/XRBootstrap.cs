// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using System;
using System.Text.Json;

namespace Polytoria.Shared;

internal static class XRBootstrap
{
	private const string SettingsPath = "user://settings_client.json";
	private const string OpenXRSettingKey = "vr.openxr";
	private const string LegacyOpenXRSettingKey = "advanced.openxr";

	public static bool IsActive { get; private set; }

	public const float EyeForwardOffsetMeters = 0.12f;

	public static float CrouchWorld { get; set; }

	public static float MinCrouchWorld { get; set; }

	public static XROrigin3D? LoadingRig { get; set; }

	public static void ReleaseLoadingRig()
	{
		if (LoadingRig != null && GodotObject.IsInstanceValid(LoadingRig))
		{
			LoadingRig.QueueFree();
		}
		LoadingRig = null;
	}

	// the origin is rotated 180 degrees. flip it
	public static readonly Basis BodyYawCorrection = Basis.FromEuler(new Vector3(0f, Mathf.Pi, 0f));

	public static void TryEnable(Viewport viewport)
	{
		try
		{
			if (OS.HasFeature("server") || Globals.IsServerBuild) return;
			if (!IsOpenXRSettingEnabled()) return;
			XRInterface iface = XRServer.FindInterface("OpenXR");
			if (iface == null)
			{
				return;
			}
			if (!iface.IsInitialized() && !iface.Initialize())
			{
				return;
			}
			if (!HasRealHeadset(iface))
			{
				iface.Uninitialize();
				return;
			}
			viewport.UseXR = true;
			viewport.GuiEmbedSubwindows = true;
			IsActive = true;
			// it doesn't match exactly, because if it did, it would be too small. i manually adjusted it to be the most comfortable
			XRServer.WorldScale = 3.5;
			if (Globals.IsMobileBuild && iface is OpenXRInterface oxr)
			{
				oxr.SessionBegun += () => RaiseRefreshRate(oxr);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"OpenXR failed!: {ex.Message}");
		}
	}

	private static bool IsOpenXRSettingEnabled()
	{
		try
		{
			if (!FileAccess.FileExists(SettingsPath)) return true;
			string json = FileAccess.GetFileAsString(SettingsPath);
			if (string.IsNullOrEmpty(json)) return true;
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object) return true;
			if (!doc.RootElement.TryGetProperty(OpenXRSettingKey, out JsonElement el)
				&& !doc.RootElement.TryGetProperty(LegacyOpenXRSettingKey, out el)) return true;
			if (el.ValueKind == JsonValueKind.False) return false;
			return true;
		}
		catch
		{
			return true;
		}
	}

	private static void RaiseRefreshRate(OpenXRInterface oxr)
	{
		try
		{
			float best = 0f;
			foreach (Variant v in oxr.GetAvailableDisplayRefreshRates())
			{
				float rate = (float)v.AsDouble();
				if (rate <= 90.5f && rate > best) best = rate;
			}
			if (best > 0f && best > oxr.DisplayRefreshRate + 0.5f)
			{
				oxr.DisplayRefreshRate = best;
			}
		}
		catch
		{
		}
	}

	private static bool HasRealHeadset(XRInterface iface)
	{
		try
		{
			Vector2 size = iface.GetRenderTargetSize();
			if (size.X < 1 || size.Y < 1) return false;
			if (XRServer.GetTracker("head") == null) return false;
			return true;
		}
		catch
		{
			return false;
		}
	}

}
