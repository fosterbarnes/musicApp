# ToDo

## Main Window

### Title Bar

#### Song info view:

#### Queue button:
  - Popout menu with re-orderable currently playing queue

#### Search bar:

### Main Window Buttons

#### Recently Added
- Inherit albums view, but sort by most recently added songs
- Option to view 'recently added' as albums, songs, or artists view

#### Artists

#### Albums
- Improve load time
- Cache the albums list (entire list and image data)
- Loading indicator when building album cache

#### Songs

#### Genres

#### Playlists

#### Recently Played

#### Queue
- Ability to re-order songs

#### Add Music Folder
- Will most likely be moved to a library management window/menu

#### Re-Scan Library
- Will most likely be moved to a library management window/menu

#### Remove Music Folder
- Will most likely be moved to a library management window/menu

#### Clear Settings
- Will most likely be moved to a library management window/menu

### Bottom Row

## Settings Menu

### EQ
  - Pre-made EQ options
  - Custom EQ selector
  - Save/load custom EQs
### Themes/colors
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

### UI Stuff

### General Concerns
- Cut down on if statements. Use more switch/loops
- Improve startup load times
- Improve album art load time

### Bugs
- General sluggishness/startup time
- Window sized is not always properly remembered and restored
- Queue does not work as intended and needs fixing

### Right-Click Context Menu

#### General Items
#### Info/Metadata

  ##### Top Info Bar

  ##### Details

  ##### Artwork

  ##### Lyrics

  ##### Options
  - Start/Stop Position
  - Remember playback position
  - Skip when shuffling
  - Volume slider for individual tracks
  - EQ

  ##### Sorting
  - Change sorting options for specific items

  ##### File

##### Artists

##### Albums

##### Songs

##### Genres

##### Playlists

##### Recently Played

##### Queue

#### Planned Features
- Visualizer
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
