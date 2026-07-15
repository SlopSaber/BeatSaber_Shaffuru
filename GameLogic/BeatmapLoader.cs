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




		/*
		 * NOTE (2024 rewrite): Prior to 1.35ish this used beatmapLevelsModel.GetBeatmapLevelAsync(levelId, ...).
		 * IPreviewBeatmapLevel/IBeatmapLevel/IDifficultyBeatmap were removed from the game entirely in favor of the
		 * plain BeatmapLevel + BeatmapKey model. Since every level Shaffuru deals with is already known to SongCore
		 * (it's either a locally installed custom level, or one we just added to the repository ourselves via
		 * AddBeatmapToLoadedPreviewBeatmaps above), we can look it up directly instead of going through
		 * BeatmapLevelLoader's network-capable async path, which is meant for levels the game hasn't indexed yet.
		 *
		 * This mirrors the pattern used by BetterSongSearch (SongCore.Loader.BeatmapLevelsModelSO.GetBeatmapLevel).
		 */
		public Task<BeatmapLevel> LoadBeatmapLevel(string levelId) {
			var level = SongCore.Loader.BeatmapLevelsModelSO.GetBeatmapLevel(levelId);

			return Task.FromResult(level);
		}

		/*
		 * Loads the raw (untransformed) IReadonlyBeatmapData for a specific key of an already-resolved BeatmapLevel,
		 * then runs it through the same transform base game gameplay would apply (LeftHanded, environment effects
		 * filtering, wall merging, etc) via CreateTransformedBeatmapData - equivalent to what
		 * IDifficultyBeatmap.GetBeatmapDataAsync() used to do pre-1.35.
		 *
		 * LoadBeatmapDataAsync's signature below is confirmed against the real 1.39.1 assemblies.
		 */
		public async Task<(IReadonlyBeatmapData beatmapData, UnityEngine.AudioClip audioClip)> LoadAndTransformBeatmapData(BeatmapLevel level, BeatmapKey key) {
			var beatmapLevelData = (await beatmapLevelLoader.LoadBeatmapLevelDataAsync(
				level,
				BeatmapLevelDataVersion.Original,
				CancellationToken.None
			)).beatmapLevelData;

			var playerSpecificSettings = _sceneSetupData.playerSpecificSettings;
			var gameplayModifiers = _sceneSetupData.gameplayModifiers;
			var targetEnvironmentInfo = _sceneSetupData.targetEnvironmentInfo;

			var rawBeatmapData = await beatmapDataLoader.LoadBeatmapDataAsync(
				beatmapLevelData,
				key,
				level.beatsPerMinute,
				false,
				targetEnvironmentInfo,
				targetEnvironmentInfo,
				BeatmapLevelDataVersion.Original,
				gameplayModifiers,
				playerSpecificSettings,
				true
			);

			/*
			 * VERIFY: IBeatmapLevelData has no direct audio clip accessor - AudioClipAsyncLoader is the real way to
			 * load one, but the overloads it exposes want an IFilePathSongAudioClipProvider/IAssetSongAudioClipProvider
			 * (or a raw file path / AudioClip / audio-clip-shaped wrapper), not IBeatmapLevelData itself. Since this is
			 * a locally-installed custom level, going through the actual audio file on disk via SongCore's known
			 * layout is the most solid path available without more concrete confirmation of what field on
			 * StandardLevelInfoSaveData holds the audio filename - check this if it throws at runtime.
			 */
			// GetCustomLevelPath/GetStandardLevelInfoSaveData are obsolete in favor of GetLoadedSaveData - confirmed
			// field names: customLevelFolderInfo.folderPath and standardLevelInfoSaveData.songFilename.
			var loadedSaveData = SongCore.Collections.GetLoadedSaveData(level.levelID).Value;
			var audioPath = System.IO.Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, loadedSaveData.standardLevelInfoSaveData.songFilename);

			var audioClip = await audioClipAsyncLoader.Load(audioPath);

			// Process the new beatmap as tho we'd play it so LeftHanded etc is accounted for
			var environmentEffectsFilterPreset = (key.difficulty == BeatmapDifficulty.ExpertPlus) ? playerSpecificSettings.environmentEffectsFilterExpertPlusPreset : playerSpecificSettings.environmentEffectsFilterDefaultPreset;

			var transformedBeatmapData = BeatmapDataTransformHelper.CreateTransformedBeatmapData(
				rawBeatmapData,
				level,
				gameplayModifiers,
				playerSpecificSettings.leftHanded,
				environmentEffectsFilterPreset,
				_sceneSetupData.targetEnvironmentInfo.environmentIntensityReductionOptions,
				// This is (currently) only used to decide if to merge walls or not...
				settingsManager.settings
			);

			return (transformedBeatmapData, audioClip);
		}

		/*
		 * VERIFY: Best-effort guess for per-difficulty metadata (NJS / offset). Post-1.35 this data type is called
		 * BeatmapBasicData (see Aeroluna/CustomJSONData docs) - the accessor name/shape below is not confirmed.
		 */
		public static BeatmapBasicData GetDifficultyBeatmapData(BeatmapLevel level, BeatmapKey key) =>
			level.GetDifficultyBeatmapData(key.beatmapCharacteristic, key.difficulty);
	}
}
