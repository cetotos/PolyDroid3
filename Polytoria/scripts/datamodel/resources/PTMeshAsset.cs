// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Polytoria.Attributes;
using Polytoria.Shared.AssetLoaders;

namespace Polytoria.Datamodel.Resources;

[Instantiable]
public partial class PTMeshAsset : MeshAsset
{
	private uint _assetID;
	private string _directURL = "";

	[Editable, ScriptProperty]
	public uint AssetID
	{
		get => _assetID;
		set
		{
			_assetID = value;
			LoadResource();
			OnPropertyChanged();
		}
	}

	[Editable, ScriptProperty]
	public string DirectURL
	{
		get => _directURL;
		set
		{
			_directURL = value ?? "";
			LoadResource();
		}
	}

	public static void RegisterAsset()
	{
		RegisterType<PTMeshAsset>();
	}

	public override void LoadResource()
	{
		AssetLoader.Singleton.GetResource(
			new() { Type = ResourceType.Mesh, ID = AssetID, DirectURL = _directURL },
			InvokeResourceLoaded
		);
	}
}
