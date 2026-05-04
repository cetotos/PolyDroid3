// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Attributes;
using Polytoria.Datamodel.Resources;

namespace Polytoria.Datamodel;

[Instantiable]
public sealed partial class Decal : Dynamic
{
	private ImageAsset? _asset;
	private Godot.Decal _decal = null!;

	private Color _color = new(1, 1, 1);
	private float _energy;

	[Editable, ScriptProperty]
	public ImageAsset? Image
	{
		get => _asset;
		set
		{
			if (_asset != null && _asset != value)
			{
				_asset.ResourceLoaded -= OnResourceLoaded;
				_asset.UnlinkFrom(this);
			}
			_asset = value;
			OnResourceLoaded(null);
			if (_asset != null)
			{
				_asset.LinkTo(this);
				_asset.ResourceLoaded += OnResourceLoaded;
				if (_asset.IsResourceLoaded && _asset.Resource != null)
				{
					OnResourceLoaded(_asset.Resource);
				}
				else
				{
					_asset.QueueLoadResource();
				}
			}
			OnPropertyChanged();
		}
	}


	[Editable, ScriptProperty, DefaultValue(1)]
	public float Energy
	{
		get => _energy;
		set
		{
			_energy = value;
			_decal.EmissionEnergy = value;
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public Color Color
	{
		get => _color;
		set
		{
			_color = value;
			_decal.Modulate = value;
			OnPropertyChanged();
		}
	}

	public override void Init()
	{
		_decal = new()
		{
			Size = Vector3.One,
			CullMask = 1
		};
		GDNode.AddChild(_decal, @internal: Node.InternalMode.Back);
		Energy = 1;

		base.Init();
	}

	internal override void OnNodeSizeChanged(Vector3 newSize)
	{
		_decal.Scale = newSize;
		base.OnNodeSizeChanged(newSize);
	}

	private void OnResourceLoaded(Resource? tex)
	{
		_decal.TextureAlbedo = (Texture2D?)tex ?? null;
	}
}
