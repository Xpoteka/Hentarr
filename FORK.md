# Hentarr fork notes

Hentarr is a private fork of [Sonarr](https://github.com/Sonarr/Sonarr) (`v5-develop`, forked at `096f4e29f`)
that uses the **AniList GraphQL API** instead of Sonarr's SkyHook/TVDB service for all series metadata, restricted
by default to adult anime. Release parsing, quality profiles, indexers, download clients, import and renaming are
unchanged from upstream.

Nothing in this fork goes upstream. Do not open issues or pull requests against Sonarr/Sonarr from this repository.

## How it works

* The AniList media id is stored in `Series.TvdbId`. There is no new column, API field or migration. `AniListIds`
  and `MalIds` are also filled so a later move to a proper field is easy.
* One AniList entry is one series with exactly one season. Sequels are separate entries on AniList and therefore
  separate series here. Series type is always `Anime`, episodes are numbered 1..n with absolute numbers.
* English titles and synonyms from AniList are delivered as scene mappings, which is the path Sonarr already uses
  for indexer search terms and for matching release titles back to a series.
* Everything keyed by TVDB id is switched off: Sonarr's scene mapping service, TheXEM, `tvdbid`/`imdbid`/`rid`/
  `tvmazeid`/`tmdbid` indexer searches, and release-to-series matching by id.
* Crash reporting (Sentry), analytics, update checks and the health checks that call `services.sonarr.tv` are
  disabled through one switch, `NzbDrone.Common.Fork.ForkSettings`.

## Settings

| Setting | Where | Values | Default |
|---|---|---|---|
| Add related entries | env `HENTARR_ADD_RELATIONS` or `<AniListAddRelatedSeries>` in `config.xml` | `true`, `false` | `true` |
| Adult filter for search | env `HENTARR_ADULT_FILTER` or `<AniListAdultFilter>` in `config.xml` | `adult`, `nonadult`, `all` | `adult` |
| Port | env `SONARR__SERVER__PORT` or `<Port>` in `config.xml` | | `8989` (Docker image: `8990`) |
| Instance name | env `SONARR__APP__INSTANCENAME` or `<InstanceName>` | must start or end with `Sonarr` (upstream rule) | `Sonarr` (Docker image: `Sonarr - Hentarr`) |

When a series is added, every related AniList entry (sequel, prequel, side story, spin-off, parent, alternative,
summary) that passes the adult filter is added as well, with the same root folder, profile, monitoring and tags. Each
added entry repeats the step, so a whole franchise such as Taimanin comes in with one add. AniList relations are not symmetric, so a second pass searches the franchise word of the title (for example
"Taimanin") and adds entries whose own relations point back at the library. Entries already in the
library or on the import list exclusions are skipped, so deleting one with "add exclusion" keeps it out. The
`AddRelatedSeries` command (System → Tasks, or `POST /api/v3/command {"name":"AddRelatedSeries"}`, optionally with
`seriesId`) runs the same expansion for titles that are already in the library.

Search terms: a plain title, `anilist:<id>`, `mal:<id>`. `tvdb:<id>` is treated as an AniList id. `imdb:` and
`tmdb:` return nothing.

## New files (fork-only)

| Path | Purpose |
|---|---|
| `src/NzbDrone.Common/Fork/ForkSettings.cs` | Single switch for everything that contacted Sonarr's services |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListGraphQlClient.cs` | GraphQL client: search, by id, by MAL id, `id_in` batches, 429 back-off |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListMapper.cs` | AniList media to `Series` / `Episode` mapping, slug, HTML stripping, alternate titles |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListMetadataProxy.cs` | The only `IProvideSeriesInfo` / `ISearchForNewSeries` implementation |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListMetadataOptions.cs` | Adult filter setting |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListTitleCache.cs` | In-memory cache of alternate titles fetched during add/refresh |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListException.cs` | Error type surfaced to the UI |
| `src/NzbDrone.Core/MetadataSource/AniList/AniListRelatedSeriesService.cs`, `AddRelatedSeriesCommand.cs` | Adds related AniList entries on add and on command |
| `src/NzbDrone.Core/MetadataSource/AniList/Resource/AniListResource.cs` | GraphQL response classes |
| `src/NzbDrone.Core/DataAugmentation/AniList/AniListSceneMappingProvider.cs` | Emits alternate titles as scene mappings |
| `src/NzbDrone.Core/DataAugmentation/AniList/AniListSceneMappingTrigger.cs` | Queues a scene mapping update on every series add/import |
| `src/NzbDrone.Core/DataAugmentation/AniList/AniListSceneMappingCleanup.cs` | Purges leftover TVDB-keyed mappings at startup |
| `src/NzbDrone.Core/MediaFiles/EpisodeImport/Aggregation/Aggregators/AggregateSingleEpisodeFallback.cs` | Maps a numberless video file to the only episode of a single-episode entry on import |
| `src/NzbDrone.Core.Test/MetadataSource/AniList/*`, `src/NzbDrone.Core.Test/DataAugmentation/AniList/*`, `src/NzbDrone.Core.Test/IndexerTests/NewznabTests/NewznabRequestGeneratorForkFixture.cs`, `src/NzbDrone.Core.Test/Files/AniList/*.json` | Unit tests and recorded AniList fixtures |
| `Dockerfile`, `.dockerignore` | Container build |
| `FORK.md` | This file |

## Upstream files modified

Every edit is marked with a `// Fork:` comment or an `[Ignore("Fork: ...")]` attribute.

| File | Change |
|---|---|
| `src/NzbDrone.Core/MetadataSource/SkyHook/SkyHookProxy.cs` | No longer implements `IProvideSeriesInfo`, `ISearchForNewSeries` (class kept, unused) |
| `src/NzbDrone.Core/DataAugmentation/Scene/ServicesProvider.cs` | No longer implements `ISceneMappingProvider` |
| `src/NzbDrone.Core/DataAugmentation/Xem/XemService.cs` | No longer implements `ISceneMappingProvider` or handles series events |
| `src/NzbDrone.Core/Indexers/Newznab/NewznabRequestGenerator.cs` | `ExternalIdSearchesEnabled => false` gates the five `Supports*Search` flags; anime season searches send a plain title query when the standard season format is off |
| `src/NzbDrone.Core/DecisionEngine/DownloadDecisionMaker.cs` | Passes no ids to `IParsingService.Map` |
| `src/NzbDrone.Core/Tv/AddSeriesService.cs`, `src/NzbDrone.Core/Tv/RefreshSeriesService.cs` | Series type is pinned to Anime on add and on every refresh; with Standard the indexers used here receive no search at all |
| `src/NzbDrone.Core/Parser/ParsingService.cs` | A release named after the series with no numbers maps to the single season; for a single-episode entry it becomes that episode instead of a season pack |
| `src/NzbDrone.Core/MediaFiles/EpisodeImport/Aggregation/AggregationService.cs` | Unparsed media files are rejected after the aggregators ran, so the single-episode fallback can map them |
| `src/NzbDrone.Common/Instrumentation/NzbDroneLogger.cs` | Sentry target only registered when `ForkSettings.CrashReportingEnabled` |
| `src/NzbDrone.Core/Update/UpdatePackageProvider.cs` | Returns no updates when `ForkSettings.UpdaterEnabled` is false |
| `src/NzbDrone.Core/Configuration/ConfigFileProvider.cs` | `AnalyticsEnabled` and `UpdateAutomatically` forced false |
| `src/NzbDrone.Core/HealthCheck/ServerSideNotificationService.cs`, `Checks/SystemTimeCheck.cs`, `Checks/ProxyCheck.cs` | Return a healthy result without calling `services.sonarr.tv` |
| `src/NzbDrone.Core.Test/IndexerTests/NewznabTests/NewznabRequestGeneratorFixture.cs` | Whole fixture ignored (assumes id searches); replaced by the fork fixture |
| `src/NzbDrone.Core.Test/UpdateTests/UpdatePackageProviderFixture.cs` | Ignored (calls `services.sonarr.tv`) |
| `src/NzbDrone.Core.Test/HealthCheck/Checks/SystemTimeCheckFixture.cs` | One test ignored |
| `frontend/src/Series/Details/SeriesDetailsLinks.tsx` | External link goes to `anilist.co/anime/<id>` |
| `frontend/src/AddSeries/AddNewSeries/AddNewSeriesSearchResult.tsx` | Same |
| `frontend/src/AddSeries/ImportSeries/Import/SelectSeries/ImportSeriesSearchResult.tsx` | Same |
| `frontend/src/AddSeries/AddNewSeries/AddNewSeries.tsx` | Search box hint mentions `anilist:` and `mal:` |
| `frontend/src/AddSeries/addSeriesOptionsStore.ts` | Add form defaults to series type Anime |

## Tested against real indexers (2026-10-09)

Searched through Prowlarr (sukebei.nyaa.si, Tokyo Toshokan, Nyaa.si) for eleven titles from T-Rex and Pink Pineapple.
What worked: title and synonym searches with absolute numbers, batch releases named after the title (`[Abysswalker] Joshi
Luck! (じょしラク!) [720p][1080p][WEB-DL]`, `euphoria (BD 1080p H264 AAC)`), numberless single-episode releases
(`[007nF] Aki Sora (BD 1920x1080 x264 10bits AAC)` maps to episode 1), `×` titles (`Pretty x Cation`), import of
numbered and numberless files, renaming with the anime format, and refresh keeping files.

Operational notes:

* Most hentai fansub releases carry no resolution token and parse as quality **Unknown**. Allow Unknown in the quality
  profile (it is off in the defaults), otherwise releases such as `[SakuraCircle] Joshi Luck! - 01-02 (OVA...) - English
  Softsubs` are rejected.
* For English-subtitled releases use three custom formats on the quality profile, with minimum score 0 and cutoff
  score 100: "English Subs" +100 (release title regex `(?i)(eng(lish)?[ ._-]?(soft|hard)?[ ._-]?subs?|engsubs?|eng-subs?|\[eng\]|english[ ._-]?(dub|subtitles)|\bsubbed\b|\bsoftsubs?\b|\bhardsubs?\b|sakuracircle)`),
  "Non-English Subs" -1000 (`(?i)(espa[nñ]ol|sub[ ._-]?esp|latino|castellano|vostfr|french|german|deutsch|russian|\brus\b|italian|\bita\b|portugu[eê]s|pt-br|chinese|简|繁|中文|字幕|\bcht\b|\bchs\b|gb_cn|polish|turkish|indonesia|thai|korean|arabic)`)
  and "Raw (no subs)" -1000 (`(?i)(\[raws?\]|\(raw\)|[ ._-]raws?[ ._\]\)-]|\bno[ ._-]?subs?\b|\bunsub(bed)?\b)`). Keep each format to one
  condition type: Sonarr ANDs different condition types, so a language condition next to the title regex matches nothing
  because these releases parse as Japanese.
* Leave **Anime Standard Format Search** off on the indexers; sukebei only supports plain `q` searches.
* Prowlarr rate-limits Tokyo Toshokan; expect occasional `429` and a one-minute back-off.

Known parser gaps seen in results (upstream parser, not changed): `Title 01 - 04`, `Title Ep.01-02`, `Title__Ep 01`,
titles prefixed with a Japanese title and `/`, and `[Group] Title [fanservice compilation]`, which the single-episode rule
maps to episode 1. A release profile with "must not contain: compilation, preview, PV" avoids the last one. AniList has
separate entries with the same title (Aki-Sora TV series and OVA), so releases of one can match the other.

## Known broken or degraded

* Import lists, Trakt, calendar feeds, Kodi/Plex metadata exporters and notifications still send the AniList id
  where they expect a TVDB id. They are not used by this instance.
* The MyAnimeList import list still talks to `services.sonarr.tv` for OAuth. Do not enable it.
* BroadcastheNet and HDBits indexers send `Series.TvdbId` in their searches (`BroadcastheNetRequestGenerator.cs`,
  `HDBitsRequestGenerator.cs`). They are private trackers for Western TV and must not be configured here.
* The proxy health check (which pinged `services.sonarr.tv` through the proxy) is disabled.
* System > Updates shows no entries. Update the fork by rebuilding.
* Labels and log lines still say "TVDB" next to AniList ids. Episode titles are "Episode n". No fanart, season
  posters or actors.
* Scene mappings from AniList refresh on every series add, every 3 hours (scheduled task) and after a restart;
  synonyms edited on AniList therefore appear within 3 hours of the next refresh of that series.

## Rebasing on upstream

1. `git fetch upstream` and `git rebase upstream/v5-develop`.
2. Conflicts can only occur in the files listed under "Upstream files modified". Re-apply the `// Fork:` lines.
3. If upstream adds a new `IProvideSeriesInfo` / `ISearchForNewSeries` / `ISceneMappingProvider` implementation,
   detach it the same way as `SkyHookProxy` / `ServicesProvider`, otherwise DryIoc resolves two implementations.
4. If upstream adds a new call to `ISonarrCloudRequestBuilder`, gate it with `ForkSettings`. Grep for
   `SonarrCloudRequestBuilder` and `sonarr.tv`.
5. Build (`dotnet build src/Sonarr.sln -p:Platform=Posix`), run `Sonarr.Core.Test` unit tests and compare with the
   previous run, then start the app with trace logging and confirm the only outbound hosts are `graphql.anilist.co`
   and `s4.anilist.co`:

   ```sh
   SONARR__LOG__LEVEL=trace SONARR__LOG__CONSOLELEVEL=trace ./Sonarr -nobrowser -data=/tmp/hentarr-data | \
     grep -o 'Req: \[[A-Z]*\] https\?://[A-Za-z0-9._-]*' | sort | uniq -c
   ```

## Building

```sh
yarn install && yarn build                                   # frontend -> _output/UI
dotnet build src/Sonarr.sln -c Debug -p:Platform=Posix       # backend  -> _output/net10.0
cp -R _output/UI _output/net10.0/UI
_output/net10.0/Sonarr -nobrowser -data=/path/to/config
```

Docker (TrueNAS SCALE or any host with buildx):

```sh
docker build -t hentarr .
docker run -d --name hentarr -p 8990:8990 \
  -e HENTARR_ADULT_FILTER=adult \
  -v /mnt/pool/apps/hentarr:/config \
  -v /mnt/pool/media/hentai:/media \
  hentarr
```

The image runs as root by default; pass `--user 1000:1000` (or the TrueNAS app user) and make `/config` and
`/media` writable by that user if you prefer.
