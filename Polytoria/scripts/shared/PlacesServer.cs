// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Godot;
using Polytoria.Schemas.API;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Polytoria.Shared;

public sealed class PlaceMeta
{
	[JsonPropertyName("creatorType")] public string? CreatorType { get; set; }
	[JsonPropertyName("creatorID")] public int? CreatorID { get; set; }
	[JsonPropertyName("name")] public string? Name { get; set; }
	[JsonPropertyName("description")] public string? Description { get; set; }
	[JsonPropertyName("genre")] public string? Genre { get; set; }
	[JsonPropertyName("placeType")] public string? PlaceType { get; set; }
	[JsonPropertyName("genreIcon")] public string? GenreIcon { get; set; }
	[JsonPropertyName("creatorName")] public string? CreatorName { get; set; }
	[JsonPropertyName("creatorThumbnail")] public string? CreatorThumbnail { get; set; }
	[JsonPropertyName("iconUrl")] public string? IconUrl { get; set; }
	[JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; }
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PlaceMeta))]
internal partial class PlaceMetaJsonContext : JsonSerializerContext { }

public static class PlacesServer
{
	public const int Port = 8124;
	public const int FirstHostPort = 8888;

	private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan WakeWaitDeadline = WarmupTimeout + TimeSpan.FromSeconds(5);

	private const string PlacesSubdir = "places";
	private const string MetaExtension = ".json";
	private const string CountExtension = ".count";

	private sealed record Entry(int Id, string Name, string FilePath, PlaceMeta Meta);

	private enum PlaceState { Cold, Warming, Running, Stopping }

	private sealed class PlaceRuntime
	{
		public PlaceState State = PlaceState.Cold;
		public Process? Process;
		public DateTime LastActiveAt;
		public readonly object Lock = new();
	}

	private static readonly List<Entry> _entries = new();
	private static readonly Dictionary<int, PlaceRuntime> _runtimes = new();
	private static readonly object _runtimesLock = new();
	private static TcpListener? _listener;
	private static Thread? _thread;
	private static Thread? _watchdogThread;

	public static string? GetPlaceFilePath(int oneBasedIndex)
	{
		if (_entries.Count == 0) ScanPlaces();
		if (oneBasedIndex < 1 || oneBasedIndex > _entries.Count) return null;
		return _entries[oneBasedIndex - 1].FilePath;
	}

	public static PlaceMeta GetPlaceMetaForFile(string placeFilePath)
	{
		string dir = Path.GetDirectoryName(placeFilePath) ?? "";
		string nameNoExt = Path.GetFileNameWithoutExtension(placeFilePath);
		return LoadMeta(Path.Combine(dir, nameNoExt + MetaExtension));
	}

	public static void WritePlayerCount(string placeFilePath, int count)
	{
		try
		{
			string dir = Path.GetDirectoryName(placeFilePath) ?? "";
			string nameNoExt = Path.GetFileNameWithoutExtension(placeFilePath);
			File.WriteAllText(Path.Combine(dir, nameNoExt + CountExtension), count.ToString());
		}
		catch { }
	}

	private static int ReadPlayerCount(string placeFilePath)
	{
		try
		{
			string dir = Path.GetDirectoryName(placeFilePath) ?? "";
			string nameNoExt = Path.GetFileNameWithoutExtension(placeFilePath);
			string path = Path.Combine(dir, nameNoExt + CountExtension);
			if (File.Exists(path) && int.TryParse(File.ReadAllText(path), out int n)) return n;
		}
		catch { }
		return 0;
	}

	private static void ResetPlayerCount(string placeFilePath)
	{
		WritePlayerCount(placeFilePath, 0);
	}

	public static int? ParseChildArg()
	{
		Dictionary<string, string> args = Globals.ReadCmdArgs();
		if (args.TryGetValue("child", out string? v) && int.TryParse(v, out int n)) return n;
		return null;
	}

	public static void Start()
	{
		try
		{
			ScanPlaces();
			if (ParseChildArg() != null) return;
			InitRuntimes();
			_listener = new TcpListener(IPAddress.Any, Port);
			_listener.Start();
			_thread = new Thread(Loop) { IsBackground = true, Name = "PlacesServer" };
			_thread.Start();
			_watchdogThread = new Thread(WatchdogLoop) { IsBackground = true, Name = "PlacesWatchdog" };
			_watchdogThread.Start();
			PT.Print($"server started on {Port} , {_entries.Count} place(s) , idle={IdleTimeout.TotalMinutes:F0}m)");
			Globals.BeforeQuit += KillChildren;
		}
		catch (Exception ex)
		{
			PT.PrintErr("failed to start! ", ex);
		}
	}

	private static void InitRuntimes()
	{
		lock (_runtimesLock)
		{
			_runtimes.Clear();
			foreach (Entry e in _entries)
			{
				_runtimes[e.Id] = new PlaceRuntime();
				try
				{
					string dir = Path.GetDirectoryName(e.FilePath) ?? "";
					string countPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(e.FilePath) + CountExtension);
					if (File.Exists(countPath)) File.Delete(countPath);
				}
				catch { }
			}
		}
	}

	private enum WakeResultKind { Ready, NotFound, Timeout, Error }

	private sealed record WakeResult(WakeResultKind Kind, int Port, string? Detail);

	private static WakeResult EnsureHotBlocking(int placeId)
	{
		Entry? entry;
		PlaceRuntime? runtime;
		int hostPort = FirstHostPort + (placeId - 1);
		lock (_runtimesLock)
		{
			entry = _entries.Find(x => x.Id == placeId);
			if (entry == null || !_runtimes.TryGetValue(placeId, out runtime))
			{
				return new WakeResult(WakeResultKind.NotFound, 0, "not_found");
			}
		}

		DateTime deadline = DateTime.UtcNow + WakeWaitDeadline;
		while (DateTime.UtcNow < deadline)
		{
			PlaceState observedState;
			lock (runtime.Lock)
			{
				observedState = runtime.State;
				if (observedState == PlaceState.Running)
				{
					runtime.LastActiveAt = DateTime.UtcNow;
					return new WakeResult(WakeResultKind.Ready, hostPort, null);
				}
			}

			if (observedState == PlaceState.Cold)
			{
				if (!SpawnPlaceChild(placeId, entry, runtime, hostPort))
				{
					return new WakeResult(WakeResultKind.Error, hostPort, "spawn_failed");
				}
			}

			Thread.Sleep(250);
		}
		return new WakeResult(WakeResultKind.Timeout, hostPort, "timeout");
	}

	private static bool SpawnPlaceChild(int placeId, Entry entry, PlaceRuntime runtime, int hostPort)
	{
		try
		{
			string? exePath = System.Environment.ProcessPath;
			if (string.IsNullOrEmpty(exePath))
			{
				PT.PrintErr("cannot resolve own exe path");
				return false;
			}

			ResetPlayerCount(entry.FilePath);

			ProcessStartInfo psi = new() { FileName = exePath, UseShellExecute = false };
			psi.ArgumentList.Add("--headless");
			psi.ArgumentList.Add("--child");
			psi.ArgumentList.Add(placeId.ToString());
			psi.ArgumentList.Add("--network");
			psi.ArgumentList.Add("server");
			psi.ArgumentList.Add("--noxr");

			Process? p = Process.Start(psi);
			if (p == null) return false;

			lock (runtime.Lock)
			{
				runtime.State = PlaceState.Warming;
				runtime.Process = p;
				runtime.LastActiveAt = DateTime.UtcNow;
			}

			p.EnableRaisingEvents = true;
			p.Exited += (s, e) => OnChildExited(placeId, runtime, entry);

			Task.Run(() => PollUntilReady(placeId, runtime, hostPort));

			PT.Print($"spawned child #{placeId} pid={p.Id} port={hostPort} place={entry.Name}");
			return true;
		}
		catch (Exception ex)
		{
			PT.PrintErr($"failed to spawn child #{placeId}: ", ex);
			return false;
		}
	}

	private static void PollUntilReady(int placeId, PlaceRuntime runtime, int hostPort)
	{
		DateTime deadline = DateTime.UtcNow + WarmupTimeout;
		while (DateTime.UtcNow < deadline)
		{
			lock (runtime.Lock)
			{
				if (runtime.State != PlaceState.Warming) return;
			}

			if (TryProbeUdp(hostPort))
			{
				lock (runtime.Lock)
				{
					if (runtime.State == PlaceState.Warming)
					{
						runtime.State = PlaceState.Running;
						runtime.LastActiveAt = DateTime.UtcNow;
					}
				}
				PT.Print($"child #{placeId} ready on port {hostPort}");
				return;
			}
			Thread.Sleep(250);
		}
		PT.PrintErr($"killing child #{placeId} because not become within {WarmupTimeout.TotalSeconds}!");
		KillChild(placeId, runtime);
	}

	private static bool TryProbeUdp(int port)
	{
		try
		{
			using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, port));
			return false;
		}
		catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
		{
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void OnChildExited(int placeId, PlaceRuntime runtime, Entry entry)
	{
		lock (runtime.Lock)
		{
			runtime.State = PlaceState.Cold;
			runtime.Process = null;
		}
		try
		{
			string dir = Path.GetDirectoryName(entry.FilePath) ?? "";
			string countPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(entry.FilePath) + CountExtension);
			if (File.Exists(countPath)) File.Delete(countPath);
		}
		catch { }
		PT.Print($"child #{placeId} exited");
	}

	private static void KillChild(int placeId, PlaceRuntime runtime)
	{
		Process? p;
		lock (runtime.Lock)
		{
			p = runtime.Process;
			if (runtime.State == PlaceState.Cold) return;
			runtime.State = PlaceState.Stopping;
		}
		if (p != null)
		{
			try { p.Kill(true); } catch { }
		}
		lock (runtime.Lock)
		{
			runtime.State = PlaceState.Cold;
			runtime.Process = null;
		}
	}

	private static void WatchdogLoop()
	{
		while (_listener != null)
		{
			try
			{
				Thread.Sleep(WatchdogInterval);
				List<(int id, PlaceRuntime runtime, Entry entry)> snapshot = new();
				lock (_runtimesLock)
				{
					foreach (KeyValuePair<int, PlaceRuntime> kv in _runtimes)
					{
						Entry? e = _entries.Find(x => x.Id == kv.Key);
						if (e != null) snapshot.Add((kv.Key, kv.Value, e));
					}
				}
				foreach ((int id, PlaceRuntime runtime, Entry entry) in snapshot)
				{
					UpdateRuntimeIdle(id, runtime, entry);
				}
			}
			catch (Exception ex)
			{
				PT.PrintErr("watchdog: ", ex);
			}
		}
	}

	private static void UpdateRuntimeIdle(int placeId, PlaceRuntime runtime, Entry entry)
	{
		bool isRunning;
		lock (runtime.Lock) isRunning = runtime.State == PlaceState.Running;
		if (!isRunning) return;

		int count = ReadPlayerCount(entry.FilePath);
		DateTime now = DateTime.UtcNow;
		bool shouldKill = false;
		lock (runtime.Lock)
		{
			if (count > 0) runtime.LastActiveAt = now;
			else if (now - runtime.LastActiveAt > IdleTimeout) shouldKill = true;
		}
		if (shouldKill)
		{
			PT.Print($"idle-killing child #{placeId} after {IdleTimeout.TotalMinutes:F0}m with 0 players");
			KillChild(placeId, runtime);
		}
	}

	private static void KillChildren()
	{
		lock (_runtimesLock)
		{
			foreach (KeyValuePair<int, PlaceRuntime> kv in _runtimes)
			{
				Process? p;
				lock (kv.Value.Lock)
				{
					p = kv.Value.Process;
					kv.Value.State = PlaceState.Cold;
					kv.Value.Process = null;
				}
				if (p != null)
				{
					try { p.Kill(true); } catch { }
				}
			}
		}
	}

	private static void ScanPlaces()
	{
		_entries.Clear();
		string dir = ResolvePlacesDir();
		if (!Directory.Exists(dir))
		{
			PT.Print($"places dir not found! creating {dir}");
			Directory.CreateDirectory(dir);
			return;
		}
		string[] files = Directory.GetFiles(dir, "*.poly");
		Array.Sort(files, StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < files.Length; i++)
		{
			string fileNameNoExt = Path.GetFileNameWithoutExtension(files[i]);
			PlaceMeta meta = LoadMeta(Path.Combine(dir, fileNameNoExt + MetaExtension));
			string displayName = string.IsNullOrEmpty(meta.Name) ? fileNameNoExt : meta.Name;
			_entries.Add(new Entry(i + 1, displayName, files[i], meta));
			PT.Print($"  #{i + 1}: {displayName}  ({files[i]})");
		}
	}

	private static string ResolvePlacesDir()
	{
		string? exeDir = Path.GetDirectoryName(System.Environment.ProcessPath);
		if (!string.IsNullOrEmpty(exeDir))
		{
			string candidate = Path.Combine(exeDir, PlacesSubdir);
			if (Directory.Exists(candidate)) return candidate;
		}
		string baseCandidate = Path.Combine(AppContext.BaseDirectory, PlacesSubdir);
		if (Directory.Exists(baseCandidate)) return baseCandidate;
		return !string.IsNullOrEmpty(exeDir) ? Path.Combine(exeDir, PlacesSubdir) : baseCandidate;
	}

	private static PlaceMeta LoadMeta(string path)
	{
		if (!File.Exists(path)) return new PlaceMeta();
		try
		{
			string text = File.ReadAllText(path);
			PlaceMeta? meta = JsonSerializer.Deserialize(text, PlaceMetaJsonContext.Default.PlaceMeta);
			return meta ?? new PlaceMeta();
		}
		catch (Exception ex)
		{
			PT.PrintWarn($"failed to parse meta '{path}'! {ex.Message}");
			return new PlaceMeta();
		}
	}

	private static void Loop()
	{
		while (_listener != null)
		{
			TcpClient client;
			try { client = _listener.AcceptTcpClient(); }
			catch { return; }
			ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
		}
	}

	private static void HandleClient(TcpClient client)
	{
		try
		{
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 5000;
			stream.WriteTimeout = (int)(WakeWaitDeadline.TotalMilliseconds + 10_000);

			string? requestLine = ReadLine(stream);
			if (requestLine == null) return;
			string[] parts = requestLine.Split(' ');
			if (parts.Length < 2) return;
			string method = parts[0];
			string target = parts[1];

			while (true)
			{
				string? hdr = ReadLine(stream);
				if (string.IsNullOrEmpty(hdr)) break;
			}

			int q = target.IndexOf('?');
			string path = q >= 0 ? target[..q] : target;
			while (path.Contains("//")) path = path.Replace("//", "/");

			Route(stream, method, path);
		}
		catch (IOException) { /* probe / dropped connection */ }
		catch (Exception ex)
		{
			PT.PrintErr("handler error! ", ex);
		}
		finally
		{
			try { client.Close(); } catch { }
		}
	}

	private static string? ReadLine(NetworkStream stream)
	{
		StringBuilder sb = new();
		int prev = -1;
		while (true)
		{
			int b = stream.ReadByte();
			if (b == -1) return sb.Length == 0 ? null : sb.ToString();
			if (prev == '\r' && b == '\n')
			{
				return sb.ToString(0, sb.Length - 1);
			}
			sb.Append((char)b);
			prev = b;
			if (sb.Length > 8192) return sb.ToString();
		}
	}

	private static void Route(NetworkStream stream, string method, string path)
	{
		try
		{
			if (path == "/api/places")
			{
				WriteWorldsRoot(stream);
			}
			else if (path.StartsWith("/api/places/") && path.EndsWith("/wake"))
			{
				string mid = path["/api/places/".Length..^"/wake".Length];
				if (!int.TryParse(mid, out int id))
				{
					WriteWakeResponse(stream, 0, new WakeResult(WakeResultKind.NotFound, 0, "bad_id"));
					return;
				}
				WakeResult res = EnsureHotBlocking(id);
				WriteWakeResponse(stream, id, res);
			}
			else if (path.StartsWith("/v1/places/") && path.EndsWith("/media"))
			{
				WriteJson(stream, "[]");
			}
			else if (path.StartsWith("/v1/places/"))
			{
				int id = ParseTrailingId(path, "/v1/places/");
				WritePlaceInfo(stream, id);
			}
			else if (path.StartsWith("/v1/assets/serve/") && path.EndsWith("/placeIcon"))
			{
				int id = ParseSegmentId(path, "/v1/assets/serve/");
				Entry? e = _entries.Find(x => x.Id == id);
				string url = ExternalUrl(e?.Meta.IconUrl) ?? (e != null ? IconEndpoint(e) : $"https://screen.torpos.org/icons/{id}.png");
				WriteJson(stream, $"{{\"url\":\"{JsonEscape(url)}\"}}");
			}
			else if (path.StartsWith("/icons/") && path.EndsWith(".png"))
			{
				int id = ParseTrailingId(path[..^4], "/icons/");
				WritePlaceIcon(stream, id);
			}
			else if (path.StartsWith("/v1/assets/serve/") &&
				(path.EndsWith("/userAvatarHeadshot") || path.EndsWith("/userAvatar")))
			{
				int userId = ParseSegmentId(path, "/v1/assets/serve/");
				bool wantIcon = path.EndsWith("/userAvatarHeadshot");
				string? cdnUrl = TryGetUserAvatarUrl(userId, wantIcon);
				string url = cdnUrl ?? $"https://screen.torpos.org/avatars/{userId}.png";
				WriteJson(stream, $"{{\"url\":\"{url}\"}}");
			}
			else if (path.StartsWith("/avatars/") && path.EndsWith(".png"))
			{
				int id = ParseTrailingId(path[..^4], "/avatars/");
				WritePng(stream, id);
			}
			else if (path.StartsWith("/v1/"))
			{
				ProxyJsonUpstream(stream, "https://api.polytoria.com" + path);
			}
			else if (path == "/api/users/me")
			{
				WriteJson(stream, "{\"id\":0,\"username\":\"Player\",\"email\":\"\",\"createdAt\":\"2020-01-01T00:00:00Z\",\"membershipType\":\"free\",\"isStaff\":false}");
			}
			else if (path == "/api/feed")
			{
				WriteJson(stream, "{\"meta\":{\"total\":0,\"perPage\":0,\"currentPage\":1,\"lastPage\":1,\"firstPage\":1},\"data\":[]}");
			}
			else if (path.StartsWith("/api/"))
			{
				WriteJson(stream, "{}");
			}
			else
			{
				WriteStatus(stream, 404, "Not Found", null, null);
			}
		}
		catch (Exception ex)
		{
			PT.PrintErr($"route error on {path}: ", ex);
			try { WriteStatus(stream, 500, "Internal Server Error", null, null); } catch { }
		}
	}

	private static void WriteWakeResponse(NetworkStream stream, int id, WakeResult res)
	{
		if (res.Kind == WakeResultKind.Ready)
		{
			WriteJson(stream, $"{{\"id\":{id},\"port\":{res.Port},\"ready\":true}}");
			return;
		}
		int code = res.Kind switch
		{
			WakeResultKind.NotFound => 404,
			WakeResultKind.Timeout => 504,
			_ => 500,
		};
		string detail = JsonEscape(res.Detail ?? res.Kind.ToString().ToLowerInvariant());
		string json = $"{{\"id\":{id},\"port\":{res.Port},\"ready\":false,\"error\":\"{detail}\"}}";
		byte[] body = Encoding.UTF8.GetBytes(json);
		WriteStatus(stream, code, res.Kind.ToString(), "application/json; charset=utf-8", body);
	}

	private static string JsonEscape(string s)
	{
		StringBuilder sb = new(s.Length);
		foreach (char c in s)
		{
			switch (c)
			{
				case '\\': sb.Append("\\\\"); break;
				case '"': sb.Append("\\\""); break;
				case '\n': sb.Append("\\n"); break;
				case '\r': sb.Append("\\r"); break;
				case '\t': sb.Append("\\t"); break;
				default: sb.Append(c); break;
			}
		}
		return sb.ToString();
	}

	private static int ParseTrailingId(string path, string prefix)
	{
		string rest = path[prefix.Length..];
		int.TryParse(rest, out int v);
		return v;
	}

	private static int ParseSegmentId(string path, string prefix)
	{
		string rest = path[prefix.Length..];
		int slash = rest.IndexOf('/');
		if (slash >= 0) rest = rest[..slash];
		int.TryParse(rest, out int v);
		return v;
	}

	private static void WriteWorldsRoot(NetworkStream stream)
	{
		APIWorldsData[] data = new APIWorldsData[_entries.Count];
		for (int i = 0; i < _entries.Count; i++)
		{
			Entry e = _entries[i];
			data[i] = new APIWorldsData
			{
				Id = e.Id,
				CreatorType = e.Meta.CreatorType ?? "user",
				CreatorID = e.Meta.CreatorID ?? 1,
				Name = e.Name,
				Description = e.Meta.Description ?? "",
				Genre = e.Meta.Genre ?? "Adventure",
				PlaceType = e.Meta.PlaceType ?? "place",
				GenreIcon = e.Meta.GenreIcon ?? "",
				CreatorName = e.Meta.CreatorName ?? "Local",
				CreatorThumbnail = e.Meta.CreatorThumbnail ?? "",
				Visits = 0,
				Playing = ReadPlayerCount(e.FilePath),
				Rating = null,
				IconUrl = ExternalUrl(e.Meta.IconUrl) ?? IconEndpoint(e),
			};
		}
		APIWorldsRoot root = new()
		{
			Meta = new APIMeta { Total = data.Length, PerPage = data.Length, CurrentPage = 1, LastPage = 1, FirstPage = 1 },
			Data = data,
		};
		string json = JsonSerializer.Serialize(root, APIGenerationContext.Default.APIWorldsRoot);
		WriteJson(stream, json);
	}

	private static void WritePlaceInfo(NetworkStream stream, int id)
	{
		Entry? e = _entries.Find(x => x.Id == id);
		if (e == null)
		{
			WriteStatus(stream, 404, "Not Found", null, null);
			return;
		}
		APIPlaceInfo info = new()
		{
			Id = e.Id,
			Name = e.Name,
			Description = e.Meta.Description ?? "",
			Creator = new APIPlaceCreator
			{
				Type = e.Meta.CreatorType ?? "user",
				Id = e.Meta.CreatorID ?? 1,
				Name = e.Meta.CreatorName ?? "Local",
				Thumbnail = e.Meta.CreatorThumbnail ?? "",
			},
			Thumbnail = ExternalUrl(e.Meta.ThumbnailUrl) ?? ExternalUrl(e.Meta.IconUrl) ?? IconEndpoint(e),
			Genre = e.Meta.Genre ?? "Adventure",
			MaxPlayers = 8,
			IsActive = true,
			IsToolsEnabled = false,
			IsCopyable = false,
			Visits = 0,
			UniqueVisits = 0,
			Playing = ReadPlayerCount(e.FilePath),
			Rating = new APIPlaceRating { Likes = 0, Dislikes = 0, Percent = "100" },
			AccessType = "public",
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
		};
		string json = JsonSerializer.Serialize(info, APIGenerationContext.Default.APIPlaceInfo);
		WriteJson(stream, json);
	}

	private static void WriteJson(NetworkStream stream, string json)
	{
		byte[] body = Encoding.UTF8.GetBytes(json);
		WriteStatus(stream, 200, "OK", "application/json; charset=utf-8", body);
	}

	private static void WritePng(NetworkStream stream, int id)
	{
		byte[] png = BuildSolidPng(id);
		WriteStatus(stream, 200, "OK", "image/png", png);
	}

	private static readonly string[] ThumbnailExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

	private static void WritePlaceIcon(NetworkStream stream, int id)
	{
		Entry? e = _entries.Find(x => x.Id == id);
		if (e != null)
		{
			string? img = LocalThumbnail(e);
			if (img != null)
			{
				try
				{
					byte[] bytes = File.ReadAllBytes(img);
					WriteStatus(stream, 200, "OK", ImageContentType(img), bytes);
					return;
				}
				catch (Exception ex) { PT.PrintErr($"failed to read thumbnail {img}: ", ex); }
			}
		}
		WritePng(stream, id);
	}

	private static string IconEndpoint(Entry e)
	{
		string url = $"https://screen.torpos.org/icons/{e.Id}.png";
		string? local = LocalThumbnail(e);
		if (local != null)
		{
			try { return $"{url}?v={new DateTimeOffset(File.GetLastWriteTimeUtc(local)).ToUnixTimeSeconds()}"; }
			catch { }
		}
		return url;
	}

	private static string? LocalThumbnail(Entry e)
	{
		string dir = Path.GetDirectoryName(e.FilePath) ?? ResolvePlacesDir();

		string? metaRef = LocalRef(e.Meta.ThumbnailUrl) ?? LocalRef(e.Meta.IconUrl);
		if (metaRef != null)
		{
			string named = Path.Combine(dir, Path.GetFileName(metaRef));
			if (File.Exists(named)) return named;
		}

		string baseNoExt = Path.Combine(dir, Path.GetFileNameWithoutExtension(e.FilePath));
		foreach (string ext in ThumbnailExtensions)
		{
			string candidate = baseNoExt + ext;
			if (File.Exists(candidate)) return candidate;
		}
		return null;
	}

	private static string? LocalRef(string? r)
	{
		if (string.IsNullOrEmpty(r)) return null;
		if (r.StartsWith("http://") || r.StartsWith("https://")) return null;
		return r;
	}

	private static string? ExternalUrl(string? r)
	{
		if (string.IsNullOrEmpty(r)) return null;
		return (r.StartsWith("http://") || r.StartsWith("https://")) ? r : null;
	}

	private static string ImageContentType(string path)
	{
		string ext = Path.GetExtension(path).ToLowerInvariant();
		return ext switch
		{
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			_ => "image/png",
		};
	}

	private static void WriteStatus(NetworkStream stream, int code, string reason, string? contentType, byte[]? body)
	{
		StringBuilder hdr = new();
		hdr.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
		hdr.Append("Connection: close\r\n");
		if (contentType != null) hdr.Append("Content-Type: ").Append(contentType).Append("\r\n");
		hdr.Append("Content-Length: ").Append(body?.Length ?? 0).Append("\r\n");
		hdr.Append("\r\n");
		byte[] hdrBytes = Encoding.ASCII.GetBytes(hdr.ToString());
		stream.Write(hdrBytes, 0, hdrBytes.Length);
		if (body != null) stream.Write(body, 0, body.Length);
		stream.Flush();
	}

	private static readonly System.Net.Http.HttpClient _upstreamHttp = CreateUpstreamClient();
	private static readonly ConcurrentDictionary<(int, bool), string?> _avatarUrlCache = new();

	private static System.Net.Http.HttpClient CreateUpstreamClient()
	{
		System.Net.Http.HttpClient c = new() { Timeout = TimeSpan.FromSeconds(5) };
		c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Polytoria Client {Globals.AppVersion}");
		c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
		return c;
	}

	private static void ProxyJsonUpstream(NetworkStream stream, string upstreamUrl)
	{
		try
		{
			System.Net.Http.HttpResponseMessage resp = _upstreamHttp.GetAsync(upstreamUrl).GetAwaiter().GetResult();
			byte[] body = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
			string ct = resp.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
			WriteStatus(stream, (int)resp.StatusCode, resp.ReasonPhrase ?? "OK", ct, body);
		}
		catch (Exception ex)
		{
			PT.Print($"proxy {upstreamUrl} failed! {ex.Message}");
			WriteStatus(stream, 502, "Bad Gateway", null, null);
		}
	}

	private static string? TryGetUserAvatarUrl(int userId, bool wantIcon)
	{
		if (_avatarUrlCache.TryGetValue((userId, wantIcon), out string? cached)) return cached;
		string? result = null;
		try
		{
			string resp = _upstreamHttp.GetStringAsync($"https://api.polytoria.com/v1/users/{userId}").GetAwaiter().GetResult();
			using JsonDocument doc = JsonDocument.Parse(resp);
			if (doc.RootElement.TryGetProperty("thumbnail", out JsonElement t))
			{
				string field = wantIcon ? "icon" : "avatar";
				if (t.TryGetProperty(field, out JsonElement v) && v.ValueKind == JsonValueKind.String)
				{
					string raw = v.GetString() ?? "";
					if (!string.IsNullOrWhiteSpace(raw)) result = raw;
				}
			}
		}
		catch (Exception ex)
		{
			PT.Print($"upstream avatar lookup failed for {userId}! {ex.Message}");
		}
		_avatarUrlCache[(userId, wantIcon)] = result;
		return result;
	}

	private static readonly Dictionary<int, byte[]> _pngCache = new();

	private static byte[] BuildSolidPng(int id)
	{
		if (_pngCache.TryGetValue(id, out byte[]? cached)) return cached;
		uint h = (uint)(id * 2654435761U);
		int seed = (int)(h ^ (h >> 16));
		Random rng = new(seed);
		Color c = Color.FromHsv((float)rng.NextDouble(), 0.55f, 0.85f);
		Image img = Image.CreateEmpty(256, 256, false, Image.Format.Rgba8);
		img.Fill(c);
		byte[] png = img.SavePngToBuffer();
		_pngCache[id] = png;
		return png;
	}
}
