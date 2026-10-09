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
