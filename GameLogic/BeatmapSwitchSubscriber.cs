using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shaffuru.AppLogic;
using Zenject;

namespace Shaffuru.GameLogic {
	class BeatmapSwitchSubscriber : IInitializable, IDisposable {
		[Inject] readonly QueueProcessor queueProcessor = null;
		[Inject] readonly BeatmapSwitcher beatmapSwitcher = null;
		readonly int delayMs = 0;

		public BeatmapSwitchSubscriber(int delayMs = 0) {
			this.delayMs = delayMs;
		}

		public void Initialize() {
			queueProcessor.switchedToNewSong += QueueProcessor_switchedToNewSong;
		}

		public void Dispose() {
			queueProcessor.switchedToNewSong -= QueueProcessor_switchedToNewSong;
		}

		private void QueueProcessor_switchedToNewSong(ShaffuruSong song, BeatmapLevel beatmapLevel, BeatmapKey beatmapKey, IReadonlyBeatmapData beatmapData, UnityEngine.AudioClip audioClip) {
			void cont() => beatmapSwitcher.SwitchToDifferentBeatmap(beatmapLevel, beatmapKey, beatmapData, audioClip, (float)song.startTime, (float)song.length);

			if(delayMs == 0) {
				cont();
				return;
			}

			Task.Delay(delayMs).ContinueWith(_ => cont(), TaskContinuationOptions.None);
		}
	}
}
