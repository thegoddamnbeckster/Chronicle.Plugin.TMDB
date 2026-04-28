# Chronicle.Plugin.TMDB

TMDB metadata provider plugin for [Chronicle](https://github.com/thegoddamnbeckster/Chronicle).

Fetches movie and TV metadata — titles, overviews, cast, crew, ratings, genres, poster and backdrop images — from [The Movie Database (TMDB)](https://www.themoviedb.org/).

## Supported Media Types

| Media Type | Fields |
|------------|--------|
| `movie`    | title, overview, year, poster_url, backdrop_url, runtime_minutes, genres, cast, directors, rating |
| `tv`       | title, overview, year, poster_url, backdrop_url, genres, cast, rating |
| `anime`    | title, overview, year, poster_url, backdrop_url, genres, cast, rating |
| `fanedits`  | title, overview, year, poster_url, backdrop_url, runtime_minutes, genres, cast, directors, rating |
| `tv.1` (Seasons) | title, overview, year, poster_url, backdrop_url |
| `tv.2` (Episodes) | title, overview, year, poster_url, runtime_minutes |

## External ID Format

`{type}:{tmdbId}` — for example:

- `movie:550` → Fight Club
- `tv:1399` → Game of Thrones
- `tv:94997` → Shōgun (also returned for anime mapped to TMDB TV)

Fix Match accepts full TMDB URLs:
- `https://www.themoviedb.org/movie/550`
- `https://www.themoviedb.org/tv/1399/season/2/episode/5`

## Installation

1. Build the plugin:
   ```powershell
   dotnet build -c Release
   ```

2. Copy `bin\Release\net9.0\*.dll` and `manifest.json` into your Chronicle `plugins\chronicle.plugin.tmdb\` directory.

3. After installation, go to Chronicle → Plugins → TMDB → Settings and enter your API key.

## Configuration

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `api_key` | ✓ | — | TMDB v3 API key. Free at https://www.themoviedb.org/settings/api |
| `language` | | `en-US` | BCP 47 language tag for titles and overviews |
| `include_adult` | | `false` | Include adult content in search results |
| `poster_size` | | `w500` | TMDB image size for poster art |
| `backdrop_size` | | `w1280` | TMDB image size for backdrop images |

## Development

This plugin references Chronicle.Plugins via a local path reference:

```xml
<ProjectReference Include="..\Chronicle\src\Chronicle.Plugins\Chronicle.Plugins.csproj"
                  Private="false" ExcludeAssets="runtime" />
```

`Chronicle.Plugins.dll` must **not** be copied to the plugin output directory — the host provides it.
