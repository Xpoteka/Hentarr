using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListMapperFixture : CoreTest
    {
        private AniListMedia LoadMedia(string fileName)
        {
            var json = ReadAllText($"Files/AniList/{fileName}");
            var response = Json.Deserialize<AniListResponse<AniListMediaData>>(json);

            response.Data.Media.Should().NotBeNull();

            return response.Data.Media;
        }

        [Test]
        public void should_map_multi_episode_ova()
        {
            var media = LoadMedia("media_10851_euphoria.json");
            var series = AniListMapper.MapSeries(media);

            series.TvdbId.Should().Be(10851);
            series.AniListIds.Should().BeEquivalentTo(new[] { 10851 });
            series.MalIds.Should().BeEquivalentTo(new[] { 10851 });
            series.Title.Should().Be("euphoria");
            series.CleanTitle.Should().Be("euphoria");
            series.SortTitle.Should().Be("euphoria");
            series.TitleSlug.Should().Be("euphoria-10851");
            series.OriginalTitle.Should().Be("euphoria");
            series.OriginalLanguage.Should().Be(Language.Japanese);
            series.SeriesType.Should().Be(SeriesTypes.Anime);
            series.Status.Should().Be(SeriesStatusType.Ended);
            series.Year.Should().Be(2011);
            series.FirstAired.Should().Be(new DateTime(2011, 12, 22, 0, 0, 0, DateTimeKind.Utc));
            series.LastAired.Should().Be(new DateTime(2016, 2, 26, 0, 0, 0, DateTimeKind.Utc));
            series.Runtime.Should().Be(30);
            series.Network.Should().Be("Majin");
            series.Genres.Should().Contain("Hentai");
            series.Certification.Should().Be("R18+");
            series.Overview.Should().StartWith("Based on the erotic game by Clock Up.");
            series.Overview.Should().NotContain("<br>");
            series.Overview.Should().NotContain("<i>");
            series.Monitored.Should().BeTrue();
            series.Seasons.Should().HaveCount(1);
            series.Seasons.Single().SeasonNumber.Should().Be(1);
            series.Seasons.Single().Monitored.Should().BeTrue();
            series.SeasonTypes.Should().HaveCount(1);
            series.SeasonTypes.Single().Type.Should().Be(SeasonType.Official);
            series.SeasonTypes.Single().EpisodeCount.Should().Be(6);
            series.Images.Should().Contain(i => i.CoverType == MediaCoverTypes.Poster && i.RemoteUrl.Contains("anilistcdn"));
            series.Images.Should().Contain(i => i.CoverType == MediaCoverTypes.Banner);
            series.Translations.Should().BeEmpty();
            series.Actors.Should().BeEmpty();
        }

        [Test]
        public void should_map_episodes_for_multi_episode_ova()
        {
            var media = LoadMedia("media_10851_euphoria.json");
            var episodes = AniListMapper.MapEpisodes(media);

            episodes.Should().HaveCount(6);
            episodes.Select(e => e.EpisodeNumber).Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5, 6 });
            episodes.Should().OnlyContain(e => e.SeasonNumber == 1);
            episodes.Should().OnlyContain(e => e.AbsoluteEpisodeNumber == e.EpisodeNumber);
            episodes.Should().OnlyContain(e => e.Title == "Episode " + e.EpisodeNumber);
            episodes.Should().OnlyContain(e => e.AirDateUtc.HasValue);
            episodes.Should().OnlyContain(e => e.AirDate != null);
            episodes.Should().OnlyContain(e => e.Runtime == 30);

            // Episodes without an airing schedule entry fall back to the start date
            episodes.First().AirDate.Should().Be("2011-12-22");

            // Episode 6 has an airing schedule entry
            episodes.Last().AirDateUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1456412400).UtcDateTime);
            episodes.Last().AirDate.Should().Be("2016-02-25");
        }

        [Test]
        public void should_map_single_episode_entry()
        {
            var media = LoadMedia("media_6987_single_episode.json");
            var series = AniListMapper.MapSeries(media);
            var episodes = AniListMapper.MapEpisodes(media);

            series.TvdbId.Should().Be(6987);
            series.Title.Should().Be("Aki-Sora");
            series.CleanTitle.Should().Be("akisora");
            series.TitleSlug.Should().Be("aki-sora-6987");
            series.OriginalTitle.Should().Be("あきそら");
            series.Status.Should().Be(SeriesStatusType.Ended);
            series.Year.Should().Be(2009);

            episodes.Should().HaveCount(1);
            episodes.Single().EpisodeNumber.Should().Be(1);
            episodes.Single().AbsoluteEpisodeNumber.Should().Be(1);
            episodes.Single().AirDate.Should().Be("2009-12-18");
        }

        [Test]
        public void should_map_entry_with_null_episodes_and_partial_dates()
        {
            var media = LoadMedia("media_177128_unreleased.json");
            var series = AniListMapper.MapSeries(media);
            var episodes = AniListMapper.MapEpisodes(media);

            series.TvdbId.Should().Be(177128);
            series.MalIds.Should().BeEmpty();
            series.Status.Should().Be(SeriesStatusType.Upcoming);
            series.Year.Should().Be(2027);
            series.FirstAired.Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            series.LastAired.Should().BeNull();
            series.Runtime.Should().Be(0);
            series.Network.Should().BeNull();
            series.Overview.Should().BeNull();
            series.Ratings.Value.Should().Be(0);
            series.SeasonTypes.Single().EpisodeCount.Should().Be(1);

            episodes.Should().HaveCount(1);
            episodes.Single().AirDate.Should().Be("2027-01-01");
        }

        [Test]
        public void should_use_airing_schedule_when_episode_count_is_null()
        {
            var media = LoadMedia("media_177128_unreleased.json");
            media.Episodes = null;
            media.AiringSchedule = new AniListAiringScheduleConnection
            {
                Nodes = new System.Collections.Generic.List<AniListAiringNode>
                {
                    new AniListAiringNode { Episode = 1, AiringAt = 1800000000 },
                    new AniListAiringNode { Episode = 3, AiringAt = 1801000000 }
                }
            };

            var episodes = AniListMapper.MapEpisodes(media);

            episodes.Should().HaveCount(3);
            episodes[0].AirDateUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1800000000).UtcDateTime);
            episodes[1].AirDateUtc.Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            episodes[2].AirDateUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1801000000).UtcDateTime);
        }

        [Test]
        public void should_leave_air_dates_empty_when_no_date_is_known()
        {
            var media = LoadMedia("media_177128_unreleased.json");
            media.StartDate = new AniListFuzzyDate();

            var series = AniListMapper.MapSeries(media);
            var episodes = AniListMapper.MapEpisodes(media);

            series.FirstAired.Should().BeNull();
            series.Year.Should().Be(0);
            episodes.Single().AirDateUtc.Should().BeNull();
            episodes.Single().AirDate.Should().BeNull();
        }

        [Test]
        public void should_fall_back_to_english_then_native_title()
        {
            var media = LoadMedia("media_6987_single_episode.json");
            media.Title.Romaji = null;
            media.Title.English = "Autumn Sky";

            AniListMapper.MapSeries(media).Title.Should().Be("Autumn Sky");

            media.Title.English = null;

            AniListMapper.MapSeries(media).Title.Should().Be("あきそら");
        }

        [Test]
        public void should_map_status()
        {
            var media = LoadMedia("media_6987_single_episode.json");

            media.Status = "RELEASING";
            AniListMapper.MapSeries(media).Status.Should().Be(SeriesStatusType.Continuing);

            media.Status = "HIATUS";
            AniListMapper.MapSeries(media).Status.Should().Be(SeriesStatusType.Continuing);

            media.Status = "CANCELLED";
            AniListMapper.MapSeries(media).Status.Should().Be(SeriesStatusType.Ended);

            media.Status = null;
            AniListMapper.MapSeries(media).Status.Should().Be(SeriesStatusType.Continuing);
        }

        [Test]
        public void should_map_ratings()
        {
            var media = LoadMedia("media_6987_single_episode.json");
            media.AverageScore = 63;

            AniListMapper.MapSeries(media).Ratings.Value.Should().Be(6.3m);
        }

        private AniListChain LoadTaimaninChain()
        {
            var chain = new AniListChain();
            chain.Seasons.Add(LoadMedia("media_3479_taimanin_asagi.json"));
            chain.Seasons.Add(LoadMedia("media_21401_taimanin_asagi_2.json"));
            chain.Seasons.Add(LoadMedia("media_97854_taimanin_asagi_3.json"));
            chain.Specials.Add(LoadMedia("media_8837_bonus_video.json"));
            return chain;
        }

        [Test]
        public void should_map_chain_to_one_series_with_seasons()
        {
            var chain = LoadTaimaninChain();
            var series = AniListMapper.MapSeries(chain);

            series.TvdbId.Should().Be(3479);
            series.Title.Should().Be("Taimanin Asagi");
            series.TitleSlug.Should().Be("taimanin-asagi-3479");
            series.AniListIds.Should().BeEquivalentTo(new[] { 3479, 21401, 97854, 8837 });
            series.MalIds.Should().BeEquivalentTo(new[] { 3479, 31652, 34399, 8837 });
            series.Status.Should().Be(SeriesStatusType.Ended);
            series.Year.Should().Be(2007);
            series.Seasons.Select(s => (s.SeasonNumber, s.Monitored, s.Title)).Should().Equal(
                (0, false, null),
                (1, true, "Taimanin Asagi"),
                (2, true, "Taimanin Asagi 2"),
                (3, true, "Taimanin Asagi 3"));
            series.SeasonTypes.Single().SeasonNumbers.Should().Equal(0, 1, 2, 3);
            series.SeasonTypes.Single().EpisodeCount.Should().Be(8);
        }

        [Test]
        public void should_map_chain_episodes_with_continuous_absolute_and_per_entry_scene_numbers()
        {
            var chain = LoadTaimaninChain();
            var episodes = AniListMapper.MapEpisodes(chain);

            var regular = episodes.Where(e => e.SeasonNumber > 0).ToList();
            regular.Should().HaveCount(8);
            regular.Select(e => e.AbsoluteEpisodeNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
            regular.Select(e => (e.SeasonNumber, e.EpisodeNumber)).Should().Equal((1, 1), (1, 2), (1, 3), (1, 4), (2, 1), (2, 2), (3, 1), (3, 2));
            regular.Select(e => (e.SceneSeasonNumber, e.SceneEpisodeNumber, e.SceneAbsoluteEpisodeNumber)).Should().Equal(
                (1, 1, 1), (1, 2, 2), (1, 3, 3), (1, 4, 4), (2, 1, 1), (2, 2, 2), (3, 1, 1), (3, 2, 2));
            regular.Should().OnlyContain(e => e.Title.StartsWith("Episode "));
            regular.First(e => e.SeasonNumber == 2).AirDate.Should().Be("2015-10-30");

            var specials = episodes.Where(e => e.SeasonNumber == 0).ToList();
            specials.Should().ContainSingle();
            specials[0].EpisodeNumber.Should().Be(1);
            specials[0].AbsoluteEpisodeNumber.Should().BeNull();
            specials[0].SceneSeasonNumber.Should().BeNull();
            specials[0].Title.Should().Be("Bonus Video");
            specials[0].AirDate.Should().Be("2009-05-01");
        }

        [Test]
        public void should_keep_full_special_title_when_it_does_not_start_with_the_series_title()
        {
            var chain = LoadTaimaninChain();
            chain.Specials[0].Title.Romaji = "Asagi Bonus Disc";

            AniListMapper.MapEpisodes(chain).Single(e => e.SeasonNumber == 0).Title.Should().Be("Asagi Bonus Disc");
        }

        [Test]
        public void should_suffix_special_titled_like_a_season_entry()
        {
            var chain = LoadTaimaninChain();
            chain.Specials[0].Title.Romaji = "Taimanin Asagi";
            chain.Specials[0].Episodes = 2;

            var specials = AniListMapper.MapEpisodes(chain).Where(e => e.SeasonNumber == 0).ToList();

            specials.Select(e => e.Title).Should().Equal("Taimanin Asagi (Special) - Part 1", "Taimanin Asagi (Special) - Part 2");
        }

        [Test]
        public void should_keep_single_entry_output_without_scene_fields_or_season_title()
        {
            var media = LoadMedia("media_10851_euphoria.json");
            var series = AniListMapper.MapSeries(media);
            var episodes = AniListMapper.MapEpisodes(media);

            series.Seasons.Single().Title.Should().BeNull();
            series.AniListIds.Should().BeEquivalentTo(new[] { 10851 });
            episodes.Should().OnlyContain(e => e.SceneSeasonNumber == null && e.SceneEpisodeNumber == null && e.SceneAbsoluteEpisodeNumber == null);
        }

        [Test]
        public void should_mark_chain_continuing_when_a_later_season_is_releasing()
        {
            var chain = LoadTaimaninChain();
            chain.Seasons[2].Status = "RELEASING";

            AniListMapper.MapSeries(chain).Status.Should().Be(SeriesStatusType.Continuing);
        }

        [TestCase("<b>Bold</b> text<br>next<br />line", "Bold text\nnext\nline")]
        [TestCase("A &amp; B &quot;quoted&quot;", "A & B \"quoted\"")]
        [TestCase("<p>One</p><p>Two</p>", "One\nTwo")]
        [TestCase("  plain  ", "plain")]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void should_strip_html(string input, string expected)
        {
            AniListMapper.StripHtml(input).Should().Be(expected);
        }

        [TestCase("Aki-Sora", 6987, "aki-sora-6987")]
        [TestCase("Itadaki! Seieki♥", 21067, "itadaki-seieki-21067")]
        [TestCase("Résumé: The Animation", 5, "resume-the-animation-5")]
        [TestCase("ぼくのぴこ", 1639, "anilist-1639")]
        public void should_generate_unique_slug(string title, int id, string expected)
        {
            AniListMapper.GenerateSlug(title, id).Should().Be(expected);
        }

        [Test]
        public void should_collect_alternate_titles_excluding_main_title_and_non_latin()
        {
            var media = LoadMedia("media_6987_single_episode.json");
            media.Title.English = "Autumn Sky";
            media.Synonyms = new System.Collections.Generic.List<string> { "Akisora", "Autumn Sky", "あきそら", "AS", " aki-sora ", "Aki Sora (2009)" };

            var titles = AniListMapper.GetAlternateTitles(media);

            titles.Should().BeEquivalentTo(new[] { "Autumn Sky", "Akisora", "Aki Sora (2009)" });
        }

        [Test]
        public void should_add_spelled_out_variant_for_multiplication_sign_titles()
        {
            var media = LoadMedia("media_6987_single_episode.json");
            media.Title.Romaji = "PRETTY×CATION THE ANIMATION";
            media.Synonyms = new System.Collections.Generic.List<string>();

            AniListMapper.GetAlternateTitles(media).Should().BeEquivalentTo(new[] { "PRETTY x CATION THE ANIMATION" });
        }

        [TestCase("Akisora", true)]
        [TestCase("Itadaki! Seieki♥", false)]
        [TestCase("Résumé", true)]
        [TestCase("あきそら", false)]
        [TestCase("AS", false)]
        [TestCase("---", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void should_decide_if_title_is_useful_for_searching(string title, bool expected)
        {
            AniListMapper.IsUsefulSearchTitle(title).Should().Be(expected);
        }
    }
}
