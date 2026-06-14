<h1 align="center" href="#Resolvec">Resolvec</h1>

<p align="center" style="display: flex; flex-direction: column; justify-content: center; align-items: center;">
  <img style="place-self: center" src="https://github.com/LanLP0/Resolvec/actions/workflows/build.yaml/badge.svg" alt="Build Status">
<p align="center">A CLI tool to deal with everything realated to Davinci Resolve.</p>
</p>

## Features
Currently, `resolvec` came with a single function `resolvec convert`. This is used to convert any video/audio file from almost any formats into Davinci Resolve compatible ones (NLE optimized).

This came from the need that the free version of Resolve on Linux has no support for common codecs like h264/h265. `resolvec convert` help to convert to/from fast av1 / prores automatically with minimal user input.

```sql
> resolvec convert /data/Resolve/Projects/My Game/Media

Video     : yuv420p10le - av1_nvenc -> .mp4 
Audio     : pcm_s24le -> .wav
Backward  : False
No. Files : 2
1 A My Game 260608_220731.m4a -> My Game 260608_220731.resolve.wav skipped (file exists)
2 V My Game 2026-06-08 22-07-32.mp4 -> My Game 2026-06-08 22-07-32.resolve.mp4
                                                                
Convert ━━━━━━━                              10% 00:06:52 ⣷
```

## Install
If you have `dotnet` (>= v10.0.0) installed on your system, go to [Releases](https://github.com/LanLP0/Resolvec/releases) and download the latest `.nupkg` file. You can run `dotnet tool install --global resolvec --add-source {DownloadDirectory}` to install the tool.

If you don't have `dotnet` installed, go to [Releases](https://github.com/LanLP0/Resolvec/releases) and grab the file specific to your Operating System, place them in a dedicated folder to storing programs, then add that folder to your system's `PATH`. Now you can use `resolvec convert` anywhere.

### For all installs,
`resolvec` requires FFMpeg to work. Please install and add it to your system's `PATH` via [this link](https://ffmpeg.org/download.html)

## Development
Contributions for new feature/improving existing feature are welcome!

Some of the things that can be improved:
- [ ] Verbosity control for `resolvec` (`-v|--verbose|--log-level`)
- [ ] Global progress bar that show stats and remaining time for all files (currently only show for individual tasks)
- [ ] Run convert on multiple gpus/cpus at once

### To get started:
```shell
git clone https://github.com/LanLP0/Resolvec
cd Resolvec/src/Resolvec
dotnet run # Run the program
```


This program and its source code is under the GPLv3 license. See [LICENSE](LICENSE) for more detail.