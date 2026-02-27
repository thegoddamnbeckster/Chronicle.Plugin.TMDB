# Chronicle.Plugin.TMDB

TMDB metadata provider plugin for [Chronicle](https://github.com/thegoddamnbeckster/Chronicle).

Fetches movie and TV show metadata — titles, overviews, cast, crew, ratings, poster and backdrop images — from [The Movie Database (TMDB)](https://www.themoviedb.org/).

## Supported Media Types

| Media Type | Fields |
|------------|--------|
| `movie`    | title, overview, year, poster_url, backdrop_url, runtime_minutes, genres, cast, directors, rating |
| `tv`       | title, overview, year, poster_url, backdrop_url, runtime_minutes, genres, cast, rating |

## Installation

1. Build the plugin in Release mode:
   ```powershell
   dotnet publish -c Release -o ./publish
   ```

2. In the Chronicle web UI → **Plugins** → **Install Plugin**, enter the path to `Chronicle.Plugin.TMDB.dll` inside the `publish/` folder.

3. After installation, go to the plugin's settings and enter your TMDB API key.

## Configuration

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `api_key` | ✓ | — | TMDB v3 API key. Get one free at https://www.themoviedb.org/settings/api |
| `language` | | `en-US` | BCP 47 language tag for titles and overviews |
| `include_adult` | | `false` | Include adult content in search results |
| `poster_size` | | `w500` | TMDB image size for poster art |
| `backdrop_size` | | `w1280` | TMDB image size for backdrop/banner images |

## External ID Format

This plugin uses the format `{type}:{tmdbId}` for external IDs — for example:

- `movie:550` → Fight Club
- `tv:1399` → Game of Thrones

## Development

This plugin references Chronicle.Plugins via a local path reference for development.
When Chronicle.Plugins is published to NuGet, update the `.csproj` accordingly.

```xml
<!-- Development (local) -->
<ProjectReference Include="..\Chronicle\src\Chronicle.Plugins\Chronicle.Plugins.csproj"
                  Private="false" ExcludeAssets="runtime" />

<!-- Production (NuGet) -->
<PackageReference Include="Chronicle.Plugins" Version="x.y.z" />
```
