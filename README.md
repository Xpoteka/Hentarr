# Hentarr

Hentarr is a fork of [Whisparr](https://github.com/Whisparr/Whisparr) that manages an adult anime collection for Usenet and BitTorrent users, using **AniDB** as its metadata source and integrating with **[Shoko Server](https://shokoanime.com/)** as the system of record for what you already own.

Like every \*arr, it monitors wanted episodes, interfaces with your indexers and download clients (SABnzbd, NZBGet, qBittorrent, Deluge, rTorrent, Transmission, and more) to grab, sort, and rename releases, and can automatically upgrade the quality of existing files when a better format becomes available.

## What Hentarr adds on top of Whisparr

### 1. AniDB as the metadata source

The Whisparr/TPDB metadata proxy has been replaced with a direct AniDB client:

* **Search** is served locally from the daily [AniDB titles dump](https://wiki.anidb.net/API#Anime_Titles), so searching never hits the AniDB API. You can also add titles directly with `anidb:<id>` / `aid:<id>`.
* **Series/episode metadata** (titles, air dates, episode lists, studio, tags, ratings, poster) is fetched from the AniDB HTTP API, rate limited to stay within AniDB's client policy. Banned responses are detected and back off automatically.
* Regular episodes and specials are imported; credits/trailers/parodies are skipped. Seasons follow the Whisparr convention (season = air year, specials = season 0), and AniDB episode numbers are kept as absolute episode numbers.

Configuration (optional, in `config.xml`):

```xml
<AniDbApiUrl>http://api.anidb.net:9001/httpapi</AniDbApiUrl>
<AniDbClientName>hentarr</AniDbClientName>
<AniDbTitlesUrl>https://anidb.net/api/anime-titles.xml.gz</AniDbTitlesUrl>
<AniDbCatalogBatchSize>40</AniDbCatalogBatchSize>
```

> **Note:** AniDB requires HTTP API clients to be registered. Register your own client name on [anidb.net](https://anidb.net/software/add) and set it as `AniDbClientName` before heavy use. Hentarr shows a health check notice while the default client name is in use.

### 2. Catalog: knowing what exists

A background task (*AniDB Catalog Sync*, hourly) maintains a local catalog table:

* The titles dump is refreshed daily and every AniDB title is seeded into the catalog, so newly published titles are discovered automatically — no manual seeding required.
* AniDB has no "all anime by studio X" lookup; studio information only exists per anime. The catalog sync therefore **aggregates studios locally**: each run it fetches a small, rate-limit-friendly batch of anime (newest first) and records studio, year, and the 18+ flag. The studio index grows continuously in the background.

### 3. Studio → works: the "AniDB Studio" import list

Add an import list of type **AniDB Studio** (Settings → Import Lists), pick a studio from the aggregated catalog (optionally 18+ titles only), choose root folder/quality profile/monitor settings — and every known work of that studio is added and queued automatically. As the catalog aggregation discovers new works of that studio (including brand-new releases), they are picked up by the next list sync.

### 4. Shoko Server integration

Add an import list of type **Shoko** with the URL and API key of your Shoko Server (`POST /api/auth` on Shoko generates a key). Series are matched via AniDB IDs. It can do three things, all optional:

* **Import** your Shoko collection into Hentarr (standard import list behaviour).
* **Sync Library As Exclusions** ("have" sync): everything already in your Shoko library is written to the Import List Exclusions, so studio lists and other lists never re-queue titles you already own.
* **Unmonitor Owned Episodes**: for series managed in Hentarr, episodes that already have files in Shoko are unmonitored so they are not downloaded again.

The Shoko sync runs on its own schedule (*Shoko Sync*, every 6 hours) in addition to the normal list sync.

### 5. Existing minus have = wanted, on a schedule

The pieces above combine into the requested pipeline, using the native \*arr machinery:

```
titles dump (what exists)            – refreshed daily
  → studio aggregation               – hourly batches
    → AniDB Studio import lists      – what you want
      – Shoko exclusions / unmonitor – what you have
        = auto-added monitored series with missing episodes ("wanted")
          → indexer search & RSS sync → Usenet/BitTorrent download
```

New titles from a watched studio are queued automatically as soon as the catalog learns about them; nothing you already have in Shoko is queued again.

### 6. Anime release parsing and search

On top of the Whisparr scene parser (date/title matching), Hentarr parses absolute-numbered anime release names and maps them to episodes via the AniDB absolute episode numbers:

* `[SubGroup] Title - 01`, `[SubGroup] Title - 01v2`, `[SubGroup] Title - 01-02` (ranges)
* `Title - 01 [SubGroup]`, `Title - 12 (DVD 480p)`
* `Title Episode 1`, `Title.Ep02.1080p`

Date-based patterns are tried first, so scene/JAV releases keep their existing matching. Searches additionally send `Title 01` style queries to Newznab/Torznab indexers (and Fanzub with *Anime Standard Format Search*), alongside the existing date, episode-title, and external-ID queries.

## Known limitations

* Series matching uses the AniDB main title (romaji). Releases named with an English synonym only match if the episode title or date lines up; alternate-title (synonym) matching is a future step.
* Studio aggregation is intentionally slow (AniDB rate limits, `AniDbCatalogBatchSize` per hour); a complete first pass over the catalog takes a while. Newest titles are aggregated first.
* Catalog entries are synced once; metadata corrections on AniDB are picked up when a series is added/refreshed, not by the catalog.
* Rebranding covers all user-visible text (UI, login, notifications, user agent). Internal names, executables and the data directory keep the upstream `Whisparr`/`NzbDrone` names for compatibility, the same way Whisparr keeps Sonarr internals.

## Major Features Include

* Support for major platforms: Windows, Linux, macOS, Raspberry Pi, etc.
* Can watch for better quality of the episodes you have and do an automatic upgrade. *e.g. from DVD to Blu-Ray*
* Automatic failed download handling will try another release if one fails
* Manual search so you can pick any release or to see why a release was not downloaded automatically
* Full integration with SABnzbd and NZBGet
* Automatically searching for releases as well as RSS Sync
* Automatically importing downloaded episodes
* SABnzbd, NZBGet, QBittorrent, Deluge, rTorrent, Transmission, uTorrent, and other download clients are supported and integrated
* Full integration with Kodi and Plex (notifications, library updates)
* Advanced customization for profiles, such that Hentarr will always download the copy you want
* A beautiful UI

## Support

Hentarr is a community fork; internal names (`NzbDrone`, `Whisparr`) are kept for compatibility with the upstream codebase, the same way Whisparr keeps Sonarr internals.

## License

* [GNU GPL v3](http://www.gnu.org/licenses/gpl.html)
* Copyright 2010-2025
