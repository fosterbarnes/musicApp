# ToDo

## Main Window

### Title Bar

#### Song info view:

#### Queue button:

#### Search bar:

### Main Window Buttons

#### Recently Added
- Option to view 'recently added' as albums, songs, or artists view

#### Artists

#### Albums

#### Songs

#### Genres

#### Playlists

#### Recently Played

#### Queue

#### Add Music

#### Re-Scan Library

#### Remove Music

#### Clear Settings

### Bottom Row

## Settings Menu

### General
#### Updates

#### Language
- Write translations and localization files

#### Import/Export
- Write settings file format and import/export logic

### Playback
#### EQ
- Pre-made EQ options, with options to create, import or export profiles (dropdown)

#### Volume normalization
- Write code-behind
- Scan all button to build normalization cache
- Clear cache button

#### Cross-fade songs

#### Audio

### Library
#### Actions

#### Scan for missing album art

#### File Storage
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
- Donation link (https://buymeacoffee.com/fosterbarnes)

## General

### UI Stuff

### General Concerns
- Cut down on if statements. Use more switch/loops
- Improve startup load times
- Improve album art load time (WIP)

### Bugs
- General sluggishness/startup time (WIP)
- Window sized is not always properly remembered and restored
- Queue does not work as intended and needs fixing (WIP)

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
