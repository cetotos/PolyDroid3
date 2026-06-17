// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using DeepLinkAddon;
using Godot;
using Polytoria.Shared;

namespace Polytoria.Client.MainMenu;

public partial class MainMenu : Node, IVRPointerTarget
{
	private const float MenuPlaneWidthMeters = 1.4f;
	private const float MenuPlaneDistance = 2.5f;
	private const float SnapDegrees = 45f;
	private const float SnapHysteresisDegrees = 5f;
	private static readonly Vector2I MenuPanelSize = new(900, 1100);

	private SubViewport _menuViewport = null!;
	private MeshInstance3D _menuPlane = null!;
	private Camera3D? _flatCam;
	private XRCamera3D? _xrCamera;
	private XROrigin3D? _xrOrigin;
	private Deeplink _deepLink = null!;
	private Vector2 _lastFlatPixel;
	private bool _flatTriggerWasDown;
	private float _snappedYawDeg = float.NaN;
	private float _anchoredY = float.NaN;

	public Transform3D PanelGlobalTransform => _menuPlane.GlobalTransform;
	public Vector2 PanelSizeMeters
	{
		get
		{
			var qm = (QuadMesh)_menuPlane.Mesh;
			return qm.Size;
		}
	}
	public Vector2I ViewportPixelSize => MenuPanelSize;
	public SubViewport TargetViewport => _menuViewport;
	public bool AcceptsPointer => true;
	public void OnPointerHit() { }

	public override void _Ready()
	{
		if (Globals.IsMobileBuild)
		{
			float uiScale = Polytoria.Client.Settings.ClientSettingsService.Instance?.Get<float>(Polytoria.Client.Settings.ClientSettingKeys.Display.UiScale) ?? 1f;
			GetTree().Root.ContentScaleFactor = Globals.MobileScale * uiScale;
		}

		_deepLink = new Deeplink();
		AddChild(_deepLink, true);
		_deepLink.DeeplinkReceived += OnDeeplinkReceived;
		CallDeferred(nameof(CheckLaunchDeeplink));

		BuildBackground();
		BuildMenuPlane();
		VRPointerRegistry.Register(this);

		if (XRBootstrap.IsActive && _xrOrigin != null)
		{
			var rightCtrl = new XRController3D { Tracker = "right_hand" };
			_xrOrigin.AddChild(rightCtrl);
			rightCtrl.AddChild(new XRPointer(rightCtrl, null!));

			if (_xrCamera != null)
			{
				var keyboard = new VRKeyboard(_menuViewport, _xrCamera);
				AddChild(keyboard);
			}
		}
	}

	public override void _ExitTree()
	{
		VRPointerRegistry.Unregister(this);
	}

	private void CheckLaunchDeeplink()
	{
		string url = _deepLink.GetLinkUrl();
		if (!string.IsNullOrEmpty(url))
		{
			HandoffToMobileUI();
		}
	}

	private void OnDeeplinkReceived(DeeplinkURL _)
	{
		HandoffToMobileUI();
	}

	private void HandoffToMobileUI()
	{
		Globals.Singleton.SwitchEntry(Globals.AppEntryEnum.MobileUI);
	}

	private void BuildBackground()
	{
		var sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() };
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = sky,
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		};
		AddChild(new WorldEnvironment { Environment = env });
		AddChild(new DirectionalLight3D
		{
			ShadowEnabled = true,
			RotationDegrees = new Vector3(-50f, -45f, 0f),
		});

		if (XRBootstrap.IsActive)
		{
			_xrOrigin = new XROrigin3D();
			AddChild(_xrOrigin);
			_xrCamera = new XRCamera3D { Current = true };
			_xrOrigin.AddChild(_xrCamera);
		}
		else
		{
			_flatCam = new Camera3D
			{
				Current = true,
				Position = new Vector3(0f, 1.6f, 0f),
			};
			AddChild(_flatCam);
		}
	}

	private void BuildMenuPlane()
	{
		_menuViewport = new SubViewport
		{
			Size = MenuPanelSize,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			TransparentBg = false,
			HandleInputLocally = true,
			GuiEmbedSubwindows = true,
		};
		AddChild(_menuViewport);

		PackedScene mobileScene = GD.Load<PackedScene>("res://scenes/mobile/mobile.tscn");
		var mobileInstance = mobileScene.Instantiate<Control>();
		_menuViewport.AddChild(mobileInstance);

		float aspect = (float)MenuPanelSize.Y / MenuPanelSize.X;
		var quadMesh = new QuadMesh { Size = new Vector2(MenuPlaneWidthMeters, MenuPlaneWidthMeters * aspect) };
		var mat = new StandardMaterial3D
		{
			AlbedoTexture = _menuViewport.GetTexture(),
			Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		float ws = XRBootstrap.IsActive ? (float)XRServer.WorldScale : 1f;
		_menuPlane = new MeshInstance3D
		{
			Mesh = quadMesh,
			MaterialOverride = mat,
			Position = new Vector3(0f, 1.6f * ws, -MenuPlaneDistance * ws),
			Scale = new Vector3(ws, ws, ws),
		};
		AddChild(_menuPlane);
	}

	public override void _Process(double delta)
	{
		if (!XRBootstrap.IsActive || _xrCamera == null || _menuPlane == null) return;

		Transform3D camGlobal = _xrCamera.GlobalTransform;
		Vector3 forwardFlat = -camGlobal.Basis.Z;
		forwardFlat.Y = 0f;
		if (forwardFlat.LengthSquared() < 1e-4f) forwardFlat = Vector3.Forward;
		forwardFlat = forwardFlat.Normalized();

		float yawDeg = Mathf.RadToDeg(Mathf.Atan2(forwardFlat.X, forwardFlat.Z));
		bool needsPlace = float.IsNaN(_snappedYawDeg)
			|| Mathf.Abs(Mathf.Wrap(yawDeg - _snappedYawDeg, -180f, 180f)) > SnapDegrees * 0.5f + SnapHysteresisDegrees;
		if (!needsPlace) return;

		_snappedYawDeg = Mathf.Round(yawDeg / SnapDegrees) * SnapDegrees;
		float yawRad = Mathf.DegToRad(_snappedYawDeg);
		Vector3 snappedForward = new(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));

		float ws = (float)XRServer.WorldScale;
		if (ws <= 0f) ws = 1f;
		if (float.IsNaN(_anchoredY)) _anchoredY = camGlobal.Origin.Y;
		Vector3 target = camGlobal.Origin + snappedForward * (MenuPlaneDistance * ws);
		target.Y = _anchoredY;

		_menuPlane.GlobalPosition = target;

		Vector3 lookAt = new(camGlobal.Origin.X, target.Y, camGlobal.Origin.Z);
		if (target.DistanceSquaredTo(lookAt) > 0.01f)
		{
			_menuPlane.LookAt(lookAt, Vector3.Up, true);
		}
	}

	public override void _UnhandledInput(InputEvent ev)
	{
		if (XRBootstrap.IsActive || _flatCam == null) return;
		if (ev is InputEventMouseMotion mm)
		{
			ForwardFlatInput(mm.Position, isClick: false, pressed: false);
		}
		else if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			ForwardFlatInput(mb.Position, isClick: true, pressed: mb.Pressed);
		}
		else if (ev is InputEventScreenTouch st)
		{
			ForwardFlatInput(st.Position, isClick: true, pressed: st.Pressed);
		}
		else if (ev is InputEventScreenDrag sd)
		{
			ForwardFlatInput(sd.Position, isClick: false, pressed: false);
		}
	}

	private void ForwardFlatInput(Vector2 screenPos, bool isClick, bool pressed)
	{
		if (_flatCam == null) return;
		Vector3 rayOrigin = _flatCam.ProjectRayOrigin(screenPos);
		Vector3 rayDir = _flatCam.ProjectRayNormal(screenPos);
		Transform3D px = PanelGlobalTransform;
		Vector3 pn = px.Basis.Z.Normalized();
		float denom = rayDir.Dot(pn);
		if (Mathf.Abs(denom) < 1e-4f) return;
		float t = (px.Origin - rayOrigin).Dot(pn) / denom;
		if (t <= 0f) return;
		Vector3 hit = rayOrigin + rayDir * t;
		Vector3 local = px.AffineInverse() * hit;
		Vector2 size = PanelSizeMeters;
		if (Mathf.Abs(local.X) > size.X / 2f || Mathf.Abs(local.Y) > size.Y / 2f) return;
		float u = (local.X + size.X / 2f) / size.X;
		float v = 1f - (local.Y + size.Y / 2f) / size.Y;
		Vector2 pixel = new(u * MenuPanelSize.X, v * MenuPanelSize.Y);
		if (isClick)
		{
			if (pressed != _flatTriggerWasDown)
			{
				_flatTriggerWasDown = pressed;
				_menuViewport.PushInput(new InputEventMouseButton
				{
					Position = pixel,
					GlobalPosition = pixel,
					ButtonIndex = MouseButton.Left,
					Pressed = pressed,
				});
			}
		}
		else if (pixel != _lastFlatPixel)
		{
			_lastFlatPixel = pixel;
			_menuViewport.PushInput(new InputEventMouseMotion
			{
				Position = pixel,
				GlobalPosition = pixel,
			});
		}
	}

}
