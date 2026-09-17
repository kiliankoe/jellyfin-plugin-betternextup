# Better Next Up for Jellyfin

A Jellyfin plugin that orders the Next Up section the way Plex orders On Deck.

Jellyfin ranks every show in Next Up by the date you last watched an episode of it. A show you paused months ago therefore sits at the back, even when a new episode arrived yesterday. Plex ranks a show by its latest activity, which is either that last watch or a new episode being added to the library. This plugin brings the Plex ordering to Jellyfin: a show jumps to the front whenever its next episode is newer than your last watch of it.

The "Max days in Next Up" client setting keeps working, but it now applies to that latest activity instead of the last watch. A show you stopped watching two years ago reappears when a new episode lands and drops out again once that episode has aged past the limit.

## Infuse

Infuse builds its Watching shelf from Jellyfin's Continue Watching list and never asks for Next Up. For Infuse the plugin folds Next Up into the Continue Watching response and orders the combined list by activity, the way Plex's On Deck does. Shows that already have an in-progress episode in the list are not added a second time.

The client prefixes live in the plugin's XML configuration under `ContinueWatchingMergeClients`, comma-separated. Adding "Jellyfin Web" there merges the lists for the web client too, which is handy for testing but duplicates the Next Up row on the home screen.

## How it works

Jellyfin resolves the Next Up section through an `ITVSeriesManager` service. Plugins register their services after the core does, so the plugin wraps the core manager and re-registers the interface. The wrapper asks the core for the unfiltered, unpaged list, looks up each show's last play date, sorts by the newer of that date and the next episode's date added, and applies the date cutoff and paging itself. Episode selection, specials handling and alternate versions stay with the core.

## Requirements

Jellyfin 12.0 or newer. The plugin targets the 12.0 ABI.

## Build

The repository ships a nix flake with the .NET 10 SDK; `direnv allow` or `nix develop` enters it.

```sh
dotnet test
dotnet build -c Release
```

The release build writes `Jellyfin.Plugin.BetterNextUp/bin/Release/net10.0/Jellyfin.Plugin.BetterNextUp.dll`.

## Install

Copy the DLL into a new folder below your server's plugin directory, then restart the server:

```
<jellyfin config>/plugins/BetterNextUp_0.1.0.0/Jellyfin.Plugin.BetterNextUp.dll
```

Disable the plugin in the dashboard to get the default behavior back.
