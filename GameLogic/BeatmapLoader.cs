using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shaffuru.GameLogic {
	class BeatmapLoader {
		readonly GameplayCoreSceneSetupData _sceneSetupData;
		static BeatmapLevelsModel beatmapLevelsModel;

		static BeatmapLevelLoader beatmapLevelLoader;
		static BeatmapDataLoader beatmapDataLoader;
		static BeatmapLevelsEntitlementModel beatmapLevelsEntitlementModel;
		static AudioClipAsyncLoader audioClipAsyncLoader;
		static SettingsManager settingsManager;
		static bool reloadLevelpacksOnExit = false;

		public BeatmapLoader(
			GameplayCoreSceneSetupData _sceneSetupData,
			BeatmapLevelsModel beatmapLevelsModel,

			BeatmapLevelLoader beatmapLevelLoader,
			BeatmapDataLoader beatmapDataLoader,
			BeatmapLevelsEntitlementModel beatmapLevelsEntitlementModel,
			AudioClipAsyncLoader audioClipAsyncLoader,
			SettingsManager settingsManager
		) {
			this._sceneSetupData = _sceneSetupData;
			BeatmapLoader.beatmapLevelsModel = beatmapLevelsModel;

			BeatmapLoader.beatmapLevelLoader = beatmapLevelLoader;
			BeatmapLoader.beatmapDataLoader = beatmapDataLoader;
			BeatmapLoader.beatmapLevelsEntitlementModel = beatmapLevelsEntitlementModel;
			BeatmapLoader.audioClipAsyncLoader = audioClipAsyncLoader;
			BeatmapLoader.settingsManager = settingsManager;
		}

		public static void AddBeatmapToLoadedPreviewBeatmaps(string levelId, BeatmapLevel level) {
			beatmapLevelsModel._allLoadedBeatmapLevelsRepository.AddBeatmapLevel(level, SongCore.Loader.CustomLevelsPack.packID);

			reloadLevelpacksOnExit = true;
		}

		public static BeatmapLevel GetPreviewBeatmapFromLevelId(string levelId) {
			if(beatmapLevelsModel._allLoadedBeatmapLevelsRepository.TryGetBeatmapLevelById(levelId, out var map))
				return map;

			return null;
		}

		public static bool RefreshLevelPacksIfNecessary() {
			if(!reloadLevelpacksOnExit)
				return false;

			reloadLevelpacksOnExit = false;

			SongCore.Loader.Instance.RefreshSongs(false);

			return true;
		}




		public Task<BeatmapLevel> LoadBeatmapLevel(string levelId) => Task.FromResult(GetPreviewBeatmapFromLevelId(levelId));

		// Loads and transforms a difficulty the same way the game does when starting a level, so that
		// left-handed mode, environment filtering and wall merging apply to every queued map.
		public async Task<(IReadonlyBeatmapData beatmapData, UnityEngine.AudioClip audioClip)> LoadAndTransformBeatmapData(BeatmapLevel level, BeatmapKey key) {
			var loadResult = await beatmapLevelLoader.LoadBeatmapLevelDataAsync(level, BeatmapLevelDataVersion.Original, CancellationToken.None);
			var beatmapLevelData = loadResult.beatmapLevelData;

			if(beatmapLevelData == null) {
				Plugin.Log.Error($"Could not load beatmap data for {level.levelID}");
				return (null, null);
			}

			var transformedBeatmapData = await beatmapDataLoader.CreateOrGetTransformedBeatmapDataAsync(
				key,
				level,
				_sceneSetupData.gameplayModifiers,
				_sceneSetupData.playerSpecificSettings,
				_sceneSetupData.targetEnvironmentInfo,
				_sceneSetupData.originalEnvironmentInfo,
				false,
				settingsManager.settings.quality.screenDisplacementEffects,
				beatmapLevelData,
				BeatmapLevelDataVersion.Original
			);

			var audioClip = await audioClipAsyncLoader.LoadSong(beatmapLevelData);

			return (transformedBeatmapData, audioClip);
		}

		public static BeatmapBasicData GetDifficultyBeatmapData(BeatmapLevel level, BeatmapKey key) =>
			level.GetDifficultyBeatmapData(key);
	}
}
