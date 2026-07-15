using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaffuru.Util {
	static class PlaylistsUtil {
		/*
		 * NOTE (2024 rewrite): BeatSaberPlaylistsLib's IPlaylist.BeatmapLevels property looks tempting but is
		 * genuinely BeatmapLevel[] now (confirmed the hard way via an InvalidCastException at runtime) - not
		 * PlaylistSong-shaped objects with per-song metadata like highlighted Difficulties. The concrete
		 * LegacyPlaylist/BlistPlaylist classes each have their own separately-named "Songs" property
		 * (List<LegacyPlaylistSong> / List<BlistPlaylistSong> respectively) which is what actually carries
		 * LevelId + Difficulties - confirmed for BlistPlaylistSong directly; LegacyPlaylistSong is assumed to mirror
		 * the same shape (LevelId, Difficulties) since it matches the plain JSON schema legacy playlists use.
		 */
		public static Dictionary<string, BeatmapDifficulty[]> GetAllSongsInPlaylist(string playlistName) {
			var x = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager
				.GetAllPlaylists(true)
				.FirstOrDefault(x => x.Title == playlistName);

			IEnumerable<IGrouping<string, BeatSaberPlaylistsLib.Types.Difficulty>> theThing = null;

			if(x is BeatSaberPlaylistsLib.Legacy.LegacyPlaylist l) {
				theThing = l.Songs
					.SelectMany(s => (s.Difficulties ?? new List<BeatSaberPlaylistsLib.Types.Difficulty>()).Select(d => (s.LevelId, d)))
					.GroupBy(t => t.LevelId, t => t.d);
			} else if(x is BeatSaberPlaylistsLib.Blist.BlistPlaylist bl) {
				theThing = bl.Songs
					.SelectMany(s => (s.Difficulties ?? new List<BeatSaberPlaylistsLib.Types.Difficulty>()).Select(d => (s.LevelId, d)))
					.GroupBy(t => t.LevelId, t => t.d);
			} else {
				return null;
			}

			// Songs with zero difficulties listed at all wouldn't show up in the GroupBy above (SelectMany drops
			// them), but they still need an entry (with null = "no specific diff highlighted") in the result
			IEnumerable<string> allLevelIds = x is BeatSaberPlaylistsLib.Legacy.LegacyPlaylist l2
				? l2.Songs.Select(s => s.LevelId)
				: ((BeatSaberPlaylistsLib.Blist.BlistPlaylist)x).Songs.Select(s => s.LevelId);

			var playlistSongs = new Dictionary<string, BeatmapDifficulty[]>();

			foreach(var levelId in allLevelIds)
				playlistSongs[levelId] = null;

			if(!Config.Instance.filter_playlist_onlyHighlighted)
				return playlistSongs;

			foreach(var xy in theThing) {
				var highlightedDiffs = xy.ToArray();

				if(highlightedDiffs.Length != 0)
					playlistSongs[xy.Key] = highlightedDiffs.Select(x => x.BeatmapDifficulty).Distinct().ToArray();
			}

			return playlistSongs;
		}
	}
}
