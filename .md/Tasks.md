## Main Window

### Title Bar
- ~~Play/pause/skip buttons~~
- ~~Volume control~~

#### Song info view:
  - ~~Currently playing song info (album art, artist, album)~~
  - ~~Ability to get to song album/artist from info view~~
  - ~~Clickable, selectable seek bar~~
  - ~~Shuffle button~~
  - ~~Repeat button~~

#### Queue button:
  - ~~Popout menu with re-orderable currently playing queue~~

#### Search bar:
  - ~~Editable text area to input search~~
  - ~~Menu with search results~~
  - ~~Ability to get to search result items in main window~~
  - ~~Context menu item: show in songs/artists/genre/album~~
  - ~~Dynamically re-sizable window based on amount of results~~
  - ~~Ability to resize window~~


### Main Window Buttons

#### Recently Added
- ~~Inherit albums view, but sort by most recently added songs~~
- Option to view 'recently added' as albums, songs, or artists view

#### Artists
- ~~Scrollable, selectable artist list~~
-~~Song list from selected artists~~

#### Albums
- ~~Main window of album thumbnails, alphabetical~~
- ~~Popout album view with large album artwork and list of songs~~
- ~~Sort by Artist/Album~~
- ~~Album art size slider~~
- ~~Album song selection fly-out menu:~~
  - ~~Dynamically resizing columns~~
  - ~~Album length and song count info~~
  - ~~High quality album art~~
  - ~~Artist, genre and year with ability to click artist or genre~~
- ~~Improve load time~~
- ~~Cache the albums list (entire list and image data)~~
- ~~Loading indicator when building album cache~~

#### Songs
- ~~List of all songs in a scrollable, selectable lists~~

#### Genres
- ~~Scrollable, selectable genre list~~
- ~~Song list from selected genres~~

#### Playlists
- ~~Scrollable, selectable playlist list~~
- ~~Add/remove buttons~~
- ~~Import/export buttons~~
- ~~Ability to pin playlists to the main button menu~~

#### Recently Played
- ~~Similar to songs, but only shows recently played tracks~~

#### Queue
- ~~List of queued songs in a scrollable, selectable list~~
- ~~Queue action buttons~~
- ~~Ability to re-order songs~~

#### Add Music
- ~~Simple button to recursively scan a given folder, then add it to the library~~
- ~~Hidden by default, can be shown in main window (Settings > Playback)~~

#### Re-Scan Library
- ~~Simple button to re-scan the current library folder(s)~~
- ~~Hidden by default, can be shown in main window (Settings > Playback)~~

#### Remove Music
- ~~Simple button to remove a given folder from the library~~
- ~~Hidden by default, can be shown in main window (Settings > Playback)~~

#### Clear Settings
- ~~Simple button to clear all app settings and libraries~~
- ~~Hidden by default, can be shown in main window (Settings > Playback)~~


### Bottom Row
- ~~Song count~~
- ~~Album count~~
- ~~Time and size calculation~~
- ~~Progress bar for song scanning and other actions~~



## Settings Menu

### General
#### Updates
- ~~Check for updates (tick-box)~~
- ~~Automatically install updates (tick-box)~~
- ~~Launch musicApp after updating (tick-box)~~

#### Language
- ~~Dropdown~~
- Write translations and localization files

#### Import/Export
- ~~Import/Export Settings (two buttons)~~
- Write settings file format and import/export logic

### Playback
#### EQ
- Pre-made EQ options, with options to create, import or export profiles (dropdown)

#### Volume normalization
- ~~Tickbox (on/off)~~
- Write code-behind
- Scan all button to build normalization cache
- Clear cache button

#### Cross-fade songs
- ~~Start time (slider)~~
- ~~Length (text box)~~

#### Audio
- ~~Multiple audio backends (dropdown)~~
- ~~Sample rate (dropdown)~~
- ~~Bits per sample (dropdown)~~

### Library
#### Actions
- ~~Add Music~~
- ~~Re-Scan Library~~
- ~~Remove Music~~
- ~~Clear Settings~~
- ~~Tick-boxes for each to show in sidebar~~

#### Scan for missing album art
- ~~Library wide album art scan with multiple album art backends/fallbacks~~
- ~~Progress bar fly-out~~

#### File Storage
- ~~Music library location~~
- ~~Settings location~~
- Change media storage location

#### Import/Export
- Library import/export (two buttons)

### Keyboard Shortcuts
- Scrollable grid view with each shortcut as an item in the list

### Theme/UI
- Color, with options to create, import or export profiles (dropdown)
- Spacing, with options to create, import or export profiles (dropdown)
- Size, with options to create, import or export profiles (dropdown)
- List size, with options to create, import or export profiles (dropdown)
- Toggle donation links (tick-box)

### About
- ~~Version info (e.g. musicApp v0.1.0 dollyShakeswerve x64)~~
- ~~Project link (https://github.com/fosterbarnes/musicApp)~~
- ~~Issues link (https://github.com/fosterbarnes/musicApp/issues/new)~~
- Donation link (https://buymeacoffee.com/fosterbarnes)



## General

### UI Stuff
- ~~Unnecessary space between the scroll bar and track menu in artist/genre view~~

### General Concerns
- ~~Split `MainWindow.xaml.cs` into multiple components~~
- Cut down on if statements. Use more switch/loops
- ~~Use hard-coded custom "pop-ups" and info menus rather than built in windows pop-ups~~
- Improve startup load times
- Improve album art load time (WIP)

### Bugs
- General sluggishness/startup time (WIP)
- Window sized is not always properly remembered and restored
- Queue does not work as intended and needs fixing (WIP)

### Right-Click Context Menu

#### General Items
- ~~Play Next~~
- ~~Add to Queue~~
- ~~Add to Playlist:~~
  -  ~~New playlist~~
  -  ~~Add to existing~~
- ~~Show in Artists~~
- ~~Show in Albums~~
- ~~Show in Songs~~
- ~~Show in Queue~~
#### Info/Metadata
  
  ##### Top Info Bar
  - ~~Song name~~
  - ~~Artist name~~
  - ~~Album name~~
  - ~~Album art~~
  
  ##### Details
  - ~~Song title (text field)~~
  - ~~Artist (text field)~~
  - ~~Album (text field)~~
  - ~~Album artist (text field)~~
  - ~~Composer (text field)~~
  - ~~Genre (dropdown menu)~~
  - ~~Year (text field)~~
  - ~~Track "#" of "#" (two text fields)~~
  - ~~Disc number "#" of "#" (two text fields)~~
  - ~~Compilation (tick box)~~
  - ~~Favorite (tick box)~~
  - ~~Bpm (text box)~~
  - ~~Play Count (text field)~~
  - ~~Ability to edit metadata~~

  ##### Artwork
  - ~~Display full size, resizable artwork~~
  - ~~Ability to change album art~~
  - ~~Button to scan for missing artwork~~

  ##### Lyrics
  - ~~Display lyrics if available~~
  - ~~Ability to add lyrics~~

  ##### Options
  - ~~Open in MP3Tag~~
  - Start/Stop Position
  - Remember playback position
  - Skip when shuffling
  - Volume slider for individual tracks
  - EQ

  ##### Sorting
  - Change sorting options for specific items

  ##### File
  - ~~Show file-type, length, size, bit rate, and other info~~

- ~~Show in Explorer~~
- ~~Remove from Library~~
- ~~Delete~~

##### Artists
- ~~Hide "Show in Artists"~~

##### Albums
- ~~Hide "Show in Albums"~~

##### Songs
- ~~Hide "Show in Songs"~~

##### Genres
- ~~Use all general items~~

##### Playlists
- ~~Use all general items~~

##### Recently Played
- ~~Hide "Show in Albums"~~

##### Queue
- ~~Hide "Show in Queue"~~

#### Planned Features
- Visualizer
- _POSSIBLE_ iTunes library import support
- Audio file converting/compressing
- Optional metadata correction/cleanup
- Robust queuing system/menu. I like to make "on the fly" playlists with my queues, so it must be as seamless and robust as possible (WIP)
- "Like" system and liked tracks menu
- Keyboard shortcuts for actions like "play/pause", "skip" "volume up/down" etc. These should work whether or not the app window is focused
- Mini-player window that can be open in addition to the main window, or as a replacement to the main window
- Support for multiple libraries
- Option to add "Add to musicApp" to windows right-click context menu
- Spotify integration
- Last.fm support
- Possible media server integration (primarily emby/jellyfin because that's what I use)

#### Backend/Boring Stuff
- Automatic updates integrated with GitHub releases (WIP)
- ~~Installer~~
- ~~Option for portable version~~