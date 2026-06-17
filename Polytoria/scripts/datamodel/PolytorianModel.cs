// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;
using Polytoria.Networking;
using Polytoria.Schemas.API;
using Polytoria.Scripting;
using Polytoria.Shared;
using Polytoria.Shared.Misc;
using Polytoria.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class PolytorianModel : CharacterModel
{
	private const double NetLookBlendUpdateInterval = 0.1;
	private double _lastNetUpdateTime = 0.0;
	private const double NetHeadUpdateInterval = 1.0 / 60.0;
	private double _lastNetHeadUpdateTime = 0.0;
	private bool _xrHeadHooked;
	private Polytoria.Shared.XRArmIK? _remoteArmIK;
	private Polytoria.Shared.XRCrouchIK? _crouchIK;
	private Polytoria.Shared.XRHeadIK? _headIK;

	private static readonly BoxShape3D _collisionBox = new() { Size = new(2f, 5.8f, 1f) };
	internal Node3D? CollisionPivot;
	internal CollisionShape3D? CollisionShape;
	private Physical? _oldPhyParent;

	internal MeshInstance3D HeadMeshInstance = null!;
	internal MeshInstance3D TorsoMeshInstance = null!;
	internal MeshInstance3D LeftArmMeshInstance = null!;
	internal MeshInstance3D RightArmMeshInstance = null!;
	internal MeshInstance3D LeftLegMeshInstance = null!;
	internal MeshInstance3D RightLegMeshInstance = null!;
	internal Node3D Pivot = null!;

	private const float BlendSpeed = 5f;
	private const float LookBlendSpeed = 15f;
	private static readonly Color _defaultBodyColor = Colors.White;

	private const int ClothingWidth = 1024;
	private const int ClothingHeight = 1024;
	private const Image.Format ClothingFormat = Image.Format.Rgba8;
	private static readonly Rect2I _clothingRect = new(0, 0, ClothingWidth, ClothingHeight);

	private int _loadAppearanceCount = 0;

	internal Skeleton3D Skeleton = null!;
	internal AnimationTree AnimTree = null!;

	private static readonly Shader _limbShader = GD.Load<Shader>("res://resources/shaders/character/limb.gdshader");
	private static readonly Shader _transparentLimbShader = GD.Load<Shader>("res://resources/shaders/character/limb_transparent.gdshader");
	private static readonly Texture2D _defaultFace = GD.Load<Texture2D>("res://assets/textures/client/character/DefaultFace.png");
	private static readonly StringName _albedoParam = "albedo";
	private static readonly StringName _albedoTexParam = "albedo_texture";

	private ImageAsset? _faceImage;
	private MeshAsset? _bodyMesh;
	private readonly ShaderMaterial _headMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _limbMat = new() { Shader = _limbShader };
	private readonly ShaderMaterial _transparentLimbMat = new() { Shader = _transparentLimbShader };
	private readonly Dictionary<GeometryInstance3D, (ShaderMaterial Mat, ShaderMaterial Source)> _limbMats = [];
	private PhysicalBoneSimulator3D _ragdollBoneSim = null!;
	private PhysicalBoneSimulator3D? _lastPhysicalBoneSim = null!;
	private readonly Dictionary<string, float> _blendTargets = [];
	private int _toBeLoadedCount = 0;
	private bool _faceLoaded = false;
	private float _lastLookBlendX = 0;
	private float _lastLookBlendY = 0;
	private bool _faceOverrided = false;
	private bool _bodyOverrided = false;
	private CharacterAnimHelper _helper = null!;
	private readonly Dictionary<CharacterAttachmentEnum, Dynamic> _attachmentEnumToDyn = [];
	private PackedScene? _bodyPkScene;
	private bool _updateClothDirty = false;

	public PhysicalBone3D? VelocityPhysicalBone;

	[Editable, ScriptProperty, Export, SyncVar]
	public Color HeadColor
	{
		get => MeshGetAlbedo(HeadMeshInstance);
		set
		{
			_headMat.Shader = (value.A == 1) ? _limbShader : _transparentLimbShader;
			ApplyAlbedo(HeadMeshInstance, _headMat, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color TorsoColor
	{
		get => MeshGetAlbedo(TorsoMeshInstance);
		set
		{
			MeshSetAlbedo(TorsoMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color LeftArmColor
	{
		get => MeshGetAlbedo(LeftArmMeshInstance);
		set
		{
			MeshSetAlbedo(LeftArmMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color RightArmColor
	{
		get => MeshGetAlbedo(RightArmMeshInstance);
		set
		{
			MeshSetAlbedo(RightArmMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color LeftLegColor
	{
		get => MeshGetAlbedo(LeftLegMeshInstance);
		set
		{
			MeshSetAlbedo(LeftLegMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public Color RightLegColor
	{
		get => MeshGetAlbedo(RightLegMeshInstance);
		set
		{
			MeshSetAlbedo(RightLegMeshInstance, value);
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty, NoSync, Attributes.Obsolete("Use FaceImage instead"), CloneIgnore]
	public int FaceID
	{
		get => (int)((_faceImage is PTImageAsset polyImg) ? polyImg.ImageID : 0);
		set
		{
			if (value == 0) { FaceImage = null; return; }
			PTImageAsset imgAsset = new();
			FaceImage = imgAsset;
			imgAsset.ImageID = (uint)value;
		}
	}

	[Editable, ScriptProperty, SyncVar]
	public ImageAsset? FaceImage
	{
		get => _faceImage;
		set
		{
			if (_faceImage != null && _faceImage != value)
			{
				_faceImage.ResourceLoaded -= OnFaceLoaded;
				_faceImage.UnlinkFrom(this);
			}
			_faceImage = value;

			// Clear current face
			SetLimbTexture(_headMat, new());
			if (_faceImage != null)
			{
				_faceOverrided = true;
				_faceLoaded = false;
				AddLoadCount();
				_faceImage.LinkTo(this);
				_faceImage.ResourceLoaded += OnFaceLoaded;

				if (_faceImage.IsResourceLoaded && _faceImage.Resource != null)
				{
					OnFaceLoaded(_faceImage.Resource);
				}
				else
				{
					_faceImage.QueueLoadResource();
				}
			}
			else
			{
				// Set to default face
				SetLimbTexture(_headMat, _defaultFace);
			}
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public MeshAsset? BodyMesh
	{
		get => _bodyMesh;
		set
		{
			if (_bodyMesh != null && _bodyMesh != value)
			{
				_bodyMesh.ResourceLoaded -= OnBodyLoaded;
				_bodyMesh.UnlinkFrom(this);
			}
			OnBodyLoaded(null);
			_bodyMesh = value;
			if (_bodyMesh != null)
			{
				AddLoadCount();
				_bodyOverrided = true;
				_bodyMesh.LinkTo(this);
				_bodyMesh.ResourceLoaded += OnBodyLoaded;
				if (_bodyMesh.IsResourceLoaded && _bodyMesh.Resource != null)
				{
					OnBodyLoaded(_bodyMesh.Resource);
				}
				else
				{
					_bodyMesh.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}

	[ScriptProperty] public bool Ragdolling { get; private set; } = false;
	[ScriptProperty] public Vector3 RagdollPosition => VelocityPhysicalBone == null ? Vector3.Zero : VelocityPhysicalBone.GlobalPosition;
	[ScriptProperty] public Vector3 RagdollRotation => VelocityPhysicalBone == null ? Vector3.Zero : VelocityPhysicalBone.GlobalRotationDegrees.FlipEuler();

	// These two's not reliable yet, as it doesn't wait for mesh to load. TODO: Come back and fix
	public bool IsAvatarLoaded { get; private set; } = false;
	public event Action? AvatarLoaded;

	[ScriptProperty] public PTSignal RagdollStarted { get; private set; } = new();
	[ScriptProperty] public PTSignal RagdollStopped { get; private set; } = new();

	public override void Init()
	{
		FaceImage = null;

		_helper = new() { Name = "CharacterHelper", Target = this };
		Globals.Singleton.AddChild(_helper, true);

		Skeleton = GDNode.GetNode<Skeleton3D>("Character/Poly/Skeleton3D");
		Skeleton.ShowRestOnly = false;
		_ragdollBoneSim = GDNode.GetNode<PhysicalBoneSimulator3D>("Character/Poly/Skeleton3D/RagdollBone");
		HeadMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/Head");
		TorsoMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/Torso");
		LeftArmMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/LeftArm");
		RightArmMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/RightArm");
		LeftLegMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/LeftLeg");
		RightLegMeshInstance = GDNode.GetNode<MeshInstance3D>("Character/Poly/Skeleton3D/RightLeg");
		Pivot = GDNode.GetNode<Node3D>("Character/Poly");

		Pivot.Scale = NodeSize;

		ApplyAlbedo(HeadMeshInstance, _headMat, _defaultBodyColor);
		ApplyAlbedo(TorsoMeshInstance, _limbMat, _defaultBodyColor);
		ApplyAlbedo(LeftArmMeshInstance, _limbMat, _defaultBodyColor);
		ApplyAlbedo(RightArmMeshInstance, _limbMat, _defaultBodyColor);
		ApplyAlbedo(LeftLegMeshInstance, _limbMat, _defaultBodyColor);
		ApplyAlbedo(RightLegMeshInstance, _limbMat, _defaultBodyColor);

		AnimTree = GDNode.GetNode<AnimationTree>("AnimationTree");
		AnimTree.Active = true;

		base.Init();
		SetProcess(true);
	}

	public override void PreDelete()
	{
		// Free helper
		_helper?.QueueFree();

		// Free materials
		_headMat.Dispose();
		_limbMat.Dispose();
		_transparentLimbMat.Dispose();
		foreach ((ShaderMaterial mat, _) in _limbMats.Values)
		{
			mat.Dispose();
		}
		_limbMats.Clear();

		base.PreDelete();
	}

	public override Node CreateGDNode()
	{
		return Globals.LoadNetworkedObjectScene(ClassName)!;
	}

	public override void EnterTree()
	{
		if (Parent is Physical phy)
		{
			_oldPhyParent = phy;

			// Configure default collision shape for PolytorianModel
			CollisionPivot = new()
			{
				Scale = NodeSize
			};
			CollisionShape = new()
			{
				Shape = _collisionBox
			};
			Physical.SetRemoteLinkOffset(CollisionShape, new(0, 3f - 0.1f, 0));
			Physical.SetRemoteLinkTarget(CollisionShape, CollisionPivot);
			GDNode.AddChild(CollisionPivot);
			CollisionPivot.Position = new(0, -3f, 0);

			phy.GDNode.AddChild(CollisionShape);
			phy.AddCollisionShape(CollisionShape);
			phy.UpdateCollision();
		}
		base.EnterTree();
	}

	public override void ExitTree()
	{
		if (_oldPhyParent != null)
		{
			_oldPhyParent.RemoveCollisionShape(CollisionShape!);
			if (Node.IsInstanceValid(CollisionPivot))
			{
				CollisionPivot.QueueFree();
			}

			CollisionPivot = null;
			CollisionShape = null;
			_crouchBox = null;
			_appliedCrouch = 0f;
		}
		base.ExitTree();
	}

	public override async void Ready()
	{
		if (Root == null)
		{
			// Create default character on null root (eg. loading screens/mobile)
			Animator = New<Animator>();
			Animator.Name = "Animator";
			Animator.Parent = this;
		}

		Animator = await WaitChild<Animator>("Animator", 5);

		if (Animator == null) return;

		AnimTree.AdvanceExpressionBaseNode = _helper.GetPath();

		Animator.SetNetworkAuthority(NetworkAuthority);

		Animator.AnimationTree = AnimTree;
		Animator.AnimatorInit();
		Animator.ImportAnimationRaw("emote_dance", "Dance");
		Animator.ImportAnimationRaw("emote_helicopter", "Helicopter");
		Animator.ImportAnimationRaw("emote_sit", "Sit");
		Animator.ImportAnimationRaw("emote_dance2", "Dance2");

		Animator.ImportOneShotAnimationRaw("emote_wave", "Wave");
		Animator.ImportOneShotAnimationRaw("emote_point", "Point");
		Animator.ImportOneShotAnimationRaw("emote_disagree", "Disagree");
		Animator.ImportOneShotAnimationRaw("emote_agree", "Agree");
		Animator.ImportOneShotAnimationRaw("emote_scream", "Scream");
		Animator.ImportOneShotAnimationRaw("emote_disappointed", "Disappointed");

		/*
		Animator.ImportOneShotAnimationRaw("poly_welcome", "polytorian_2/welcome");
		Animator.ImportOneShotAnimationRaw("avataredit_pose1", "polytorian_2/pose1");
		Animator.ImportOneShotAnimationRaw("avataredit_pose2", "polytorian_2/pose2");
		Animator.ImportOneShotAnimationRaw("avataredit_pose3", "polytorian_2/pose3");
		*/

		Animator.ImportOneShotAnimationRaw("slash", "ToolSlash", true);
		Animator.ImportOneShotAnimationRaw("eat", "ToolEat", true);
		Animator.ImportOneShotAnimationRaw("drink", "ToolDrink", true);
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		Pivot?.Scale = newSize;
		CollisionPivot?.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	public override void Process(double delta)
	{
		base.Process(delta);

		if (_hasPose && _localArmIK == null)
		{
			ApplyNetPose();
		}

		if (_updateClothDirty)
		{
			_updateClothDirty = false;
			UpdateClothMaterials();
		}

		foreach (KeyValuePair<string, float> kvp in _blendTargets)
		{
			string propName = kvp.Key;
			float target = kvp.Value;
			float current = (float)AnimTree.Get(propName);

			float targetBlendSpeed = BlendSpeed;
			float newValue;

			if (propName.Contains("Look"))
			{
				targetBlendSpeed = LookBlendSpeed;

				newValue = Mathf.Lerp(current, target, MathUtils.ExpDecay((float)delta, targetBlendSpeed));
			}
			else
			{
				newValue = Mathf.MoveToward(current, target, (float)delta * targetBlendSpeed);
			}

			AnimTree.Set(propName, newValue);
		}
	}

	private void UpdateClothMaterials()
	{
		// TODO: combine the face into the composite texture
		// currently the head gets a unique material since its face isn't baked into the texture

		ImageTexture composite = null!;
		Clothing[] clothings = GetChildrenOfClass<Clothing>();
		if (clothings.Length != 0)
		{
			Image result = Image.CreateEmpty(ClothingWidth, ClothingHeight, false, ClothingFormat);
			// the loop draws from back to front, like a painter
			// clothing is ordered from front to back
			clothings.Reverse();
			foreach (Clothing clothing in clothings)
			{
				Texture2D? texture = clothing.ClothTexture;
				// Skip unloaded ones
				if (texture != null)
				{
					Image image = texture.GetImage();
					// just in case the clothing isn't the correct format or size
					// Godot will skip these if the format or size already match
					image.Convert(ClothingFormat);
					image.Resize(ClothingWidth, ClothingHeight);
					result.BlendRect(image, _clothingRect, Vector2I.Zero);
				}
			}
			composite = ImageTexture.CreateFromImage(result);
		}
		SetLimbTexture(_limbMat, composite);
		SetLimbTexture(_transparentLimbMat, composite);
	}

	private void OnFaceLoaded(Resource tex)
	{
		SetLimbTexture(_headMat, (Texture2D)tex);
		if (!_faceLoaded)
		{
			_faceLoaded = true;
			AssetLoadCheckout();
		}
	}

	private void AddLoadCount()
	{
		IsAvatarLoaded = false;
		_toBeLoadedCount++;
	}

	private void AssetLoadCheckout()
	{
		_toBeLoadedCount--;
		if (_toBeLoadedCount < 0)
		{
			_toBeLoadedCount = 0;
		}
		if (!IsAvatarLoaded && _toBeLoadedCount == 0)
		{
			IsAvatarLoaded = true;
			AvatarLoaded?.Invoke();
		}
	}

	private void OnBodyLoaded(Resource? resource)
	{
		if (resource is PackedScene scene)
		{
			if (_bodyPkScene == scene) return;
			_bodyPkScene = scene;

			Node n = scene.Instantiate();

			ApplyBodyPart(n, HeadMeshInstance, "Head");
			ApplyBodyPart(n, LeftArmMeshInstance, "LeftArm");
			ApplyBodyPart(n, RightArmMeshInstance, "RightArm");
			ApplyBodyPart(n, LeftLegMeshInstance, "LeftLeg");
			ApplyBodyPart(n, RightLegMeshInstance, "RightLeg");
			ApplyBodyPart(n, TorsoMeshInstance, "Torso");

			n.QueueFree();
		}
		else if (resource == null)
		{
			_bodyPkScene = null;
			ApplyDefaultBodyPart(HeadMeshInstance, "Head");
			ApplyDefaultBodyPart(LeftArmMeshInstance, "LeftArm");
			ApplyDefaultBodyPart(RightArmMeshInstance, "RightArm");
			ApplyDefaultBodyPart(LeftLegMeshInstance, "LeftLeg");
			ApplyDefaultBodyPart(RightLegMeshInstance, "RightLeg");
			ApplyDefaultBodyPart(TorsoMeshInstance, "Torso");
		}
	}

	private static void ApplyDefaultBodyPart(MeshInstance3D m3d, string k)
	{
		m3d.Mesh = GD.Load<Godot.Mesh>($"res://assets/models/bodyparts/default/{k}.tres");
	}

	private static void ApplyBodyPart(Node source, MeshInstance3D target, string sourceName)
	{
		if (source.GetNodeOrNull($"Poly/Skeleton3D/{sourceName}") is MeshInstance3D m3d)
		{
			target.Mesh = m3d.Mesh;
		}
		else
		{
			throw new Exception("Invalid Body Mesh");
		}
	}

	[ScriptMethod]
	public void StartRagdoll(Vector3? force = null)
	{
		force ??= Vector3.Zero;
		Rpc(nameof(NetStartRagdoll), force.Value);
	}

	[ScriptMethod]
	public void StopRagdoll()
	{
		Rpc(nameof(NetStopRagdoll));
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private async void NetStartRagdoll(Vector3 force)
	{
		if (_lastPhysicalBoneSim != null) return;

		// need duplicates cuz godot won't adapt dynamically to bones
		PhysicalBoneSimulator3D s = (PhysicalBoneSimulator3D)_ragdollBoneSim.Duplicate();

		VelocityPhysicalBone = s.GetNode<PhysicalBone3D>("Physical Bone UpperTorso");

		Skeleton.AddChild(s);

		s.Active = true;
		s.PhysicalBonesStartSimulation();

		_lastPhysicalBoneSim = s;

		VelocityPhysicalBone.LinearVelocity = force / VelocityPhysicalBone.GravityScale;
		Ragdolling = true;
		RagdollStarted.Invoke();
	}

	[NetRpc(AuthorityMode.Authority, CallLocal = true, TransferMode = TransferMode.Reliable)]
	private void NetStopRagdoll()
	{
		if (_lastPhysicalBoneSim == null) return;

		_lastPhysicalBoneSim.PhysicalBonesStopSimulation();
		_lastPhysicalBoneSim.Active = false;
		_lastPhysicalBoneSim.QueueFree();
		_lastPhysicalBoneSim = null;

		Ragdolling = false;
		RagdollStopped.Invoke();
	}

	[ScriptMethod]
	public override Dynamic GetAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		if (!_attachmentEnumToDyn.TryGetValue(attachmentEnum, out Dynamic? dyn))
		{
			Node3D a = GetNode3DAttachment(attachmentEnum);
			dyn = New<Dynamic>();
			dyn.OverrideGDNode(a);
		}

		return dyn;
	}

	public Node3D GetNode3DAttachment(CharacterAttachmentEnum attachmentEnum)
	{
		Node3D result = attachmentEnum switch
		{
			CharacterAttachmentEnum.Head => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_Head/HeadAttachment"),
			CharacterAttachmentEnum.UpperTorso => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperTorso/UpperTorsoAttachment"),
			CharacterAttachmentEnum.LowerTorso => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerTorso/LowerTorsoAttachment"),
			CharacterAttachmentEnum.ShoulderLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperArm_L/ShoulderLeftAttachment"),
			CharacterAttachmentEnum.ShoulderRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperArm_R/RightShoulderAttachment"),
			CharacterAttachmentEnum.ElbowLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerArm_L/LeftElbowAttachment"),
			CharacterAttachmentEnum.ElbowRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerArm_R/RightElbowAttachment"),
			CharacterAttachmentEnum.HandLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_Hand_L/LeftHandAttachment"),
			CharacterAttachmentEnum.HandRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_Hand_R/RightHandAttachment"),
			CharacterAttachmentEnum.LegLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperLeg_L/LeftLegAttachment"),
			CharacterAttachmentEnum.LegRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_UpperLeg_R/RightLegAttachment"),
			CharacterAttachmentEnum.KneeLeft => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerLeg_L/LeftKneeAttachment"),
			CharacterAttachmentEnum.KneeRight => GDNode.GetNode<Node3D>("Character/Poly/Skeleton3D/O_LowerLeg_R/RightKneeAttachment"),
			_ => throw new NotImplementedException(),
		};

		return result;
	}

	public override void RecvBlendValue(CharacterModelBlendEnum blendName, float blendValue)
	{
		string propName = "";
		switch (blendName)
		{
			case CharacterModelBlendEnum.Sitting:
				propName = "parameters/Sit/blend_amount";
				break;
			case CharacterModelBlendEnum.ToolHoldLeft:
				propName = "parameters/GearHold_L/blend_amount";
				break;
			case CharacterModelBlendEnum.ToolHoldRight:
				propName = "parameters/GearHold_R/blend_amount";
				break;
			case CharacterModelBlendEnum.LookX:
				propName = "parameters/LookXAdd/add_amount";
				break;
			case CharacterModelBlendEnum.LookY:
				propName = "parameters/LookYAdd/add_amount";
				break;
		}

		if (propName != "")
		{
			_blendTargets[propName] = blendValue;
		}
	}

	public override void RecvSpeedValue(float speedValue)
	{
		if (AnimTree == null) return;
		AnimTree.Set("parameters/TimeScale/scale", speedValue);
	}

	private bool _xrArmsHooked;
	private Polytoria.Shared.XRArmIK? _localArmIK;

	public override void ApplyCameraModifier(Camera camera)
	{
		if (!_xrArmsHooked && Polytoria.Shared.XRBootstrap.IsActive
			&& Skeleton != null
			&& Polytoria.Shared.XRControlBridge.LeftController != null
			&& Polytoria.Shared.XRControlBridge.RightController != null)
		{
			_xrArmsHooked = true;
			_localArmIK = new Polytoria.Shared.XRArmIK(
				Skeleton,
				Polytoria.Shared.XRControlBridge.LeftController,
				Polytoria.Shared.XRControlBridge.RightController);
			Skeleton.AddChild(_localArmIK);
		}

		Camera3D cam3D = camera.Camera3D;
		Transform3D camTransform = cam3D.GlobalTransform;
		Transform3D charTransform = GetGlobalTransform();

		if (Polytoria.Shared.XRBootstrap.IsActive && Skeleton != null)
		{
			if (!_xrHeadHooked)
			{
				_xrHeadHooked = true;
				EnsureHeadIK();
			}
			EnsureCrouchIK();
			if (_crouchIK != null)
			{
				_crouchIK.CrouchWorld = Polytoria.Shared.XRBootstrap.CrouchWorld;
				_crouchIK.Moving = CurrentState is CharacterModelStateEnum.Walking or CharacterModelStateEnum.Running;

				Vector3 headWorld = camTransform.Origin;
				Vector3 fwdFlat = -camTransform.Basis.Z;
				fwdFlat.Y = 0;
				if (fwdFlat.LengthSquared() > 1e-4f)
				{
					headWorld -= fwdFlat.Normalized() * (Polytoria.Shared.XRBootstrap.EyeForwardOffsetMeters * (float)XRServer.WorldScale);
				}
				_crouchIK.HeadTargetLocal = Skeleton.GlobalTransform.AffineInverse() * headWorld;
			}
			Polytoria.Shared.XRBootstrap.MinCrouchWorld = ApplyBodyCrouch(Polytoria.Shared.XRBootstrap.CrouchWorld);

			Basis camBasis = camTransform.Basis;
			Basis skelBasisInv = Skeleton.GlobalTransform.Basis.Inverse();
			Basis headLocalBasis = (skelBasisInv * camBasis * Polytoria.Shared.XRBootstrap.BodyYawCorrection).Orthonormalized();

			Vector3 euler = headLocalBasis.GetEuler();
			float pitchLimit = Mathf.DegToRad(70f);
			float yawLimit = Mathf.DegToRad(110f);
			float rollLimit = Mathf.DegToRad(45f);
			euler.X = Mathf.Clamp(euler.X, -pitchLimit, pitchLimit);
			euler.Y = Mathf.Clamp(euler.Y, -yawLimit, yawLimit);
			euler.Z = Mathf.Clamp(euler.Z, -rollLimit, rollLimit);
			Quaternion clampedQ = Basis.FromEuler(euler).GetRotationQuaternion();

			if (_headIK != null) _headIK.TargetRotationLocal = clampedQ;

			double now = Time.GetTicksMsec() / 1000.0;
			if (now >= _lastNetHeadUpdateTime + NetHeadUpdateInterval)
			{
				_lastNetHeadUpdateTime = now;
				Transform3D lt = _localArmIK?.LastLeftTargetLocal ?? Transform3D.Identity;
				Transform3D rt = _localArmIK?.LastRightTargetLocal ?? Transform3D.Identity;
				Quaternion lq = lt.Basis.GetRotationQuaternion();
				Quaternion rq = rt.Basis.GetRotationQuaternion();
				Vector3 torsoOffset = _crouchIK?.LastTorsoOffset ?? Vector3.Zero;
				Rpc(nameof(NetRecvPose),
					clampedQ.X, clampedQ.Y, clampedQ.Z, clampedQ.W,
					lt.Origin.X, lt.Origin.Y, lt.Origin.Z, lq.X, lq.Y, lq.Z, lq.W,
					rt.Origin.X, rt.Origin.Y, rt.Origin.Z, rq.X, rq.Y, rq.Z, rq.W,
					Polytoria.Shared.XRBootstrap.CrouchWorld,
					torsoOffset.X, torsoOffset.Y, torsoOffset.Z);
			}
			return;
		}

		Vector3 camForward = -camTransform.Basis.Z.Normalized();

		Vector3 localForward = charTransform.Basis.Inverse() * camForward;
		localForward = localForward.Normalized();

		float lookY = Mathf.Clamp(localForward.Y, -1f, 1f);
		float lookX = -localForward.X;

		if (lookX != _lastLookBlendX)
		{
			_lastLookBlendX = lookX;
		}

		if (lookY != _lastLookBlendY)
		{
			_lastLookBlendY = lookY;
		}

		NetRecvLookBlend(lookY, lookX);

		if (Time.GetTicksMsec() / 1000.0 >= _lastNetUpdateTime + NetLookBlendUpdateInterval)
		{
			_lastNetUpdateTime = Time.GetTicksMsec() / 1000.0;
			Rpc(nameof(NetRecvLookBlend), lookY, lookX);
		}
	}

	private void EnsureHeadIK()
	{
		if (_headIK != null || Skeleton == null) return;
		_headIK = new Polytoria.Shared.XRHeadIK(Skeleton);
		Skeleton.AddChild(_headIK);
	}

	private void EnsureCrouchIK()
	{
		if (_crouchIK != null || Skeleton == null) return;
		_crouchIK = new Polytoria.Shared.XRCrouchIK(Skeleton);
		Skeleton.AddChild(_crouchIK);
		Skeleton.MoveChild(_crouchIK, 0);
	}

	private const float HandBodyFollowRate = 50f;
	private const float HandBodySnapDistance = 3f;
	private const float HandBodyMaxExtrapolation = 0.08f;

	private AnimatableBody3D? _lHandBody;
	private AnimatableBody3D? _rHandBody;
	private Vector3 _lHandTarget;
	private Vector3 _rHandTarget;
	private Vector3 _lHandPrev;
	private Vector3 _rHandPrev;
	private double _handTargetTime;
	private float _handTargetDt = 1f / 30f;

	private void UpdateServerHandBodies(Vector3 leftLocal, Vector3 rightLocal)
	{
		if (Skeleton == null) return;
		Transform3D skel = Skeleton.GlobalTransform;
		if (_lHandBody == null || _rHandBody == null)
		{
			_lHandBody = CreateHandBody();
			_rHandBody = CreateHandBody();
			_lHandBody.GlobalPosition = skel * leftLocal;
			_rHandBody.GlobalPosition = skel * rightLocal;
			SetPhysicsProcess(true);
		}
		double now = Time.GetTicksMsec() / 1000.0;
		_handTargetDt = Mathf.Clamp((float)(now - _handTargetTime), 0.01f, 0.2f);
		_handTargetTime = now;
		_lHandPrev = _lHandTarget;
		_rHandPrev = _rHandTarget;
		_lHandTarget = skel * leftLocal;
		_rHandTarget = skel * rightLocal;
	}

	public override void PhysicsProcess(double delta)
	{
		if (_lHandBody != null && _rHandBody != null)
		{
			float ahead = Mathf.Min((float)(Time.GetTicksMsec() / 1000.0 - _handTargetTime), HandBodyMaxExtrapolation) / _handTargetDt;
			MoveHandBody(_lHandBody, _lHandTarget + (_lHandTarget - _lHandPrev) * ahead, delta);
			MoveHandBody(_rHandBody, _rHandTarget + (_rHandTarget - _rHandPrev) * ahead, delta);
		}
		base.PhysicsProcess(delta);
	}

	private static void MoveHandBody(AnimatableBody3D body, Vector3 target, double delta)
	{
		Vector3 pos = body.GlobalPosition;
		if (pos.DistanceSquaredTo(target) > HandBodySnapDistance * HandBodySnapDistance)
		{
			body.GlobalPosition = target;
			return;
		}
		body.GlobalPosition = pos.Lerp(target, 1f - Mathf.Exp(-HandBodyFollowRate * (float)delta));
	}

	private AnimatableBody3D CreateHandBody()
	{
		var body = new AnimatableBody3D
		{
			SyncToPhysics = true,
			CollisionLayer = 1,
			CollisionMask = 0,
			TopLevel = true,
		};
		body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.28f } });
		Skeleton!.AddChild(body);
		if (Parent is Physical phy && phy.GDNode is PhysicsBody3D playerBody)
		{
			body.AddCollisionExceptionWith(playerBody);
		}
		return body;
	}

	private BoxShape3D? _crouchBox;
	private float _appliedCrouch;

	private float ApplyBodyCrouch(float crouchWorld)
	{
		float scaleY = Mathf.Max(NodeSize.Y, 0.01f);
		if (CollisionShape == null) return _appliedCrouch * scaleY;
		float crouch = Mathf.Clamp(crouchWorld / scaleY, 0f, _collisionBox.Size.Y * 0.5f);
		if (Mathf.Abs(crouch - _appliedCrouch) < 0.01f) return _appliedCrouch * scaleY;
		if (crouch < _appliedCrouch && !HasHeadroom(crouch)) return _appliedCrouch * scaleY;

		if (_crouchBox == null)
		{
			_crouchBox = (BoxShape3D)_collisionBox.Duplicate();
			CollisionShape.Shape = _crouchBox;
		}
		_appliedCrouch = crouch;
		_crouchBox.Size = _collisionBox.Size with { Y = _collisionBox.Size.Y - crouch };
		Physical.SetRemoteLinkOffset(CollisionShape, new(0, 3f - 0.1f - crouch * 0.5f, 0));
		return _appliedCrouch * scaleY;
	}

	private BoxShape3D? _headroomProbe;

	private bool HasHeadroom(float targetCrouch)
	{
		float growth = _appliedCrouch - targetCrouch;
		if (growth <= 0f || CollisionShape == null) return true;
		PhysicsDirectSpaceState3D? space = CollisionShape.GetWorld3D()?.DirectSpaceState;
		if (space == null) return true;
		if (Parent is not Physical phy || phy.GDNode is not PhysicsBody3D body) return true;

		_headroomProbe ??= new BoxShape3D();
		_headroomProbe.Size = new Vector3(_collisionBox.Size.X * 0.9f, growth, _collisionBox.Size.Z * 0.9f);

		float currentHalf = (_collisionBox.Size.Y - _appliedCrouch) * 0.5f;
		Transform3D xf = CollisionShape.GlobalTransform;
		xf.Origin += xf.Basis * new Vector3(0f, currentHalf + growth * 0.5f, 0f);

		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = _headroomProbe,
			Transform = xf,
			CollisionMask = body.CollisionMask,
			Exclude = [body.GetRid()],
		};
		return space.IntersectShape(query, 1).Count == 0;
	}

	private const double PoseInterpDelay = 0.05;

	private struct NetPose
	{
		public double T;
		public Quaternion Head;
		public Transform3D Left;
		public Transform3D Right;
		public float Crouch;
		public Vector3 Torso;
	}

	private NetPose _poseA;
	private NetPose _poseB;
	private bool _hasPose;

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvPose(
		float hx, float hy, float hz, float hw,
		float lx, float ly, float lz, float lqx, float lqy, float lqz, float lqw,
		float rx, float ry, float rz, float rqx, float rqy, float rqz, float rqw,
		float crouch, float tox, float toy, float toz)
	{
		if (_localArmIK != null) return;

		NetPose pose = new()
		{
			T = Time.GetTicksMsec() / 1000.0,
			Head = new Quaternion(hx, hy, hz, hw).Normalized(),
			Left = new Transform3D(new Basis(new Quaternion(lqx, lqy, lqz, lqw).Normalized()), new Vector3(lx, ly, lz)),
			Right = new Transform3D(new Basis(new Quaternion(rqx, rqy, rqz, rqw).Normalized()), new Vector3(rx, ry, rz)),
			Crouch = crouch,
			Torso = new Vector3(tox, toy, toz),
		};
		_poseA = _hasPose ? _poseB : pose;
		_poseB = pose;
		_hasPose = true;

		if (Root.Network.IsServer)
		{
			UpdateServerHandBodies(pose.Left.Origin, pose.Right.Origin);
		}
	}

	private void ApplyNetPose()
	{
		if (Skeleton == null) return;

		float t = 1f;
		double span = _poseB.T - _poseA.T;
		if (span > 0.0005)
		{
			double renderTime = Time.GetTicksMsec() / 1000.0 - PoseInterpDelay;
			t = Mathf.Clamp((float)((renderTime - _poseA.T) / span), 0f, 1f);
		}

		EnsureHeadIK();
		if (_headIK != null) _headIK.TargetRotationLocal = _poseA.Head.Slerp(_poseB.Head, t);

		if (_remoteArmIK == null)
		{
			_remoteArmIK = new Polytoria.Shared.XRArmIK(Skeleton, null, null);
			Skeleton.AddChild(_remoteArmIK);
		}
		_remoteArmIK.OverrideLeftTargetLocal = _poseA.Left.InterpolateWith(_poseB.Left, t);
		_remoteArmIK.OverrideRightTargetLocal = _poseA.Right.InterpolateWith(_poseB.Right, t);

		float crouch = Mathf.Lerp(_poseA.Crouch, _poseB.Crouch, t);
		EnsureCrouchIK();
		if (_crouchIK != null)
		{
			_crouchIK.CrouchWorld = crouch;
			_crouchIK.Moving = CurrentState is CharacterModelStateEnum.Walking or CharacterModelStateEnum.Running;
			_crouchIK.OverrideTorsoOffset = _poseA.Torso.Lerp(_poseB.Torso, t);
		}
		ApplyBodyCrouch(crouch);
	}

	[NetRpc(AuthorityMode.Authority, TransferMode = TransferMode.UnreliableOrdered)]
	private void NetRecvLookBlend(float lookYBlend, float lookXBlend)
	{
		RecvBlendValue(CharacterModelBlendEnum.LookX, lookXBlend);
		RecvBlendValue(CharacterModelBlendEnum.LookY, lookYBlend);
	}

	[ScriptMethod]
	public void LoadAppearance(int userID, bool loadTool = true)
	{
		ClearAppearance();
		_ = SafeLoadAppearance(userID, loadTool);
	}

	private async Task SafeLoadAppearance(int userID, bool loadTool)
	{
		try
		{
			await InternalLoadAppearance(userID, loadTool);
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			PT.PrintErr("LoadAppearance failed for userID=", userID, ": ", ex);
		}
	}

	[ScriptMethod]
	public void ClearAppearance()
	{
		HeadColor = _defaultBodyColor;
		TorsoColor = _defaultBodyColor;
		LeftArmColor = _defaultBodyColor;
		RightArmColor = _defaultBodyColor;
		LeftLegColor = _defaultBodyColor;
		RightLegColor = _defaultBodyColor;
		FaceImage = null;
		_faceOverrided = false;
		_bodyOverrided = false;

		foreach (Instance item in GetChildren())
		{
			if (item is Accessory or Clothing)
			{
				item.Delete();
			}
		}
	}

	private void MeshSetAlbedo(GeometryInstance3D mesh, Color albedo)
	{
		ShaderMaterial source = (albedo.A == 1) ? _limbMat : _transparentLimbMat;
		ApplyAlbedo(mesh, source, albedo);
	}

	private Color MeshGetAlbedo(GeometryInstance3D mesh)
	{
		return _limbMats.TryGetValue(mesh, out (ShaderMaterial Mat, ShaderMaterial Source) entry)
			? (Color)entry.Mat.GetShaderParameter(_albedoParam)
			: _defaultBodyColor;
	}

	private void ApplyAlbedo(GeometryInstance3D mesh, ShaderMaterial source, Color albedo)
	{
		if (!_limbMats.TryGetValue(mesh, out (ShaderMaterial Mat, ShaderMaterial Source) entry))
		{
			entry = (new ShaderMaterial(), source);
		}
		else
		{
			entry.Source = source;
		}

		entry.Mat.Shader = source.Shader;
		entry.Mat.SetShaderParameter(_albedoTexParam, source.GetShaderParameter(_albedoTexParam));
		entry.Mat.SetShaderParameter(_albedoParam, albedo);
		_limbMats[mesh] = entry;
		mesh.MaterialOverride = entry.Mat;
	}

	private void SetLimbTexture(ShaderMaterial source, Variant texture)
	{
		source.SetShaderParameter(_albedoTexParam, texture);
		foreach ((ShaderMaterial mat, ShaderMaterial src) in _limbMats.Values)
		{
			if (ReferenceEquals(src, source))
			{
				mat.SetShaderParameter(_albedoTexParam, texture);
			}
		}
	}

	internal async Task<AvatarLoadResponse> InternalLoadAppearance(int userID, bool loadTool = false, bool loadToolNpc = false)
	{
		_loadAppearanceCount++;

		// Prevent reloading
		int myCount = _loadAppearanceCount;

		APIAvatarResponse avatarData = await FetchAvatar(userID);
		if (myCount != _loadAppearanceCount) throw new OperationCanceledException("The avatar is cancelled");

		if (IsDeleted)
		{
			throw new OperationCanceledException("The avatar is deleted");
		}

		// Apply body color
		HeadColor = Color.FromString(avatarData.Colors.Head, _defaultBodyColor);
		TorsoColor = Color.FromString(avatarData.Colors.Torso, _defaultBodyColor);
		LeftArmColor = Color.FromString(avatarData.Colors.LeftArm, _defaultBodyColor);
		RightArmColor = Color.FromString(avatarData.Colors.RightArm, _defaultBodyColor);
		LeftLegColor = Color.FromString(avatarData.Colors.LeftLeg, _defaultBodyColor);
		RightLegColor = Color.FromString(avatarData.Colors.RightLeg, _defaultBodyColor);

		bool hasTool = false;
		List<Task> asyncLoads = [];

		foreach (APIAvatarAsset asset in avatarData.Assets)
		{
			if (asset.Type == "clothing")
			{
				PTImageAsset txt = New<PTImageAsset>();
				txt.DirectURL = asset.Path ?? "";
				txt.ImageID = (uint)asset.ID;
				Clothing c = New<Clothing>();
				c.Name = asset.Name;
				c.Image = txt;
				c.Parent = this;
			}
			else if (asset.Type == "face")
			{
				if (_faceOverrided) continue;
				PTImageAsset face = New<PTImageAsset>();
				face.DirectURL = asset.Path ?? "";
				face.ImageID = (uint)asset.ID;
				FaceImage = face;
			}
			else if (asset.Type == "body")
			{
				if (_bodyOverrided) continue;
				var body = New<PTMeshAsset>();
				body.DirectURL = asset.Path ?? "";
				body.AssetID = (uint)asset.ID;
				BodyMesh = body;
			}
			else if (asset.Type == "hat")
			{
				asyncLoads.Add(LoadHatAsync(asset, myCount));
			}
			else if (asset.Type == "tool")
			{
				if (Parent is Player plr && loadTool)
				{
					hasTool = true;
					asyncLoads.Add(LoadToolForPlayerAsync(asset, plr, myCount));
				}
				else if (Parent is NPC npc && loadToolNpc)
				{
					hasTool = true;
					asyncLoads.Add(LoadToolForNpcAsync(asset, npc, myCount));
				}
			}
		}

		if (asyncLoads.Count > 0)
		{
			await Task.WhenAll(asyncLoads);
		}

		AssetLoadCheckout();

		return new() { HasTool = hasTool };
	}

	private static async Task<APIAvatarResponse> FetchAvatar(int userID)
	{
		const int MaxAttempts = 3;
		Exception? lastError = null;
		for (int attempt = 0; attempt < MaxAttempts; attempt++)
		{
			try
			{
				return await PolyAPI.GetUserAvatarFromID(userID);
			}
			catch (System.Net.Http.HttpRequestException ex)
			{
				lastError = ex;
				if (attempt < MaxAttempts - 1)
				{
					await Task.Delay(500 * (1 << attempt));
				}
			}
		}
		throw lastError ?? new System.Net.Http.HttpRequestException("avatar fetch failed");
	}

	private async Task LoadHatAsync(APIAvatarAsset asset, int myCount)
	{
		try
		{
			Accessory? accessory = await Root.Insert.AccessoryAsync(asset.ID, asset.Path);
			if (myCount != _loadAppearanceCount) { accessory?.Delete(); return; }
			if (IsDeleted) { accessory?.Delete(); return; }
			accessory?.Parent = this;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	private async Task LoadToolForPlayerAsync(APIAvatarAsset asset, Player plr, int myCount)
	{
		try
		{
			Tool? tool = await Root.Insert.ToolAsync(asset.ID, asset.Path);
			if (myCount != _loadAppearanceCount) { tool?.Delete(); return; }
			if (IsDeleted) { tool?.Delete(); return; }
			tool?.Parent = plr.Inventory;
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	private async Task LoadToolForNpcAsync(APIAvatarAsset asset, NPC npc, int myCount)
	{
		try
		{
			Tool? tool = await Root.Insert.ToolAsync(asset.ID, asset.Path);
			if (myCount != _loadAppearanceCount) { tool?.Delete(); return; }
			if (IsDeleted) { tool?.Delete(); return; }
			if (tool != null) npc.EquipTool(tool);
		}
		catch (Exception ex)
		{
			PT.PrintErr(ex);
		}
	}

	internal async Task WaitForAppearanceLoad()
	{
		if (FaceImage != null && !FaceImage.IsResourceLoaded)
		{
			await FaceImage.ResourceLoadedInternal.Wait();
		}
		if (BodyMesh != null && !BodyMesh.IsResourceLoaded)
		{
			await BodyMesh.ResourceLoadedInternal.Wait();
		}

		Instance checkOn = this;

		// Check on NPC for loading tools
		if (Parent is NPC)
		{
			checkOn = Parent;
		}

		foreach (var item in checkOn.GetDescendants())
		{
			if (item is Mesh m)
			{
				if (m.Loading)
				{
					await m.Loaded.Wait();
				}
			}
			else if (item is Clothing c)
			{
				if (c.Image != null && !c.Image.IsResourceLoaded)
				{
					await c.Image.ResourceLoadedInternal.Wait();
				}
			}
		}
	}

	internal void QueueRenderCloth()
	{
		_updateClothDirty = true;
	}

	public void SetAnimationOverrideTo(bool to)
	{
		AnimTree.Active = !to;
	}

	internal struct AvatarLoadResponse()
	{
		public bool HasTool = false;
	}
}
