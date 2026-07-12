# ToDo

## Main Window
### Main Window Buttons
#### Recently Added
- Option to view 'recently added' as albums, songs, or artists view

## Settings Menu
### General
#### Language
- Write translations and localization files

### Playback
#### EQ
- Pre-made EQ options, with options to create, import or export profiles (dropdown)

#### Volume normalization
- Write code-behind
- Scan all button to build normalization cache
- Clear cache button

### Library
#### File Storage
- Change media storage location

#### Import/Export
- Library import/export (two buttons)

### Theme/UI
- Color, with options to create, import or export profiles (dropdown)
- Spacing, with options to create, import or export profiles (dropdown)
- Size, with options to create, import or export profiles (dropdown)
- List size, with options to create, import or export profiles (dropdown)

## General
### General Concerns
- Cut down on if statements. Use more switch/loops

### Bugs
- Window sized is not always properly remembered and restored

### Right-Click Context Menu
#### Info/Metadata
##### Options
- Start/Stop Position
- Remember playback position
- Skip when shuffling
- Volume slider for individual tracks
- EQ

##### Sorting
- Change sorting options for specific items

#### Planned Features
- Visualizer
- _POSSIBLE_ iTunes library import support
- Audio file converting/compressing
- Optional metadata correction/cleanup
- Robust queuing system/menu. I like to make "on the fly" playlists with my queues, so it must be as seamless and robust as possible (WIP)
- "Like" system and liked tracks menu
- Keyboard shortcuts for actions like "play/pause", "skip" "volume up/down" etc. These should work whether or not the app window is focused
- Support for multiple libraries
- Option to add "Add to musicApp" to windows right-click context menu
- Spotify integration
- Last.fm support
- Possible media server integration (primarily emby/jellyfin because that's what I use)

#### Backend/Boring Stuff
- Automatic updates integrated with GitHub releases (WIP)
