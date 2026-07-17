using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using Shaffuru.GameLogic;
using Shaffuru.Util;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine.Networking;

namespace Shaffuru.AppLogic {
	struct SongDownloaderJob : IJob {
		public static readonly HashSet<uint> downloadingMaps = new HashSet<uint>();
		public static readonly string ShaffuruDownloadPath = Path.Combine(Directory.GetCurrentDirectory(), CustomLevelPathHelper.customLevelsDirectoryPath, "SHAFFURU_DOWNLOADS");

		uint beatsaverId;

		public SongDownloaderJob(uint beatsaverId) {
			this.beatsaverId = beatsaverId;

			downloadingMaps.Add(beatsaverId);
		}

		static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

		static readonly string UserAgent = "Shaffuru/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
		public unsafe void Execute() {
			var mapId = beatsaverId;

			try {
				if(!SongDetailsUtil.instance.songs.FindByMapId(beatsaverId, out var song))
					return;

				var downloadUrl = $"https://r2cdn.beatsaver.com/{song.hash.ToLowerInvariant()}.zip";
				var folderName = string.Concat($"{song.key} ({song.songName} - {song.levelAuthorName})".Split(InvalidPathChars)).Trim();

				using(var m = DownloadMap(downloadUrl)) {
					if(m == null)
						return;

					var p = SaveMap(m, folderName);

					if(p == null)
						return;

					/*
					 * SongCore.Loader.LoadCustomLevel almost certainly touches Unity engine objects (decoding the
					 * audio file, generating a cover sprite, etc), which is not safe to do from this Job's
					 * background thread - has to be marshalled onto the main thread. This previously silently
					 * failed (or threw somewhere Unity's Job System just swallowed) with zero logging, which is why
					 * downloaded songs never made it into the queue.
					 *
					 * IMPORTANT: RequestManager.cs's caller only waits for this Job's Execute() to return (via
					 * dl.Complete()) before checking whether the map is requestable yet - so this can NOT be
					 * fire-and-forget. We block this background thread (which is fine, unlike blocking the main
					 * thread) until the main-thread work has actually finished.
					 */
					var mainThreadTask = IPA.Utilities.Async.UnityMainThreadTaskScheduler.Factory.StartNew(() => {
						// Same idea as RamCleaner pausing playback around its GC.Collect() burst - loading a freshly
						// downloaded map on the main thread is real work (audio decode, sprite generation, etc) and
						// doing it while a song is actively playing would just show up as a stutter/hitch. Pausing
						// around it (if we're even in gameplay at all right now) makes it a clean, deliberate pause
						// instead, same as the RAM clearing pass already does.
						var pausedFor = AudioTimeWrapper.current;
						var didPause = pausedFor?.isValid == true;

						if(didPause)
							pausedFor.Pause();

						try {
							// VERIFY: SongCore.Loader.LoadCustomLevel's second parameter is SongCore.Data.SongFolderEntry -
							// I don't have its shape confirmed, `default` is a guess (fine if it's a reference type and
							// null is an acceptable "just use defaults" value, wrong if it's a struct requiring real fields).
							var result = SongCore.Loader.LoadCustomLevel(p, default);

							if(result == null) {
								Plugin.Log.Error($"Downloaded {mapId} to {p} but SongCore.Loader.LoadCustomLevel failed to load it (returned null)");
								return;
							}

							var (levelId, preview) = result.Value;

							BeatmapLoader.AddBeatmapToLoadedPreviewBeatmaps(levelId, preview);

							MapPool.instance.AddRequestableLevel(preview);
						} catch(Exception ex) {
							Plugin.Log.Error($"Failed to load downloaded map {mapId} from {p} into the pool: {ex}");
						} finally {
							if(didPause)
								pausedFor.Resume();
						}
					});

					mainThreadTask.GetAwaiter().GetResult();
				}
			} catch(Exception ex) {
				Plugin.Log.Error($"Failed to download/save requested map {beatsaverId}: {ex}");
			} finally {
				downloadingMaps.Remove(beatsaverId);
				RamCleaner.AddWeight(1);
			}
		}

		DownloadHandlerBuffer DownloadMap(string downloadUrl) {
			using(var www = UnityWebRequest.Get(downloadUrl)) {
				var b = new DownloadHandlerBuffer();

				www.SetRequestHeader("User-Agent", UserAgent);
				www.timeout = 20;
				www.downloadHandler = b;
				www.disposeDownloadHandlerOnDispose = false;

				www.SendWebRequest();

				while(!www.isDone)
					Thread.Sleep(50);

				if(www.isHttpError || www.isNetworkError)
					return null;

				return b;
			}
		}

		unsafe string SaveMap(DownloadHandlerBuffer dlh, string folderName) {
			var files = new Dictionary<string, (NativeArray<byte> ptr, UnmanagedMemoryStream stream)>(4);

			try {
				var longestFileNameLength = 0;

				using(var zipStream = new MemoryStream(dlh.data, false)) {
					// BetterSongSearch 🙏
					using(var archive = new ZipArchive(zipStream, ZipArchiveMode.Read)) {
						foreach(var entry in archive.Entries) {
							var len = (int)entry.Length;

							// If a file, supposedly, is bigger than that we can assume its malicious
							if(len > 200_000_000)
								throw new InvalidDataException();

							// Dont extract directories / sub-files
							if(entry.FullName.IndexOf('/') != -1)
								continue;
								
							using(var str = entry.Open()) {
								var file = new NativeArray<byte>(len, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
								var x = new UnmanagedMemoryStream((byte*)NativeArrayUnsafeUtility.GetUnsafePtr(file), len, len, FileAccess.ReadWrite);

								files.Add(entry.Name, (file, x));

								str.CopyTo(x);

								x.Position = 0;

								if(entry.Name.Length > longestFileNameLength)
									longestFileNameLength = entry.Name.Length;
							}
						}
					}
				}

				// Failsafe so we dont break songcore. Info.dat, a diff and the song itself - not sure if the cover is needed
				if(files.Count < 3 || !files.Keys.Any(x => x.Equals("info.dat", StringComparison.OrdinalIgnoreCase)))
					throw new InvalidDataException();

				var path = Path.Combine(ShaffuruDownloadPath, folderName);

				if(path.Length > 253 - longestFileNameLength)
					path = $"{path.Substring(0, 253 - longestFileNameLength - 7)}..";

				if(Directory.Exists(path)) {
					var pathNum = 1;
					while(Directory.Exists(path + $" ({pathNum})"))
						pathNum++;

					path += $" ({pathNum})";
				}

				if(!Directory.Exists(path))
					Directory.CreateDirectory(path);

				foreach(var e in files) {
					var entryPath = Path.Combine(path, e.Key);
					using(var s = File.OpenWrite(entryPath))
						e.Value.stream.CopyTo(s);
				}

				return path;
			} finally {
				foreach(var item in files)
					item.Value.ptr.Dispose();
			}
		}
	}
}
