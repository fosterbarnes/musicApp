# ![Icon](https://i.postimg.cc/d3c9vxzF/Music-App-Icon24x24.png) musicApp - an offline music player

https://musicapp.info/

Desktop music player for Windows with a focus on functionality, efficiency, clean UI, and customization.

Currently in a pre-release state, so not all intended features or behaviors have been added or thoroughly tested. You can track the current progress below.
## Progress

![Progress Bar](https://progress-bars.entcheneric.com/bar.svg?progress=80&backgroundColor=%23212121&height=40&width=800&borderRadius=10&striped=false&animated=false&gradientAnimated=false&animationSpeed=1&stripeAnimationSpeed=1&gradientAnimationSpeed=1&initialAnimationSpeed=1&color=%23705399)  
**136 / 169 tasks complete (80.5%)**
[Tasks.md](https://github.com/fosterbarnes/musicApp/blob/main/.md/Tasks.md#main-window)

[34,737](https://github.com/fosterbarnes/musicApp/blob/main/.md/scc.txt) lines of code and counting...


## Downloads

This project is in early development, bugs are expected. Windows only (for now).

<table border="0">
<tbody>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/v0.3.2/musicApp-v0.3.2-kitBunga-x64-installer.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_x64.svg" width="180" height="auto" alt="x64 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/v0.3.2/musicApp-v0.3.2-kitBunga-x86-installer.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_x86.svg" width="180" height="auto" alt="x86 installer"/></a></td>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/v0.3.2/musicApp-v0.3.2-kitBunga-arm64-installer.exe"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_arm.svg" width="180" height="auto" alt="arm64 installer"/></a></td>
</tr>
<tr>
<td valign="top"><a href="https://github.com/fosterbarnes/musicApp/releases/download/v0.3.2/musicApp-v0.3.2-kitBunga-portable.zip"><img src="https://raw.githubusercontent.com/fosterbarnes/musicApp/refs/heads/main/.resources/svg/download_portable.svg" width="180" height="auto" alt="portable .zip"/></a></td>
</tr>
</tbody>
</table>

## Screenshots

| <h3>Albums</h3> |
|:---:|
| ![Albums](./.resources/scr/albums.png) |

| <h3>Songs</h3> |
|:---:|
| ![Songs](./.resources/scr/songs.png) |

| <h3>Artists</h3> |
|:---:|
| ![Artists](./.resources/scr/artists.png) |

<details>
<summary>More Screenshots:</summary>

| <h3>Genres</h3> |
|:---:|
| ![Genres](./.resources/scr/genres.png) |

| <h3>Playlists</h3> |
|:---:|
| ![Playlists](./.resources/scr/playlists.png) |

| <h3>Recently Played</h3> |
|:---:|
| ![Recently Played](./.resources/scr/recent.png) |

| <h3>Queue</h3> |
|:---:|
| ![Queue](./.resources/scr/queue.png) |

| <h3>Search</h3> |
|:---:|
| ![Search](./.resources/scr/search.png) |

| <h3>Info</h3> |
|:---:|
| ![Info](./.resources/scr/info.png) |

</details>

## Why does this exist?

I dislike streaming services. I have tried many music player apps like Foobar2000,
Musicbee, AIMP, Clementine, Strawberry, etc. and just they're not for me. No disrespect to the creators, they're clearly very well-built apps. I like (tolerate) iTunes, and while it IS functional and has a ui that I find better than the alternatives, it's very out of date, sluggish overall and can cause other weird issues with other applications.

To be honest, this app is made so I can use as my daily music player. HOWEVER, if you agree with one or more of the previous statements, this app may also be for you too. It's made for Windows with WPF in C#, for this reason, Linux/macOS versions are not currently planned. My main concern is efficiency for my personal daily driver OS (Windows 10) not cross compatibility. The thought of making such a detailed and clean UI in Rust (my cross compat. language of choice) gives me goosebumps and shivers, ergo: WPF in C#, using XAML for styling.

## Compatibility

| Platform  | Architecture   |
|------------|-----------------|
| Windows 10 | x86, x64, arm64 |
| Windows 11 | x86, x64, arm64 |

## Planned Ports

| Platform  | Architecture   |
|------------|-----------------|
| Debian Linux | x64, arm64 |
| macOS | x64, arm64 |

## Support

If you have any issues, create an issue from the [Issues](https://github.com/fosterbarnes/rustitles/issues) tab and I will get back to you as quickly as possible.

If you'd like to support me, follow me on twitch:
[https://www.twitch.tv/fosterbarnes](https://www.twitch.tv/fosterbarnes)