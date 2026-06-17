// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Datamodel;
using Polytoria.Formats;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Script = Polytoria.Datamodel.Script;

namespace Polytoria.Shared;

public static class DatamodelLoader
{
	public static async Task LoadWorldFile(World root, string filePath, string? entryPath = null)
	{
		await LoadWorldBytes(root, File.ReadAllBytes(filePath), entryPath);
	}

	public static async Task<Instance?> LoadModelFile(World root, string filePath, Instance? parent = null)
	{
		return await LoadModelBytes(root, await File.ReadAllBytesAsync(filePath), parent);
	}

	public static async Task LoadWorldBytes(World root, byte[] data, string? entryPath = null)
	{
		PolyFileTypeEnum fileType = DetermineFileTypeFromBytes(data);
		PT.Print("Determined file type: ", fileType);
		if (fileType == PolyFileTypeEnum.PolyXML)
		{
			if (data[0] == 0xEF)
			{
				data = [.. data.Skip(3)];
			}

			// XML Format
			await XmlFormat.LoadString(root, data.GetStringFromUtf8());
		}
		else if (fileType == PolyFileTypeEnum.PolyData)
		{
			PolyFormat.LoadWorld(root, data);
		}
		else if (fileType == PolyFileTypeEnum.Packed)
		{
			// Poly Format
			PackedFormat.LoadPackedWorld(root, data, entryPath);
		}
	}

	public static async Task<string> GetImportFolderName(byte[] data)
	{
		PolyFileTypeEnum fileType = DetermineFileTypeFromBytes(data);
		if (fileType == PolyFileTypeEnum.PolyXML)
		{
			if (data[0] == 0xEF)
			{
				data = [.. data.Skip(3)];
			}

			XmlFormat.GameItem gameItem = XmlFormat.ParseContent(data.GetStringFromUtf8());

			return gameItem.Name ?? "Model";
		}
		else if (fileType == PolyFileTypeEnum.PolyData)
		{
			PolyFormat.PolyRootData rootData = PolyFormat.ReadRootDataBytes(data);
			return rootData.Objects.Length > 0 ? rootData.Objects[0].Name : "Model";
		}
		else if (fileType == PolyFileTypeEnum.Packed)
		{
			PackedFormat.ModelData? modelData = PackedFormat.ReadModelData(data);

			if (modelData == null) return "";
			return modelData.Value.ModelName;
		}
		return "";
	}

	public static async Task<Instance?> LoadModelBytes(World root, byte[] data, Instance? parent = null, string? modelNameOverride = null)
	{
		parent ??= root.TemporaryContainer;

		PolyFileTypeEnum fileType = DetermineFileTypeFromBytes(data);
		string modelName = modelNameOverride ?? await GetImportFolderName(data);
		string baseFolder = Globals.ToolboxFolderName + "/" + modelName + "/";

		if (fileType == PolyFileTypeEnum.PolyXML)
		{
			if (data[0] == 0xEF)
			{
				data = [.. data.Skip(3)];
			}

			// XML Format
			Instance? m = await XmlFormat.LoadModelString(root, data.GetStringFromUtf8(), parent);

			if (m != null)
			{
				// iterate through scripts
				foreach (Instance item in m.GetDescendants())
				{
					item.ModelRoot = m;
					if (item is Script s)
					{
						string scriptPath = baseFolder + s.CreateLuaFileName();
						root.IO.WriteBytesToPath(scriptPath, s.Source.ToUtf8Buffer());
						s.LinkedScript = root.Assets.GetFileLinkByPath(scriptPath);
					}
				}
#if CREATOR
				// Save model to linked session
				if (root.LinkedSession != null)
				{
					string modelPath = baseFolder + modelName + ".model";
					root.LinkedSession.SaveModel(m, modelPath);
				}
#endif
			}
			return m;
		}
		else if (fileType == PolyFileTypeEnum.PolyData)
		{
			Instance? m = PolyFormat.LoadModel(root, data, parent);
			if (m != null)
			{
#if CREATOR
				if (root.LinkedSession != null)
				{
					string modelPath = baseFolder + modelName + ".model";
					root.LinkedSession.SaveModel(m, modelPath);
				}
#endif
				return m;
			}
		}
		else if (fileType == PolyFileTypeEnum.Packed)
		{
			Instance? packedModel = PackedFormat.LoadPackedModel(root, data, parent);
			if (packedModel != null)
			{
#if CREATOR
				// Save model to linked session
				if (root.LinkedSession != null)
				{
					string modelPath = baseFolder + modelName + ".model";
					root.LinkedSession.SaveModel(packedModel, modelPath);
				}
#endif
				return packedModel;
			}
		}
		return null;
	}

	public static async Task<PolyFileTypeEnum> DetermineFileType(string filePath)
	{
		byte[] b = await File.ReadAllBytesAsync(filePath);
		return DetermineFileTypeFromBytes(b);
	}

	public static PolyFileTypeEnum DetermineFileTypeFromBytes(byte[] data)
	{
		if (data.Length <= 0) return PolyFileTypeEnum.Empty;
		if (data[0] == 0xEF || data[0] == 0x3C)
		{
			return PolyFileTypeEnum.PolyXML;
		}
		if (data[0] == 0x7B || data[0] == 0x28)
		{
			return PolyFileTypeEnum.PolyData;
		}
		return PolyFileTypeEnum.Packed;
	}
}

public enum PolyFileTypeEnum
{
	Packed,
	PolyXML,
	PolyData,
	Empty
}
