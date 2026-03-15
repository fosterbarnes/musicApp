## Main Window

### Title Bar
- ~~Play/pause/skip buttons~~
- ~~Volume control~~
##### Song info view:
  - ~~Currently playing song info (album art, artist, album)~~
  - Ability to get to song album/artist from info view
  - ~~Clickable, selectable seek bar~~
  - ~~Shuffle button~~
  - ~~Repeat button~~
##### Queue button:
  - Popout menu with re-orderable currently playing queue
##### Search bar:
  - ~~Editable text area to input search~~
  - ~~Menu with search results~~
  - ~~Ability to get to search result items in main window~~
  - Context menu item: show in songs/artists/genre/album


### Main Window Buttons

#### Artists
- ~~Scrollable, selectable artist list~~
-~~Song list from selected artists~~

#### Albums
- ~~Main window of album thumbnails, alphabeticals~~
- ~~Popout album view with large album artwork and list of songss~~

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
- Ability to re-order songs

#### Add Music Folder
- ~~Simple button to recursively scan a given folder, then add it to the library~~
- Will most likely be moved to a library management window/menu

#### Re-Scan Library
- ~~Simple button to re-scan the current library folder(s)~~
- Will most likely be moved to a library management window/menu

#### Remove Music Folder
- ~~Simple button to remove a given folder from the library~~
- Will most likely be moved to a library management window/menu

#### Clear Settings
- ~~Simple button to clear all app settings and libraries~~
- Will most likely be moved to a library management window/menu


### Bottom Row
- ~~Song count~~
- ~~Album count~~
- ~~Time and size calculation~~
- ~~Progress bar for song scanning and other actions~~




## Settings Menu
##### EQ
  - Pre-made EQ options
  - Custom EQ selector
  - Save/load custom EQs
##### Themes/colors
  - Pre-made color options
  - Custom color picker
  - Multiple custom save slots
- Multiple audio backends
- Cross-fading between songs
- Volume normalization
- Sample rate
- Library import/export
- Language
- Change settings storage location
- Change media storage location
- Check for updates
- Keyboard shortcuts
- Toggle donation links




## General
#### UI Stuff
- ~~Unnecessary space between the scroll bar and track menu in artist/genre view~~

#### General Concerns
- Split `MainWindow.xaml.cs` into multiple components
- Cut down on if statements. Use more switch/loops
- ~~Use hard-coded custom "pop-ups" and info menus rather than built in windows pop-ups~~

#### Bugs

#### Planned Features
- Ability to edit metadata
- Visualizer
- Playlist import support
- _POSSIBLE_ iTunes library import support
- Audio file converting/compressing
- Album art scraper
- Optional metadata correction/cleanup
- Robust queuing system/menu. I like to make "on the fly" playlists with my queues, so it must be as seamless and robust as possible
- "Like" system and liked tracks menu
- Keyboard shortcuts for actions like "play/pause", "skip" "volume up/down" etc. These should work whether or not the app window is focused
- Mini-player window that can be open in addition to the main window, or as a replacement to the main window
- Support for multiple libraries
- Option to add "Add to musicApp" to windows right-click context menu
- Spotify integration
- Last.fm support
- Possible media server integration (primarily emby/jellyfin because that's what I use)

#### Backend/Boring Stuff
- Automatic updates integrated with GitHub releases
- Installer
- Option for portable version