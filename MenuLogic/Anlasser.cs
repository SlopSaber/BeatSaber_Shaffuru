using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeatmapEditor3D;
using HarmonyLib;
using Shaffuru.GameLogic;
using SiraUtil.Zenject;
using UnityEngine;
using Zenject;

namespace Shaffuru.MenuLogic {
	/*
	 * VERIFIED (2024/1.39.1 rewrite) against the real assemblies for this exact game version - all the types/ctors
	 * used below (BeatmapLevel, BeatmapKey, BeatmapBasicData, BeatmapEditor3D.BeatmapLevelData,
	 * MenuTransitionsHelper.StartStandardLevel's IBeatmapLevelData overload) were dumped from Main.dll/DataModels.dll
	 * /BeatSaber.BeatmapEditor.dll directly. BeatmapLevelSO/BeatmapDataSO from the old code no longer exist for this
	 * purpose at all - BeatmapLevelSO is now purely menu-preview metadata with no way to attach real difficulty data,
	 * so the "fake playable level" now has to be a real BeatmapLevel + a real BeatmapEditor3D.BeatmapLevelData
	 * (which happily accepts raw json strings, same trick as the old SetJsonData did) instead.
	 */
	class Anlasser {
		public const string LevelIdPrefix = "___Shaffuru_";

		readonly PlayerDataModel playerDataModel;
		readonly MenuTransitionsHelper menuTransitionsHelper;
		readonly EnvironmentsListModel environmentsListModel;

		static int lastShaffuruMapLength = 0;

		public static BeatmapLevel beatmapLevel { get; private set; }
		public static BeatmapKey beatmapKey { get; private set; }
		static BeatmapEditor3D.BeatmapLevelData beatmapLevelData;
		static AudioClip fakeAudioClip;

		static UBinder<Plugin, System.Random> rngSource;

		static EnvironmentName defaultEnvironment;

		public Anlasser(
			PlayerDataModel playerDataModel,
			CustomLevelLoader customLevelLoader,
			[InjectOptional] MenuTransitionsHelper menuTransitionsHelper,
			[InjectOptional] EnvironmentsListModel environmentsListModel,
			UBinder<Plugin, System.Random> rng
		) {
			this.playerDataModel = playerDataModel;
			this.menuTransitionsHelper = menuTransitionsHelper;
			this.environmentsListModel = environmentsListModel;

			rngSource = rng;

			defaultEnvironment = customLevelLoader.CreateEnvironmentName("", customLevelLoader._defaultEnvironmentInfo);
		}

		public event Action<LevelCompletionResults> finishedOrFailedCallback;

		// Bare-bones IPreviewMediaData for our fake level - we don't have (or need) a real preview clip
		class FakePreviewMediaData : IPreviewMediaData {
			public Task<Sprite> GetCoverSpriteAsync() => Task.FromResult(SongCore.Loader.defaultCoverImage);
			public Task<AudioClip> GetPreviewAudioClip() => Task.FromResult(fakeAudioClip);
			public void UnloadPreviewAudioClip() { }
			public void UnloadCoverSprite() { }
		}

		static readonly FakePreviewMediaData previewMediaData = new FakePreviewMediaData();

		public void UpdateFakeBeatmap(int lengthSeconds) {
			if(lengthSeconds > 480 * 60)
				throw new ArgumentOutOfRangeException();

			const float bpm = 13.37f;
			const BeatmapDifficulty difficulty = BeatmapDifficulty.ExpertPlus;

			var levelId = $"{LevelIdPrefix}{lengthSeconds}";

			if(lastShaffuruMapLength != lengthSeconds) {
				if(fakeAudioClip != null)
					GameObject.Destroy(fakeAudioClip);

				fakeAudioClip = AudioClip.Create("", lengthSeconds * 1000, 1, 1000, false);

				lastShaffuruMapLength = lengthSeconds;
			}

			var comedy = (DateTime.Now.Day == 1 && DateTime.Now.Month == 4) || rngSource.Value.NextDouble() >= 0.98;
			var a = (bpm * ((lengthSeconds - 1) / 60f)).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

			string beatmapJson;

			if(comedy) {
				beatmapJson = @"{""version"":""3.0.0"",""basicBeatmapEvents"":[{""i"":3,""f"":3}],""colorNotes"":[{""b"":0.33,""x"":1,""y"":1,""d"":8},{""b"":" + a + @",""x"":1,""y"":1,""d"":8}],""bombNotes"":[],""obstacles"":[{""b"":0.33,""y"":2,""d"":0.02,""w"":1,""h"":-3},{""b"":0.33,""x"":2,""y"":2,""d"":0.02,""w"":1,""h"":-3},{""b"":0.33,""y"":2,""d"":0.02,""w"":3,""h"":1},{""b"":0.33,""x"":1,""y"":0,""d"":0.02,""w"":1,""h"":1},{""b"":0.33,""x"":3,""d"":0.02,""w"":1,""h"":2},{""b"":" + a + @",""x"":0,""d"":0.03,""w"":4,""h"":5}],""bpmEvents"":[],""rotationEvents"":[],""sliders"":[],""burstSliders"":[],""waypoints"":[],""colorBoostBeatmapEvents"":[],""lightColorEventBoxGroups"":[],""lightRotationEventBoxGroups"":[],""basicEventTypesWithKeywords"":[],""useNormalEventsAsCompatibleEvents"":true}";
			} else {
				beatmapJson = @"{""version"":""3.0.0"",""basicBeatmapEvents"":[{""b"":0,""et"":3,""i"":0,""f"":3}],""colorNotes"":[{""b"":0.5,""x"":1,""y"":0,""c"":1,""d"":1},{""b"":0.5,""x"":1,""y"":1,""c"":1,""d"":1},{""b"":0.5,""x"":1,""y"":2,""c"":1,""d"":1},{""b"":0.5,""x"":2,""y"":0,""c"":0,""d"":8},{""b"":" + a + @",""x"":1,""y"":1,""c"":0,""d"":8}],""bombNotes"":[],""obstacles"":[{""b"":0.4,""x"":3,""y"":0,""d"":3,""w"":1,""h"":5},{""b"":0.4,""x"":0,""y"":0,""d"":3,""w"":1,""h"":5},{""b"":" + a + @",""x"":0,""y"":0,""d"":0.1,""w"":4,""h"":5}],""bpmEvents"":[],""rotationEvents"":[],""sliders"":[],""burstSliders"":[],""waypoints"":[],""colorBoostBeatmapEvents"":[],""lightColorEventBoxGroups"":[],""lightRotationEventBoxGroups"":[],""basicEventTypesWithKeywords"":[],""useNormalEventsAsCompatibleEvents"":true}";
			}

			var audioJson = @"{""version"":""4.0.0"",""songChecksum"":"""",""songSampleCount"":0,""songFrequency"":0,""bpmData"":[],""lufsData"":[]}";
			var lightshowJson = @"{""version"":""4.0.0"",""waypoints"":[],""basicEvents"":[],""colorBoostEvents"":[],""eventBoxGroups"":[]}";

			beatmapLevelData = new BeatmapEditor3D.BeatmapLevelData(
				"Shaffuru",
				fakeAudioClip,
				audioJson,
				beatmapJson,
				lightshowJson
			);

			var beatmapBasicData = new BeatmapBasicData(
				noteJumpMovementSpeed: 10,
				noteJumpStartBeatOffset: 0,
				environmentName: defaultEnvironment,
				beatmapColorScheme: null,
				notesCount: comedy ? 2 : 5,
				cuttableObjectsCount: comedy ? 2 : 5,
				obstaclesCount: comedy ? 6 : 3,
				bombsCount: 0,
				mappers: new[] { "Kinsi55" },
				lighters: new[] { "Kinsi55" }
			);

			var beatmapBasicDatas = new Dictionary<(GameplayType, BeatmapCharacteristic, BeatmapDifficulty), BeatmapBasicData> {
				[(GameplayType.BeatSaber, BeatmapCharacteristic.Standard, difficulty)] = beatmapBasicData
			};

			beatmapLevel = new BeatmapLevel(
				version: 1,
				hasPrecalculatedData: false,
				levelID: levelId,
				songName: $"Shaffuru ({lengthSeconds / 60} Minutes)",
				songSubName: "",
				songAuthorName: "Kinsi55",
				allMappers: new[] { "Kinsi55" },
				allLighters: new[] { "Kinsi55" },
				beatsPerMinute: bpm,
				integratedLufs: 0,
				songTimeOffset: 0,
				previewStartTime: 0,
				previewDuration: 0,
				songDuration: lengthSeconds,
				contentRating: default,
				previewMediaData: previewMediaData,
				beatmapBasicData: beatmapBasicDatas
			);

			beatmapKey = new BeatmapKey(levelId, GameplayType.BeatSaber, BeatmapCharacteristic.Standard, difficulty);
		}

		public void Start(int lengthSeconds, int rngSeed = 0) {
			if(rngSeed != 0)
				rngSource.Value = new System.Random(rngSeed);

			UpdateFakeBeatmap(lengthSeconds);

			var endCallback = new Action<StandardLevelScenesTransitionSetupData, LevelCompletionResults>((a, b) => {
				if(b.levelEndAction == LevelCompletionResults.LevelEndAction.Restart) {
					Start(lengthSeconds);
					return;
				}

				BeatmapLoader.RefreshLevelPacksIfNecessary();

				if(b.levelEndStateType == LevelCompletionResults.LevelEndStateType.Cleared ||
					b.levelEndStateType == LevelCompletionResults.LevelEndStateType.Failed ||
					// If user is dum dum and plays with nofail and then backs out to menu we show this too because we are nice :)
					b.energy == 0f
				) {
					finishedOrFailedCallback?.Invoke(b);
				}
			});

			var key = beatmapKey;

			var colorSchemesSettings = playerDataModel.playerData.colorSchemesSettings;

			menuTransitionsHelper.StartStandardLevel(
				GameMode.Solo,
				in key,
				beatmapLevel,
				playerDataModel.playerData.overrideEnvironmentSettings,
				colorSchemesSettings.GetOverrideColorScheme(),
				colorSchemesSettings.ShouldOverrideLightshowColors(),
				playerDataModel.playerData.gameplayModifiers,
				playerDataModel.playerData.playerSpecificSettings,
				null,
				environmentsListModel,
				new GameplayAdditionalInformation("Menu"),
				null,
				null,
				endCallback,
				null,
				beatmapLevelData
			);
		}
	}
}
