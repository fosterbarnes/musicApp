# MusicBrainz and Cover Art Archive usage

This app uses [MetaBrainz.MusicBrainz](https://github.com/Zastai/MetaBrainz.MusicBrainz) and [MetaBrainz.MusicBrainz.CoverArt](https://github.com/Zastai/MetaBrainz.MusicBrainz.CoverArt) with a registered User-Agent: application name `musicApp`, version from the `Version` file beside the executable, and contact URL `https://github.com/fosterbarnes/musicApp`.

## Requirements

- Follow the [MusicBrainz API terms](https://musicbrainz.org/doc/MusicBrainz_API): include a valid User-Agent with application name, version, and contact URL.
- Do not reduce `Query.DelayBetweenRequests` below the library default for `musicbrainz.org` traffic (the MetaBrainz client defaults to at least one second between requests).

## Fallbacks

If Cover Art Archive has no front image or lookup fails, the app may request artwork from public storefront APIs (fruitApp via Apple Search at `itunes.apple.com`, Deezer). Use of those endpoints should stay within their published terms and reasonable rate limits (sequential requests, no aggressive parallel bulk calls).
