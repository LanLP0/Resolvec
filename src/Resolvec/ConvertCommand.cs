using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFMpegCore;
using FFMpegCore.Enums;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Resolvec;

[Description("Convert video/audio files from/into Davinci Resolve friendly format (uses ffmpeg under the hood)")]
public sealed class ConvertCommand : AsyncCommand<ConvertCommandSettings>
{
    private static readonly string[] AutoScanExtensions =
        [".mp4", ".avi", ".mov", ".mjpg", ".mkv", ".flv", ".wmv", ".wav", ".mp3", ".opus", ".aac", ".ogg", ".m4a"];

    private static readonly Dictionary<string, CodecSettings> CodecPresets = new()
    {
        { "mp4", new CodecSettings("h264", "mp4", "yuv420p", "aac", "mp3") }
    };

    private static readonly Dictionary<string, string[]> FFMpegOptions = new()
    {
        // Video
        { "av1_nvenc", ["-tune hq", "-cq 20", "-preset p4"] },
        { "libsvtav1", ["-crf 24", "-preset 4", "-svtav1-params fast-decode=1"] },
        { "libx264", ["-preset medium", "-crf 20"] },

        // Audio
        { "pcm_s24le", ["-ar 4800"] },
        { "aac", ["-b:a 160k", "-movflags +faststart"] }
    };

    protected override async Task<int> ExecuteAsync(CommandContext context, ConvertCommandSettings settings,
        CancellationToken cancellationToken)
    {
        // Console.WriteLine(JsonSerializer.Serialize(FFMpeg.GetCodecs()));
        // return 0;

        var paths = GetAllVideoAudioFiles(settings.Paths, settings);
        if (paths.Count is 0)
        {
            AnsiConsole.MarkupLine("[red]No valid path entered. Please check your input paths.[/]");
            return -1;
        }

        if (paths.Count > 1 && settings.OutputPath.IsSet && !Directory.Exists(settings.OutputPath.Value))
        {
            // Multiple file -> Output must be a directory
            AnsiConsole.MarkupLine("[red]Output path does not exists.[/]");
            return -1;
        }

        if (settings.Backward.IsSet)
        {
            settings.Backward.Value ??= "mp4";
            if (!CodecPresets.TryGetValue(settings.Backward.Value, out var codec))
            {
                AnsiConsole.MarkupLine("[red]Backward codec unsupported. Available values are: {0}[/]",
                    string.Join(", ", CodecPresets.Keys));
                return -1;
            }

            settings.VideoCodec = codec.VideoCodec;
            settings.VideoExtension = codec.VideoExtension;
            settings.PixelFormat = codec.PixelFormat;
            settings.AudioCodec = codec.AudioCodec;
            settings.AudioExtension = codec.AudioExtension;
        }

        try
        {
            FixCodec(settings);
        }
        catch (InvalidOperationException exception)
        {
            AnsiConsole.MarkupLine("[red]Error: {0}[/]", Markup.Escape(exception.Message));
            return -1;
        }

        AnsiConsole.MarkupLine("""
                               [white bold]Video     :[/] {0} - {1} -> [blue].{2}[/] 
                               [white bold]Audio     :[/] {3} -> [blue].{4}[/]
                               [white bold]Backward  :[/] [blue]{5}[/]
                               [white bold]No. Files :[/] [blue]{6}[/]
                               """,
            settings.PixelFormat, settings.VideoCodec, settings.VideoExtension,
            settings.AudioCodec, settings.AudioExtension, settings.Backward.IsSet, paths.Count);

        if (settings.OutputPath.IsSet && settings.OutputPath.Value is null)
        {
            settings.OutputPath.Value = Path.GetDirectoryName(paths[0]);
            AnsiConsole.MarkupLine("[blue bold]Output directory: {0}[/]", settings.OutputPath);
        }

        var count = 1;
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            try
            {
                sb.Clear();
                await ConvertOne(path, settings, count++, sb);
            }
            catch (Exception e)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Error: {e.Message}[/]");
                continue;
            }
        }

        return 0;
    }

    private async Task ConvertOne(string input, ConvertCommandSettings settings, int count, StringBuilder errorBuilder)
    {
        var fData = FFProbe.Analyse(input);
        var isVideo = fData.VideoStreams.Count > 0;
        AnsiConsole.Markup("[white bold]{0}[/]", count);
        AnsiConsole.Markup(isVideo ? " [blue]V[/] " : " [yellow]A[/] ");
        AnsiConsole.Write(Path.GetFileName(input));

        // Find the correct output path
        var outputPath = settings.OutputPath.Value;
        string newName;
        if (!settings.Backward.IsSet) // Add suffix always
        {
            newName =
                $"{Path.GetFileNameWithoutExtension(input)}.{settings.Suffix}.{(isVideo ? settings.VideoExtension : settings.AudioExtension)}";
        }
        else // Strip suffix
        {
            var ext = Path.GetExtension(input);
            if (input.EndsWith($".{settings.Suffix}{ext}"))
                newName = string.Concat(Path.GetFileName(input).Split('.')[..^2]) + '.' +
                          (isVideo ? settings.VideoExtension : settings.AudioExtension);
            else newName = $"{Path.GetFileNameWithoutExtension(input)}{ext}";
        }

        if (outputPath is null)
        {
            outputPath = Path.Join(Path.GetDirectoryName(input), newName);
        }
        else
        {
            // If a path to a directory was given
            // /some/path/directory (not exist yet)/
            if (Directory.Exists(outputPath) || Path.GetFileName(outputPath) == string.Empty)
                outputPath = Path.Join(outputPath, newName);

            // Otherwise, it is already a path to a file
        }
        
        var fileExistsSkip = !settings.Force && File.Exists(outputPath);

        AnsiConsole.Markup(isVideo ? " [blue]->[/] " : " [yellow]->[/] ");
        AnsiConsole.MarkupLine(fileExistsSkip ? Path.GetFileName(outputPath) + " [yellow]skipped (file exists)[/]" : Path.GetFileName(outputPath));

        var options = FFMpegArguments
            .FromFileInput(input)
            .OutputToFile(outputPath, settings.Force, config =>
            {
                if (isVideo)
                {
                    config.WithVideoCodec(settings.VideoCodec);
                    config.WithCustomArgument("-vf \"scale=ceil(iw/2)*2:ceil(ih/2)*2\"");
                    config.ForcePixelFormat(settings.PixelFormat);

                    if (!settings.Backward.IsSet)
                        config.WithCustomArgument("-g 30");
                    
                    foreach (var opt in settings.FFMpegVideoOptions)
                        config.WithCustomArgument(opt);
                }

                config.WithAudioCodec(settings.AudioCodec);

                foreach (var opt in settings.FFMpegAudioOptions)
                    config.WithCustomArgument(opt);
                
                config.OverwriteExisting();
                config.WithCustomArgument("-hide_banner");
            })
            .WithLogLevel(FFMpegLogLevel.Error);
        
        if (settings.DryRun)
        {
            // TODO Verbose
            Console.Write("ffmpeg ");
            Console.WriteLine(options.Arguments);
            return;
        }
        
        if (fileExistsSkip)
            return;

        if (Environment.UserInteractive)
        {
            var ffmpegErrored = false;
            await AnsiConsole.Progress().AutoClear(true)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn
                    {
                        CompletedStyle = new Style(Color.Green),
                        FinishedStyle = new Style(Color.Lime),
                        RemainingStyle = new Style(Color.Grey)
                    },
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
            {
                var convert = ctx.AddTask("Convert", maxValue: 100);
                var task = options
                    .WithLogLevel(FFMpegLogLevel.Info)
                    .NotifyOnError(s =>
                    {
                        if (s.StartsWith("Err"))
                            ffmpegErrored = true;
                        
                        errorBuilder.AppendLine(s);
                    })
                    .NotifyOnProgress(t => convert.Value(t), fData.Duration)
                    .ProcessAsynchronously();

                while (!task.IsCompleted)
                {
                    await Task.Delay(1000);
                }
                
                convert.Value(100);
            });
            
            if (ffmpegErrored)
                throw new Exception(errorBuilder.ToString());
        }
        else await options.ProcessAsynchronously();
    }

    /// <summary>
    /// Fix common input codecs into ffmpeg recognisable values (e.g. h264 -> nvenc_h264 or libx264)
    /// </summary>
    /// <param name="settings">Settings to mutate</param>
    /// <exception cref="InvalidOperationException">Unsupported video/audio codec</exception>
    private void FixCodec(ConvertCommandSettings settings)
    {
        var supportedCodecs = FFMpeg.GetCodecs().Select(c => c.Name).ToArray();
        settings.VideoCodec = settings.VideoCodec switch
        {
            "h264" => new[] { "h264_nvenc", "h264_vaapi", "libx264" }.FirstOrDefault(c => supportedCodecs.Contains(c),
                "h264"),
            "h265" => "libx265",
            "av1" => new[] { "av1_nvenc", "av1_vaapi", "libsvtav1" }.FirstOrDefault(c => supportedCodecs.Contains(c),
                "av1"),
            _ => settings.VideoCodec
        };

        if (!supportedCodecs.Contains(settings.VideoCodec))
            throw new InvalidOperationException("Unsupported video codec: " + settings.VideoCodec);
        if (!supportedCodecs.Contains(settings.AudioCodec))
            throw new InvalidOperationException("Unsupported audio codec: " + settings.AudioCodec);

        if (FFMpegOptions.TryGetValue(settings.VideoCodec, out var options) && settings.FFMpegVideoOptions.Count is 0)
        {
            foreach (var opt in options)
                settings.FFMpegVideoOptions.Add(opt);
        }

        if (FFMpegOptions.TryGetValue(settings.AudioCodec, out options) && settings.FFMpegAudioOptions.Count is 0)
        {
            foreach (var opt in options)
                settings.FFMpegAudioOptions.Add(opt);
        }
    }

    /// <returns>Paths to video and audio files (existence checked)</returns>
    private IReadOnlyList<string> GetAllVideoAudioFiles(string[] paths, ConvertCommandSettings settings)
    {
        var result = new List<string>();

        foreach (var path in CongregatePaths(paths))
        {
            if (File.Exists(path))
            {
                result.Add(Path.GetFullPath(path));
                continue;
            }

            if (!Directory.Exists(path))
                continue;

            var files = Directory.GetFiles(path);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file);
                if (!AutoScanExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                if (!settings.Backward.IsSet && file.EndsWith($".{settings.Suffix}{ext}"))
                    continue;
                if (settings.Backward.IsSet && !file.EndsWith($".{settings.Suffix}{ext}"))
                    continue;

                result.Add(file);
            }
        }

        return result;
    }

    private static IEnumerable<string> CongregatePaths(string[] paths)
    {
        for (var i = 0; i < paths.Length;)
        {
            var path = paths[i];

            if (!path.EndsWith('\\'))
            {
                i++;
                yield return path;
            }
            else
            {
                var p = new List<string> { path[..^1] };
                i++;
                while (i < paths.Length && paths[i].EndsWith('\\'))
                {
                    path = paths[i];
                    p.Add(path[..^1]);
                    i++;
                }

                path = paths[i];
                p.Add(path[..^1]);
                i++;
                yield return string.Join(' ', CollectionsMarshal.AsSpan(p));
            }
        }
    }

    private record CodecSettings(
        string VideoCodec,
        string VideoExtension,
        string PixelFormat,
        string AudioCodec,
        string AudioExtension);
}

public sealed class ConvertCommandSettings : CommandSettings
{
    [CommandArgument(0, "<files/directories>")]
    [Description("One or more files/directories to process")]
    public string[] Paths { get; init; } = [];

    [CommandOption("-o|--out [path]")]
    [Description(
        "By default, output file will be placed alongside original file. Set output file path for single file or a directory for multiple files. Defaults to directory of first file if no path provided.")]
    public FlagValue<string?> OutputPath { get; set; }

    [CommandOption("-f|--force")]
    [Description("Force conversion of files")]
    [DefaultValue(false)]
    public bool Force { get; init; } = false;

    [CommandOption("-b|--backward [format]")]
    [Description("Convert files back to [[format]] (Default: format = mp4)")]
    [DefaultValue("mp4")]
    public FlagValue<string?> Backward { get; init; }

    [CommandOption("-v|--video-ext <ext>")]
    [Description("Video extension (ffmpeg supported codec)")]
    [DefaultValue("mp4")]
    public string VideoExtension { get; set; } = "mp4";

    [CommandOption("-a|--audio-ext <ext>")]
    [Description("Audio extension")]
    [DefaultValue("wav")]
    public string AudioExtension { get; set; } = "wav";

    [CommandOption("-m|--video-codec <codec>")]
    [Description("Video codec (ffmpeg supported codec)")]
    [DefaultValue("av1")]
    public string VideoCodec { get; set; } = "av1";

    [CommandOption("-n|--audio-codec <codec>")]
    [Description("Audio codec")]
    [DefaultValue("pcm_s24le")]
    public string AudioCodec { get; set; } = "pcm_s24le";

    [CommandOption("-p|--px-format <format>")]
    [Description("Pixel format")]
    [DefaultValue("yuv420p10le")]
    public string PixelFormat { get; set; } = "yuv420p10le";

    [CommandOption("-s|--suffix <suffix>")]
    [Description("Suffix to append to the end of the file \".suffix.ext\"")]
    [DefaultValue("resolve")]
    public string Suffix { get; init; } = "resolve";

    [CommandOption("--dry-run")]
    [Description("Don't convert, only show conversions that is about to happen")]
    [DefaultValue(false)]
    public bool DryRun { get; init; } = false;

    [CommandOption("--ff-video <option>")]
    [Description("Options to be passed directly to ffmpeg (Video)")]
    public List<string> FFMpegVideoOptions { get; set; } = [];
    
    [CommandOption("--ff-audio <option>")]
    [Description("Options to be passed directly to ffmpeg (Audio)")]
    public List<string> FFMpegAudioOptions { get; set; } = [];
}
