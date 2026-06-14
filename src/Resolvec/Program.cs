using Resolvec;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("resolvec");
    config.SetApplicationVersion("1.0.0");
    
    config.AddExample("convert <file>");
    config.AddExample("convert <directory>");
    config.AddExample("convert --back <file>");
    
    config.AddCommand<ConvertCommand>("convert");
    
#if DEBUG
    config.PropagateExceptions();
    // config.ValidateExamples();
#endif
});

return app.Run(args);